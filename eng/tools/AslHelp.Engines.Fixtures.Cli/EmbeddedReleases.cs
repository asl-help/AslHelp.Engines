using AslHelp.Engines.Fixtures.Cli.Transfer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Reads the pinned releases of a toolchain from its embedded <c>releases.json</c>, 1 row per pin.
/// </summary>
/// <typeparam name="TRow">The type of the row, which brings the path of the file and its own checks.</typeparam>
internal static class EmbeddedReleases<TRow>
    where TRow : class, IReleaseRow<TRow>
{
    private static readonly Lazy<IReadOnlyList<TRow>> _releases = new(ReadEmbedded);

    /// <summary>
    ///     Gets the rows of the file in file order, read once on 1st use.
    /// </summary>
    /// <remarks>
    ///     A damaged or missing file throws on every get, because the 1st exception is kept.
    /// </remarks>
    public static IReadOnlyList<TRow> All => _releases.Value;

    /// <summary>
    ///     Looks up the row that <paramref name="spelling"/> picks in <see cref="All"/>.
    /// </summary>
    /// <param name="spelling">The version as <see cref="IReleaseRow{TRow}.Matches"/> takes it.</param>
    /// <returns>
    ///     The row of the version.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="spelling"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="All"/> doesn't have a row of <paramref name="spelling"/>.</exception>
    public static TRow Find(string spelling)
    {
        ArgumentNullException.ThrowIfNull(spelling);

        foreach (var release in All)
        {
            if (release.Matches(spelling))
            {
                return release;
            }
        }

        string[] pins = new string[All.Count];

        for (int i = 0; i < pins.Length; i++)
        {
            pins[i] = All[i].Pin;
        }

        // Throw InvalidOperationException, because the CLI prints the message as is and ArgumentException adds a parameter suffix.
        throw new InvalidOperationException(
            SR.FormatReleaseNotPinned(TRow.DisplayName, spelling, string.Join(", ", pins), TRow.ReleasesPath));
    }

    /// <summary>
    ///     Reads the rows of a release file shaped like the embedded one.
    /// </summary>
    /// <param name="json">The UTF-8 JSON array of rows.</param>
    /// <returns>
    ///     The rows in file order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">
    ///     The file is not an array of rows or is the JSON literal <c>null</c>. A row is <c>null</c>, lacks a key, has an unknown
    ///     one, fails the checks of <typeparamref name="TRow"/>, has a sha1 that is not 40 lowercase hex chars or has a key of an
    ///     earlier row. The message has the path of the file.
    /// </exception>
    public static IReadOnlyList<TRow> Read(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        TRow[]? releases;

        try
        {
            releases = JsonSerializer.Deserialize(json, TRow.ArrayTypeInfo);
        }
        catch (JsonException exception)
        {
            // Wrap the JSON error, so the CLI message points at the file to fix and not only a JSON path.
            throw new InvalidDataException(SR.FormatReleasesInvalid(TRow.ReleasesPath, exception.Message), exception);
        }

        if (releases is null)
        {
            throw new InvalidDataException(SR.FormatReleasesNull(TRow.ReleasesPath));
        }

        // Compare keys like Matches does, so 2 rows never answer to 1 spelling.
        HashSet<string> keys = [with(StringComparer.OrdinalIgnoreCase)];

        // Check every row at load, so a bad pin fails every build and not only one that downloads.
        foreach (var release in releases)
        {
            // Check for a null row here, because the serializer checks nulls only for the keys of a row and not for the array items.
            if (release is null)
            {
                throw new InvalidDataException(SR.FormatReleasesNullRow(TRow.ReleasesPath));
            }

            // Run the checks of the row first, because the message of a bad sha1 names the version and the row checks it.
            release.ThrowIfInvalid();

            try
            {
                _ = PinnedHash.Sha1(release.Sha1);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(SR.FormatReleasesBadSha1(TRow.ReleasesPath, release.Sha1, release.Version), exception);
            }

            ThrowIfTaken(keys, release);
        }

        return releases;
    }

    /// <summary>
    ///     Adds the keys of <paramref name="release"/> to <paramref name="keys"/> and throws if an earlier row has 1 of them.
    /// </summary>
    /// <param name="keys">The keys of the earlier rows.</param>
    /// <param name="release">The row.</param>
    /// <exception cref="InvalidDataException">An earlier row has a key of <paramref name="release"/>.</exception>
    private static void ThrowIfTaken(HashSet<string> keys, TRow release)
    {
        // Take the keys of 1 row as a set first, because a row may spell 2 of its keys the same.
        HashSet<string> own = [with(StringComparer.OrdinalIgnoreCase), .. release.Keys];

        foreach (string key in own)
        {
            if (!keys.Add(key))
            {
                throw new InvalidDataException(SR.FormatReleasesKeyTwice(TRow.ReleasesPath, key));
            }
        }
    }

    /// <summary>
    ///     Reads the embedded release file of <typeparamref name="TRow"/> from the assembly of <typeparamref name="TRow"/>.
    /// </summary>
    /// <returns>
    ///     The rows in file order.
    /// </returns>
    /// <exception cref="FileNotFoundException">The assembly doesn't have the resource.</exception>
    private static IReadOnlyList<TRow> ReadEmbedded()
    {
        using var json = typeof(TRow).Assembly.GetManifestResourceStream(TRow.ReleasesResource)
            ?? throw new FileNotFoundException(SR.FormatResourceNotEmbedded(TRow.ReleasesResource), TRow.ReleasesResource);

        return Read(json);
    }
}
