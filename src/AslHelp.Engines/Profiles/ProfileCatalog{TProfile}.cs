using AslHelp.Engines.Profiles.Errors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AslHelp.Engines.Profiles;

/// <summary>
///     Represents a set of profile documents of 1 or more engines and picks the document that fits a game.
/// </summary>
/// <remarks>
///     <para>
///         An engine library adds the documents it ships and a consumer adds its own through the same methods.
///         Every document goes through the mapper when it's added, so a bad document fails right away.
///         A document fits a target with the same engine and the same key. A document without a key fits every key of its engine.
///     </para>
///     <para>
///         The engine passes its version parser, so it decides how its version text reads and sorts.
///         <see cref="Select"/> compares the parts of <see cref="ProfileVersion"/> and treats the 1st part as the major and the 2nd as the minor.
///     </para>
///     <para>
///         <see cref="Select"/> takes the document that lists the version of the target.
///         A version with fewer parts is the line of every version that starts with it.
///         A listed 1.2 fits 1.2.3 and a target 1.2 takes the highest listed 1.2.x, both without a warning.
///         When no document lists the version or its line, a caller that allows a guess gets the nearest listed version of the same major and minor
///         or else the highest listed version below the target in the same major, like 1.8.4 for 1.9.0.
///         When the listed versions that start with the target sit in more than 1 document, the highest of them is a guess too.
///         Each guess comes with a warning.
///         It never takes a version of another major or a version above the target outside its minor.
///         A document added later wins over an earlier document for the same version, so a consumer document replaces a shipped document.
///     </para>
///     <para>
///         Isn't thread-safe while documents get added.
///     </para>
/// </remarks>
/// <typeparam name="TProfile">The profile type of the engine.</typeparam>
public sealed class ProfileCatalog<TProfile>
{
    private readonly List<ProfileEntry<TProfile>> _entries = [];
    private readonly Func<ProfileReader, Result<TProfile>> _mapper;
    private readonly Func<string, ProfileVersion?> _versionParser;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProfileCatalog{TProfile}"/> class.
    /// </summary>
    /// <param name="mapper">
    ///     The mapper that turns a document into a profile, like the one an engine library passes to <see cref="ProfileReader.Read{TProfile}"/>.
    /// </param>
    /// <param name="versionParser">
    ///     The version parser of the engine, which returns <see langword="null"/> for text that isn't a version.
    ///     The catalog reads the versions of every document with it, so create the version of a <see cref="ProfileTarget"/> with it too.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="mapper"/> or <paramref name="versionParser"/> is <see langword="null"/>.
    /// </exception>
    public ProfileCatalog(Func<ProfileReader, Result<TProfile>> mapper, Func<string, ProfileVersion?> versionParser)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(versionParser);

        _mapper = mapper;
        _versionParser = versionParser;
    }

    /// <summary>
    ///     Gets the documents in the order they were added.
    /// </summary>
    public IReadOnlyList<ProfileEntry<TProfile>> Entries => _entries;

    /// <summary>
    ///     Adds the document <paramref name="json"/>.
    /// </summary>
    /// <param name="source">The name of the document for messages, like a file name. It becomes <see cref="ProfileEntry{TProfile}.Source"/>.</param>
    /// <param name="json">The text of the document.</param>
    /// <returns>
    ///     The new entry or a failure with <paramref name="source"/> when the document doesn't read. A failed document isn't added.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source"/> or <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    public Result<ProfileEntry<TProfile>> Add(string source, string json)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(json);

        return AddEntry(source, ProfileReader.ReadEntry(source, () => JsonDocument.Parse(json), _mapper, _versionParser));
    }

    /// <summary>
    ///     Adds the document read from <paramref name="utf8Json"/>.
    /// </summary>
    /// <param name="source">The name of the document for messages, like a file name. It becomes <see cref="ProfileEntry{TProfile}.Source"/>.</param>
    /// <param name="utf8Json">The stream with the UTF-8 text of the document. The caller disposes it.</param>
    /// <returns>
    ///     The new entry or a failure with <paramref name="source"/> when the document doesn't read. A failed document isn't added.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="source"/> or <paramref name="utf8Json"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="IOException">The stream fails.</exception>
    public Result<ProfileEntry<TProfile>> Add(string source, Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(utf8Json);

        return AddEntry(source, ProfileReader.ReadEntry(source, () => JsonDocument.Parse(utf8Json), _mapper, _versionParser));
    }

    /// <summary>
    ///     Adds every embedded resource of <paramref name="assembly"/> whose name starts with <paramref name="prefix"/>, like <c>Profiles/</c>.
    /// </summary>
    /// <remarks>
    ///     Adds the documents in ordinal order of their names. <see cref="ProfileEntry{TProfile}.Source"/> is the resource name.
    /// </remarks>
    /// <param name="assembly">The assembly.</param>
    /// <param name="prefix">The start of the resource names.</param>
    /// <returns>
    ///     A success, also when no name starts with <paramref name="prefix"/>,
    ///     or a failure with the name of the 1st document that doesn't read. Then none of the documents is added.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="assembly"/> or <paramref name="prefix"/> is <see langword="null"/>.
    /// </exception>
    public Result AddEmbedded(Assembly assembly, string prefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(prefix);

        List<string> names = [.. Array.FindAll(assembly.GetManifestResourceNames(), name => name.StartsWith(prefix, StringComparison.Ordinal))];
        names.Sort(StringComparer.Ordinal);

        List<ProfileEntry<TProfile>> entries = [];

        foreach (string name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name)!;

            if (!ProfileReader.ReadEntry(name, () => JsonDocument.Parse(stream), _mapper, _versionParser).TryUnwrap(out var entry, out var error))
            {
                return ProfileDocumentError.SourceUnreadable(name, error.Message);
            }

            entries.Add(entry);
        }

        _entries.AddRange(entries);
        return Result.Ok();
    }

    /// <summary>
    ///     Adds the document in the file at <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    ///     <see cref="ProfileEntry{TProfile}.Source"/> is <paramref name="path"/>.
    /// </remarks>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The new entry or a failure with the path when the file doesn't open or its document doesn't read. A failed document isn't added.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    public Result<ProfileEntry<TProfile>> AddFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        Result<ProfileEntry<TProfile>> result;

        try
        {
            using var stream = File.OpenRead(path);
            result = ProfileReader.ReadEntry(path, () => JsonDocument.Parse(stream), _mapper, _versionParser);
        }
        // Catch ArgumentException and NotSupportedException too, because an empty or malformed path throws them.
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return ProfileDocumentError.SourceUnreadable(path, ex.Message);
        }

        return AddEntry(path, result);
    }

    /// <summary>
    ///     Picks the document that fits <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    ///     Doesn't pick a document of another engine or key, even when it lists the version.
    ///     Takes a listed version with the same parts first. After that it takes the highest listed version that starts with the target,
    ///     and only then the longest listed line of the target.
    ///     A guess within 1 minor takes the nearest 3rd part, then a version below the target over a version above it.
    /// </remarks>
    /// <param name="target">The engine, the version and the key of the game.</param>
    /// <param name="guessHandling">What to do when no document lists the version or its line.</param>
    /// <returns>
    ///     The selection, with <see cref="ProfileSelection{TProfile}.Warning"/> set for a guess,
    ///     a failure with the target and the nearest version when <paramref name="guessHandling"/> doesn't allow the guess,
    ///     or a failure with the target when no document of its engine and key lists the version, its minor or a lower version of its major.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    public Result<ProfileSelection<TProfile>> Select(ProfileTarget target, ProfileGuessHandling guessHandling)
    {
        ArgumentNullException.ThrowIfNull(target);

        if ((FindListed(target) ?? FindGuess(target)) is not { } selection)
        {
            return CatalogError.NoneFits(target);
        }

        return selection.Match == ProfileMatch.Listed || guessHandling == ProfileGuessHandling.Allow
            ? selection
            : CatalogError.NotListed(target, selection.Version);
    }

    private static bool Fits(ProfileEntry<TProfile> entry, ProfileTarget target)
    {
        return string.Equals(entry.Engine, target.Engine, StringComparison.Ordinal)
            && (entry.Key is null || string.Equals(entry.Key, target.Key, StringComparison.Ordinal));
    }

    // Take the nearest 3rd part, then a version below the target over a version above it, then the version nearer the target on that side.
    // The versions and the target all have a 3rd part here, because FindListed already took any version that is a line of the target
    // or starts with it.
    private static bool IsNearer(ProfileVersion candidate, ProfileVersion best, ProfileVersion target)
    {
        long candidateDistance = Math.Abs((long)candidate.Parts[2] - target.Parts[2]);
        long bestDistance = Math.Abs((long)best.Parts[2] - target.Parts[2]);

        if (candidateDistance != bestDistance)
        {
            return candidateDistance < bestDistance;
        }

        bool candidateBelow = candidate < target;

        if (candidateBelow != (best < target))
        {
            return candidateBelow;
        }

        return candidateBelow ? candidate > best : candidate < best;
    }

    // Take the same version, else the highest listed version of the line of the target, else the longest listed line of the target.
    // A version of the line of the target starts with every part of the target, so it's nearer than a line, which leaves parts off.
    private ProfileSelection<TProfile>? FindListed(ProfileTarget target)
    {
        (ProfileEntry<TProfile> Entry, ProfileVersion Version)? line = null;
        (ProfileEntry<TProfile> Entry, ProfileVersion Version)? inLine = null;

        // Go through the documents from the last added, so a later document wins a tie.
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];

            if (!Fits(entry, target))
            {
                continue;
            }

            foreach (var version in entry.Versions)
            {
                if (version == target.Version)
                {
                    return new ProfileSelection<TProfile>(target, entry, version, ProfileMatch.Listed);
                }

                if (version.IsLineOf(target.Version))
                {
                    if (line is not { } longest || version.Parts.Count > longest.Version.Parts.Count)
                    {
                        line = (entry, version);
                    }
                }
                else if (target.Version.IsLineOf(version) && (inLine is not { } highest || version > highest.Version))
                {
                    inLine = (entry, version);
                }
            }
        }

        if (inLine is { } highestInLine)
        {
            var match = IsOnlyEntryOfLine(target, highestInLine.Entry, highestInLine.Version) ? ProfileMatch.Listed : ProfileMatch.SameMinor;
            return new ProfileSelection<TProfile>(target, highestInLine.Entry, highestInLine.Version, match);
        }

        return line is { } longestLine ? new ProfileSelection<TProfile>(target, longestLine.Entry, longestLine.Version, ProfileMatch.Listed) : null;
    }

    // Check the other documents, because their versions of the line of the target can have other layouts than the picked version.
    // Skip a document that lists the picked version itself, because the later document replaces it for that version.
    private bool IsOnlyEntryOfLine(ProfileTarget target, ProfileEntry<TProfile> entry, ProfileVersion version)
    {
        foreach (var other in _entries)
        {
            if (ReferenceEquals(other, entry) || !Fits(other, target))
            {
                continue;
            }

            foreach (var otherVersion in other.Versions)
            {
                if (otherVersion != version && target.Version.IsLineOf(otherVersion))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private ProfileSelection<TProfile>? FindGuess(ProfileTarget target)
    {
        (ProfileEntry<TProfile> Entry, ProfileVersion Version)? sameMinor = null;
        (ProfileEntry<TProfile> Entry, ProfileVersion Version)? lower = null;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];

            if (!Fits(entry, target))
            {
                continue;
            }

            foreach (var version in entry.Versions)
            {
                // Skip any other major, because its layouts are too far from the target to guess with.
                if (version.Parts[0] != target.Version.Parts[0])
                {
                    continue;
                }

                if (version.Parts[1] == target.Version.Parts[1])
                {
                    if (sameMinor is not { } nearest || IsNearer(version, nearest.Version, target.Version))
                    {
                        sameMinor = (entry, version);
                    }
                }
                else if (version < target.Version && (lower is not { } highest || version > highest.Version))
                {
                    lower = (entry, version);
                }
            }
        }

        if (sameMinor is { } nearestSameMinor)
        {
            return new ProfileSelection<TProfile>(target, nearestSameMinor.Entry, nearestSameMinor.Version, ProfileMatch.SameMinor);
        }

        if (lower is { } highestLower)
        {
            return new ProfileSelection<TProfile>(target, highestLower.Entry, highestLower.Version, ProfileMatch.LowerVersion);
        }

        return null;
    }

    private Result<ProfileEntry<TProfile>> AddEntry(string source, Result<ProfileEntry<TProfile>> result)
    {
        if (!result.TryUnwrap(out var entry, out var error))
        {
            return ProfileDocumentError.SourceUnreadable(source, error.Message);
        }

        _entries.Add(entry);
        return entry;
    }
}
