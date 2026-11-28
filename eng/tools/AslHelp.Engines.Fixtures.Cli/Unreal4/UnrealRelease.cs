using AslHelp.Engines.Fixtures.Unreal4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Describes 1 row of <c>releases.json</c>, an engine minor and its pinned hotfix.
/// </summary>
/// <param name="Version">The pinned version with its hotfix, such as <c>4.19.2</c>.</param>
/// <param name="App">The Epic app of the minor, such as <c>UE_4.19</c>.</param>
/// <param name="Build">The build version in the manifest, such as <c>4.19.2-4033788+++UE4+Release-4.19-2023.1-Windows</c>.</param>
/// <param name="Sha1">The sha1 of the manifest as 40 lowercase hex chars.</param>
/// <param name="InstallTags">Whether the manifest has install tags, so the download leaves the tagged files out.</param>
/// <param name="Players">The players in build order, written as tags such as <c>win-x64-shipping</c>.</param>
/// <param name="EditorFlags">The switches for the editor target, such as <c>-waitmutex</c>.</param>
/// <param name="PackageFlags">The switches for the package of a player, such as <c>-rocket</c>.</param>
/// <param name="PathPrepend">The folder that goes first on PATH of a build or <see langword="null"/> for none.</param>
/// <param name="Runtimes">Where the runtime DLLs of the players come from.</param>
internal sealed record UnrealRelease(
    string Version,
    string App,
    string Build,
    string Sha1,
    bool InstallTags,
    IReadOnlyList<UnrealPlayer> Players,
    IReadOnlyList<string> EditorFlags,
    IReadOnlyList<string> PackageFlags,
    string? PathPrepend,
    UnrealRuntimeSource Runtimes) : IReleaseRow<UnrealRelease>
{
    /// <inheritdoc/>
    public static JsonTypeInfo<UnrealRelease[]> ArrayTypeInfo => UnrealReleaseContext.Default.UnrealReleaseArray;

    /// <inheritdoc/>
    public static string DisplayName => UnrealSR.DisplayName;

    /// <inheritdoc/>
    /// <remarks>
    ///     The keys are the version, the minor and the app, so 2 rows of 1 minor fail the load.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<string> Keys => [Version, Minor, App];

    /// <summary>
    ///     Gets the version without its hotfix, such as <c>4.19</c>.
    /// </summary>
    [JsonIgnore]
    public string Minor => Version[..Version.LastIndexOf('.', StringComparison.Ordinal)];

    /// <inheritdoc/>
    /// <remarks>
    ///     The pin is the minor.
    /// </remarks>
    [JsonIgnore]
    public string Pin => Minor;

    /// <inheritdoc/>
    public static string ReleasesPath => "eng/fixtures/unreal4/releases.json";

    /// <inheritdoc/>
    public static string ReleasesResource => "Unreal4/releases.json";

    /// <inheritdoc/>
    /// <remarks>
    ///     The spelling is the version, the minor or the app, such as <c>4.19.2</c>, <c>4.19</c> or <c>UE_4.19</c>. Upper or lower case
    ///     both work. Another hotfix of the minor doesn't match.
    /// </remarks>
    public bool Matches(string spelling)
    {
        return spelling.Equals(Version, StringComparison.OrdinalIgnoreCase)
            || spelling.Equals(Minor, StringComparison.OrdinalIgnoreCase)
            || spelling.Equals(App, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A row fails when its version is not major.minor.hotfix, its build doesn't start with its version, it has an empty
    ///     switch, it doesn't list a player or it lists a player twice. A bad player tag fails the
    ///     parse before.
    /// </remarks>
    public void ThrowIfInvalid()
    {
        // Check the shape first, because Minor cuts the hotfix off the version without a check. Compare the parsed version with
        // the text, because the parse takes spaces and leading zeros that no spelling of the user matches.
        if (!System.Version.TryParse(Version, out var version) || version.Build < 0 || version.Revision >= 0 || version.ToString() != Version)
        {
            throw new InvalidDataException(UnrealSR.FormatReleasesBadVersion(ReleasesPath, Version));
        }

        if (!Build.StartsWith($"{Version}-", StringComparison.Ordinal))
        {
            throw new InvalidDataException(UnrealSR.FormatReleasesBuildMismatch(ReleasesPath, Build, Version));
        }

        // Check the switches, because the JSON options refuse a null list but not a null in a list.
        foreach (string flag in (IEnumerable<string>)[.. EditorFlags, .. PackageFlags])
        {
            if (string.IsNullOrWhiteSpace(flag))
            {
                throw new InvalidDataException(UnrealSR.FormatReleasesBadFlag(ReleasesPath, Version));
            }
        }

        ThrowIfBadPlayers();
    }

    /// <summary>
    ///     Throws if the row doesn't list a player or lists a player twice.
    /// </summary>
    /// <exception cref="InvalidDataException">The row doesn't list a player or lists a player twice.</exception>
    private void ThrowIfBadPlayers()
    {
        if (Players.Count == 0)
        {
            throw new InvalidDataException(UnrealSR.FormatReleasesNoPlayers(ReleasesPath, Version));
        }

        HashSet<UnrealPlayer> players = [];

        foreach (var player in Players)
        {
            if (!players.Add(player))
            {
                throw new InvalidDataException(UnrealSR.FormatReleasesPlayerTwice(ReleasesPath, player.Tag, Version));
            }
        }
    }
}
