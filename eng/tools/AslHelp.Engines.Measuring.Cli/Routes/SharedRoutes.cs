using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Finds the signature routes that the versions of a group share.
/// </summary>
internal static class SharedRoutes
{
    /// <summary>
    ///     Maps the executable of each version once and searches the routes of each global.
    /// </summary>
    /// <param name="versions">The versions.</param>
    /// <param name="leastLiterals">The fewest unmasked bytes of a route, so a route doesn't match random data at run time.</param>
    /// <param name="maxDegreeOfParallelism">The number of executables to map, decode and search at once, the same as the players of the run.</param>
    /// <param name="logger">The logger for an executable that fails to read.</param>
    /// <param name="cancellationToken">The token that stops the search.</param>
    /// <returns>
    ///     The routes by global, in the order of the globals of the versions.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static IReadOnlyDictionary<string, GlobalRoutes> Find(
        IReadOnlyList<RouteVersion> versions,
        int leastLiterals,
        int maxDegreeOfParallelism,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var codes = new VersionCode?[versions.Count];
        var failures = new string?[versions.Count];

        try
        {
            // Cancel the reads too, because a version without a site never reaches the check of a candidate.
            // Bound the reads and the searches like the players, because the decode of 1 executable takes memory in its size
            // and each search takes 1 core.
            ParallelOptions options = new() { CancellationToken = cancellationToken, MaxDegreeOfParallelism = maxDegreeOfParallelism };

            Parallel.For(0, versions.Count, options, index =>
            {
                var version = versions[index];

                if (version.Globals.Count == 0)
                {
                    return;
                }

                if (VersionCode.Read(version).TryUnwrap(out var code, out var error))
                {
                    codes[index] = code;
                }
                else
                {
                    failures[index] = error.Message;
                }
            });

            List<VersionCode> readable = [];
            Dictionary<string, string> unreadable = [with(StringComparer.Ordinal)];

            for (int index = 0; index < versions.Count; index++)
            {
                if (codes[index] is { } code)
                {
                    readable.Add(code);
                }
                else if (failures[index] is { } failure)
                {
                    unreadable[versions[index].Version] = failure;
                    logger.ExecutableNotRead(versions[index].Version, failure);
                }
            }

            Dictionary<string, GlobalRoutes> routes = [with(StringComparer.Ordinal)];

            foreach (string global in versions.SelectMany(static version => version.Globals.Keys).Distinct(StringComparer.Ordinal))
            {
                routes[global] = new GlobalSearch(global, readable, leastLiterals, options).Run(versions, unreadable);
            }

            return routes;
        }
        finally
        {
            foreach (var code in codes)
            {
                code?.Dispose();
            }
        }
    }
}

// Searches the routes of 1 global, 1 route per round, until no candidate covers a version that is left.
// Refuses a candidate that reaches another address in any readable version of the group, because a reader of that version would read the wrong value.
file sealed class GlobalSearch(
    string global,
    List<VersionCode> readable,
    int leastLiterals,
    ParallelOptions options)
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
    // A looser mask adds nothing for a version with its own site, because every match of the strict form is also a match of the masked form.
    // Tries the tiers from the tightest, so a route masks only the nibbles that it needs.
    private Picked? Pick()
    {
        // Stop growing at the versions with a site, because a version without one adds a scan to every candidate and the window tiers still search it.
        int withSite = _remaining.Count(code => code.WindowsOf(global).Any());

        if (PickFrom(CandidatesOfSites, withSite) is { } picked)
        {
            return picked;
        }

        foreach (var tier in Enum.GetValues<MaskTier>())
        {
            if (PickFrom(instructions => CandidatesOfWindows(tier, instructions), _remaining.Count) is { } loose)
            {
                return loose;
            }
        }

        return null;
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

                var reached = Reach(entry.Candidate, entry.Versions);

                if (reached.Count > 0 && (best is null || best.IsWorseThan(entry.Candidate, reached.Count)) && !ConflictsInGroup(entry.Candidate))
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
            var reached = best.Reached;

            foreach ((var code, uint rva) in Reach(best.Candidate, _remaining.FindAll(other => !reached.ContainsKey(other))))
            {
                reached[code] = rva;
            }
        }

        return best;
    }

    // Searches the versions at once up to the job limit, because each search scans a whole file and a candidate can have dozens of versions.
    // Hands each version to 1 thread, because the cache of a version is not thread-safe. Adds the results in version order,
    // so the pick stays the same.
    private Dictionary<VersionCode, uint> Reach(RouteCandidate candidate, List<VersionCode> codes)
    {
        var rvas = new uint?[codes.Count];

        Parallel.For(0, codes.Count, options, index => rvas[index] = codes[index].TryReach(global, candidate, out uint rva) ? rva : null);

        Dictionary<VersionCode, uint> reached = [];

        for (int index = 0; index < codes.Count; index++)
        {
            if (rvas[index] is { } rva)
            {
                reached[codes[index]] = rva;
            }
        }

        return reached;
    }

    // Searches every readable version of the group, also the covered ones and the ones without the global, because the group shares its routes.
    // The search results stay in the cache of each version, so a later round or the next global reuses them.
    private bool ConflictsInGroup(RouteCandidate candidate)
    {
        // Stop at the 1st conflict, because 1 is enough to refuse the candidate.
        return !Parallel.For(0, readable.Count, options, (index, state) =>
        {
            if (readable[index].Conflicts(global, candidate))
            {
                state.Stop();
            }
        }).IsCompleted;
    }

    // Skip a candidate with too few unmasked bytes, because a search at run time also reads the writable memory of the module
    // and a short pattern can match the data there.
    private IEnumerable<RouteCandidate> Enumerate(SiteWindow window, MaskTier tier, int instructions)
    {
        return RouteCandidates.Enumerate(window, tier, instructions).Where(candidate => candidate.Literals >= leastLiterals);
    }

    private Dictionary<RouteCandidate, CandidateVersions>.ValueCollection CandidatesOfSites(int instructions)
    {
        Dictionary<RouteCandidate, CandidateVersions> seen = [];

        foreach (var code in _remaining)
        {
            foreach (var window in code.WindowsOf(global))
            {
                foreach (var candidate in Enumerate(window, MaskTier.Strict, instructions))
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
            foreach (var candidate in Enumerate(window, tier, instructions))
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
// Owns the mapped image, so the search disposes it after the last global.
file sealed class VersionCode : IDisposable
{
    private readonly CodeImage _image;
    private readonly Dictionary<RouteCandidate, ScanResult> _results = [];
    private readonly ILookup<ulong, SiteWindow> _sites;

    private VersionCode(RouteVersion version, CodeImage image)
    {
        Version = version;
        _image = image;

        HashSet<ulong> addresses = [.. version.Globals.Values.SelectMany(rvas => rvas.Select(rva => image.ImageBase + rva))];
        _sites = InstructionSites.Find(image, addresses).ToLookup(static window => window.Address);
    }

    public RouteVersion Version { get; }

    public static Result<VersionCode> Read(RouteVersion version)
    {
        if (!CodeImage.Read(version.ExecutablePath).TryUnwrap(out var image, out var error))
        {
            return error;
        }

        try
        {
            return new VersionCode(version, image);
        }
        catch
        {
            image.Dispose();

            throw;
        }
    }

    public void Dispose()
    {
        _image.Dispose();
    }

    public IEnumerable<SiteWindow> WindowsOf(string global)
    {
        return Version.Globals.TryGetValue(global, out var rvas) ? rvas.SelectMany(rva => _sites[_image.ImageBase + rva]) : [];
    }

    public bool TryReach(string global, RouteCandidate candidate, out uint rva)
    {
        var result = Scan(candidate);
        rva = result.Rva.GetValueOrDefault();

        return result.Rva.HasValue && IsOf(global, rva);
    }

    // Counts a match that reaches an RVA of the global as no conflict, because the route then covers this version too.
    public bool Conflicts(string global, RouteCandidate candidate)
    {
        var result = Scan(candidate);

        return result.Matched && !(result.Rva is { } rva && IsOf(global, rva));
    }

    private bool IsOf(string global, uint rva)
    {
        return Version.Globals.TryGetValue(global, out var rvas) && rvas.Contains(rva);
    }

    private ScanResult Scan(RouteCandidate candidate)
    {
        if (!_results.TryGetValue(candidate, out var result))
        {
            result = new SignatureScan(candidate).Search(_image);
            _results[candidate] = result;
        }

        return result;
    }
}
