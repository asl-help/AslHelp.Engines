using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Finds the signature routes that the versions of a group share.
/// </summary>
internal static class SharedRoutes
{
    /// <summary>
    ///     Reads the executable of each version once and searches the routes of each global.
    /// </summary>
    /// <param name="versions">The versions.</param>
    /// <param name="logger">The logger for an executable that fails to read.</param>
    /// <param name="cancellationToken">The token that stops the search.</param>
    /// <returns>
    ///     The routes by global, in the order of the globals of the versions.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static IReadOnlyDictionary<string, GlobalRoutes> Find(
        IReadOnlyList<RouteVersion> versions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        List<VersionCode> readable = [];
        Dictionary<string, string> unreadable = [with(StringComparer.Ordinal)];

        foreach (var version in versions)
        {
            // Check here too, because a version without a site never gets to the check of a candidate.
            cancellationToken.ThrowIfCancellationRequested();

            if (version.Globals.Count == 0)
            {
                continue;
            }

            try
            {
                readable.Add(new VersionCode(version, CodeImage.Read(version.ExecutablePath)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Keep going, because PEReader throws several types for a corrupt file and the other versions can still share a route.
                unreadable[version.Version] = exception.Message;
                logger.ExecutableNotRead(version.Version, exception.Message);
            }
        }

        Dictionary<string, GlobalRoutes> routes = [with(StringComparer.Ordinal)];

        foreach (string global in versions.SelectMany(static version => version.Globals.Keys).Distinct(StringComparer.Ordinal))
        {
            routes[global] = new GlobalSearch(global, readable, cancellationToken).Run(versions, unreadable);
        }

        return routes;
    }
}

// Searches the routes of 1 global, 1 route per round, until no candidate covers a version that is left.
file sealed class GlobalSearch(
    string global,
    List<VersionCode> readable,
    CancellationToken cancellationToken)
{
    private readonly List<VersionCode> _remaining = readable.FindAll(code => code.Version.Globals.ContainsKey(global));
    private readonly List<SiteWindow> _windows = [.. readable.SelectMany(code => code.WindowsOf(global))];

    public GlobalRoutes Run(IReadOnlyList<RouteVersion> versions, Dictionary<string, string> unreadable)
    {
        List<Route> routes = [];
        HashSet<string> covered = [with(StringComparer.Ordinal)];

        while (_remaining.Count > 0 && Pick() is { } picked)
        {
            List<string> routeVersions = [];
            Dictionary<string, string> reached = [with(StringComparer.Ordinal)];

            foreach (var code in _remaining)
            {
                if (picked.Reached.TryGetValue(code, out uint rva))
                {
                    routeVersions.Add(code.Version.Version);
                    reached[code.Version.Version] = $"0x{rva:X}";
                    _ = covered.Add(code.Version.Version);
                }
            }

            routes.Add(new Route(picked.Candidate.Forward, picked.Candidate.Offset, routeVersions, reached));
            _ = _remaining.RemoveAll(picked.Reached.ContainsKey);
        }

        Dictionary<string, string> uncovered = [with(StringComparer.Ordinal)];

        foreach (var version in versions)
        {
            if (!covered.Contains(version.Version))
            {
                uncovered[version.Version] = version.Globals.ContainsKey(global)
                    ? unreadable.GetValueOrDefault(version.Version) ?? SR.UncoveredNoCandidate
                    : SR.UncoveredNotFound;
            }
        }

        return new GlobalRoutes(routes, uncovered.Count > 0 ? uncovered : null);
    }

    // Tries the windows of every version only after the sites of the versions left, because they are for a load that linear decoding missed.
    // A register mask adds nothing for a version with its own site, because every match of the strict form is also a match of the masked form.
    private Picked? Pick()
    {
        // Stop growing at the versions with a site, because a version without one adds a scan to every candidate and the window tiers still search it.
        int withSite = _remaining.Count(code => code.WindowsOf(global).Any());

        return PickFrom(CandidatesOfSites, withSite)
            ?? PickFrom(instructions => CandidatesOfWindows(MaskTier.Strict, instructions), _remaining.Count)
            ?? PickFrom(instructions => CandidatesOfWindows(MaskTier.Register, instructions), _remaining.Count);
    }

    private Picked? PickFrom(Func<int, IEnumerable<CandidateVersions>> candidates, int enough)
    {
        Picked? best = null;

        // Grow from the fewest instructions, because the search stops once a candidate covers enough versions.
        for (int instructions = 1; instructions <= RouteCandidates.MostInstructions && (best is null || best.Reached.Count < enough); instructions++)
        {
            // Try only the first candidates, because a global can have thousands and a version that nothing covers would try them all.
            var ordered = candidates(instructions)
                .OrderByDescending(static entry => entry.Versions.Count)
                .ThenBy(static entry => entry.Candidate.Bytes)
                .ThenBy(static entry => entry.Candidate.Forward, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Candidate.Offset)
                .Take(RouteCandidates.MostCandidates);

            foreach (var entry in ordered)
            {
                // Skip a candidate that cannot win with all of its versions, because each version it searches costs a scan of a whole file.
                if (best is not null && !best.IsWorseThan(entry.Candidate, entry.Versions.Count))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                Dictionary<VersionCode, uint> reached = [];

                foreach (var code in entry.Versions)
                {
                    if (code.TryReach(global, entry.Candidate, out uint rva))
                    {
                        reached[code] = rva;
                    }
                }

                if (reached.Count > 0 && (best is null || best.IsWorseThan(entry.Candidate, reached.Count)))
                {
                    best = new Picked(entry.Candidate, reached);

                    if (reached.Count == _remaining.Count)
                    {
                        return best;
                    }
                }
            }
        }

        if (best is not null)
        {
            // Search the other versions for the candidate, because linear decoding can miss 1 load of a version with other sites.
            foreach (var code in _remaining)
            {
                if (!best.Reached.ContainsKey(code) && code.TryReach(global, best.Candidate, out uint rva))
                {
                    best.Reached[code] = rva;
                }
            }
        }

        return best;
    }

    private Dictionary<RouteCandidate, CandidateVersions>.ValueCollection CandidatesOfSites(int instructions)
    {
        Dictionary<RouteCandidate, CandidateVersions> seen = [];

        foreach (var code in _remaining)
        {
            foreach (var window in code.WindowsOf(global))
            {
                foreach (var candidate in RouteCandidates.Enumerate(window, MaskTier.Strict, instructions))
                {
                    if (!seen.TryGetValue(candidate, out var entry))
                    {
                        seen[candidate] = new CandidateVersions(candidate, [code]);
                    }
                    else if (entry.Versions[^1] != code)
                    {
                        entry.Versions.Add(code);
                    }
                }
            }
        }

        // Count the versions without a site for every candidate, because linear decoding missed their loads and any candidate can still cover them.
        var withoutSite = _remaining.FindAll(code => !code.WindowsOf(global).Any());

        foreach (var entry in seen.Values)
        {
            entry.Versions.AddRange(withoutSite);
        }

        return seen.Values;
    }

    private Dictionary<RouteCandidate, CandidateVersions>.ValueCollection CandidatesOfWindows(MaskTier tier, int instructions)
    {
        Dictionary<RouteCandidate, CandidateVersions> seen = [];

        foreach (var window in _windows)
        {
            foreach (var candidate in RouteCandidates.Enumerate(window, tier, instructions))
            {
                _ = seen.TryAdd(candidate, new CandidateVersions(candidate, _remaining));
            }
        }

        return seen.Values;
    }
}

// Pairs a candidate with the versions to search.
file sealed record CandidateVersions(
    RouteCandidate Candidate,
    List<VersionCode> Versions);

// Tracks the best candidate so far with the RVA it reaches in each version.
file sealed record Picked(
    RouteCandidate Candidate,
    Dictionary<VersionCode, uint> Reached)
{
    // Ranks by most versions, then fewest instructions, then fewest bytes, then ordinal text, then offset, so the result is deterministic.
    public bool IsWorseThan(RouteCandidate other, int covered)
    {
        int order = covered.CompareTo(Reached.Count);

        if (order == 0)
        {
            order = Candidate.Instructions.CompareTo(other.Instructions);
        }

        if (order == 0)
        {
            order = Candidate.Bytes.CompareTo(other.Bytes);
        }

        if (order == 0)
        {
            order = string.CompareOrdinal(Candidate.Forward, other.Forward);
        }

        if (order == 0)
        {
            order = Candidate.Offset.CompareTo(other.Offset);
        }

        return order > 0;
    }
}

// Keeps the code of 1 version with its sites and the result of each search, because a search scans the whole file.
file sealed class VersionCode
{
    private readonly CodeImage _image;
    private readonly Dictionary<RouteCandidate, uint?> _reached = [];
    private readonly ILookup<ulong, SiteWindow> _sites;

    public VersionCode(RouteVersion version, CodeImage image)
    {
        Version = version;
        _image = image;

        HashSet<ulong> addresses = [.. version.Globals.Values.SelectMany(rvas => rvas.Select(rva => image.ImageBase + rva))];
        _sites = InstructionSites.Find(image, addresses).ToLookup(static window => window.Address);
    }

    public RouteVersion Version { get; }

    public IEnumerable<SiteWindow> WindowsOf(string global)
    {
        return Version.Globals.TryGetValue(global, out var rvas) ? rvas.SelectMany(rva => _sites[_image.ImageBase + rva]) : [];
    }

    public bool TryReach(string global, RouteCandidate candidate, out uint rva)
    {
        if (!_reached.TryGetValue(candidate, out uint? reached))
        {
            reached = new SignatureScan(candidate).Search(_image);
            _reached[candidate] = reached;
        }

        rva = reached.GetValueOrDefault();

        return reached.HasValue && Version.Globals[global].Contains(rva);
    }
}
