using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Reads the pinned UDK releases from <c>releases.json</c>, 1 row per version.
/// </summary>
internal static class UdkReleases
{
    private const string ReleasesPath = "eng/fixtures/unreal3/releases.json";
    private const string ReleasesResource = "Unreal3/releases.json";

    private static readonly Lazy<IReadOnlyList<UdkRelease>> _releases = new(ReadEmbedded);

    /// <summary>
    ///     Gets the rows of <c>releases.json</c> in version order, read once on 1st use.
    /// </summary>
    /// <remarks>
    ///     A damaged file throws on every get, because the 1st exception is kept.
    /// </remarks>
    public static IReadOnlyList<UdkRelease> All => _releases.Value;

    /// <summary>
    ///     Looks up the row of <paramref name="version"/> in <see cref="All"/>.
    /// </summary>
    /// <param name="version">
    ///     The version as in the file name, such as <c>2015-02</c> or <c>2011-03-BETA2</c>. Upper or lower case both work.
    /// </param>
    /// <returns>
    ///     The row of the version, with the pinned spelling and sha1.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="All"/> doesn't have a row of <paramref name="version"/>.</exception>
    public static UdkRelease Find(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        foreach (var release in All)
        {
            if (release.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
            {
                return release;
            }
        }

        string[] versions = new string[All.Count];

        for (int i = 0; i < versions.Length; i++)
        {
            versions[i] = All[i].Version;
        }

        // Throw InvalidOperationException, because the CLI prints the message as is and ArgumentException adds a parameter suffix.
        throw new InvalidOperationException(
            UdkSR.FormatUdkVersionNotPinned(version, string.Join(", ", versions), UdkRelease.Item, ReleasesPath));
    }

    /// <summary>
    ///     Reads the rows of a release file shaped like <c>releases.json</c>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON array of rows, each with <c>version</c> and <c>sha1</c>.</param>
    /// <returns>
    ///     The rows in file order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">
    ///     The file is not an array of rows or is the JSON literal <c>null</c>. A row lacks a key, has an unknown one or
    ///     has a sha1 that is not 40 lowercase hex chars. The message has the path of <c>releases.json</c>.
    /// </exception>
    public static IReadOnlyList<UdkRelease> Read(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        UdkRelease[]? releases;

        try
        {
            releases = JsonSerializer.Deserialize(json, UdkReleaseContext.Default.UdkReleaseArray);
        }
        catch (JsonException exception)
        {
            // Wrap the JSON error, so the CLI message points at the file to fix and not only a JSON path.
            throw new InvalidDataException(UdkSR.FormatReleasesInvalid(ReleasesPath, exception.Message), exception);
        }

        if (releases is null)
        {
            throw new InvalidDataException(UdkSR.FormatReleasesNull(ReleasesPath));
        }

        // Parse every sha1 at load, so a bad pin fails every build and not only one that downloads.
        foreach (var release in releases)
        {
            try
            {
                _ = release.File;
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(UdkSR.FormatReleasesBadSha1(ReleasesPath, release.Sha1, release.Version), exception);
            }
        }

        return releases;
    }

    /// <summary>
    ///     Reads <c>releases.json</c> from the resources of this assembly.
    /// </summary>
    /// <returns>
    ///     The rows in file order.
    /// </returns>
    /// <exception cref="FileNotFoundException">The assembly doesn't have a <c>releases.json</c> resource.</exception>
    private static IReadOnlyList<UdkRelease> ReadEmbedded()
    {
        using var json = typeof(UdkReleases).Assembly.GetManifestResourceStream(ReleasesResource)
            ?? throw new FileNotFoundException(UdkSR.FormatReleasesNotEmbedded(ReleasesResource), ReleasesResource);

        return Read(json);
    }
}
