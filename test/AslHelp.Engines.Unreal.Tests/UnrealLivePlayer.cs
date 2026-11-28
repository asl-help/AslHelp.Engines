using AslHelp.Engines.Fixtures.Unreal4;
using AslHelp.Engines.Profiles;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Describes 1 built Unreal Engine 4 or 5 player that the live tests start, with the version and the key of the name of its folder.
/// </summary>
/// <param name="Folder">The player folder.</param>
/// <param name="Version">The engine version.</param>
/// <param name="Key">The key, like <c>win-x64-shipping</c>.</param>
public sealed record UnrealLivePlayer(
    string Folder,
    ProfileVersion Version,
    string Key)
{
    /// <summary>
    ///     The variable with the folder that <c>just fixtures-all unreal4</c> fills, 1 player folder per version and key.
    /// </summary>
    public const string PlayersVariable = "FIXTURES_UNREAL_PLAYERS";

    /// <summary>
    ///     The variable with the folder of 1 player.
    /// </summary>
    public const string PlayerVariable = "FIXTURES_UNREAL_PLAYER";

    // Take the 4.19.2 x64 shipping player for a folder of FIXTURES_UNREAL_PLAYER whose name doesn't have a version and a key.
    private const string DefaultKey = "win-x64-shipping";

    private static readonly ProfileVersion _defaultVersion = UnrealTarget.ParseVersion("4.19.2")!;

    // The executables of Unreal Engine 4.0 to 4.7 don't have a version resource.
    private static readonly ProfileVersion _firstVersionStamp = UnrealTarget.ParseVersion("4.8")!;

    /// <summary>
    ///     Gets the build configuration.
    /// </summary>
    public UnrealConfiguration Configuration => Key.EndsWith("-development", StringComparison.Ordinal)
        ? UnrealConfiguration.Development
        : UnrealConfiguration.Shipping;

    /// <summary>
    ///     Gets the path of the executable.
    /// </summary>
    public string ExecutablePath => UnrealPlayerNames.ExecutablePath(
        Folder,
        new Version(Version.Text),
        Is64Bit,
        Configuration == UnrealConfiguration.Shipping);

    /// <summary>
    ///     Gets a value indicating whether the executable has the engine version in its version resource, which players from 4.8 on have.
    /// </summary>
    public bool HasVersionStamp => Version >= _firstVersionStamp;

    /// <summary>
    ///     Gets the id, like <c>4.19.2-win-x64-shipping</c>.
    /// </summary>
    public string Id => $"{Version}-{Key}";

    /// <summary>
    ///     Gets whether the player is an x64 build.
    /// </summary>
    public bool Is64Bit => Key.StartsWith("win-x64-", StringComparison.Ordinal);

    /// <summary>
    ///     Gets the target that detection reads from the player.
    /// </summary>
    public ProfileTarget Target => new(UnrealTarget.Unreal4, Version, Key);

    /// <summary>
    ///     Gets the version to pass to detection, which is <see langword="null"/> when the executable has the stamp.
    /// </summary>
    public string? VersionWithoutStamp => HasVersionStamp ? null : Version.Text;

    /// <summary>
    ///     Lists 1 test fixture per player of <see cref="PlayersVariable"/> in the order of the versions,
    ///     else 1 fixture for the player of <see cref="PlayerVariable"/>.
    /// </summary>
    /// <remarks>
    ///     Gives 1 fixture with a <see langword="null"/> player when the variables don't have a player, so the tests report why instead of
    ///     failing for a source without fixtures.
    /// </remarks>
    /// <returns>
    ///     The test fixtures, each with the id of its player as its name.
    /// </returns>
    public static IEnumerable<TestFixtureData> Fixtures()
    {
        UnrealLivePlayer[] players = [.. Find()];

        return players.Length == 0
            ? [new TestFixtureData([null]).SetArgDisplayNames("none")]
            : players.Select(static player => new TestFixtureData(player).SetArgDisplayNames(player.Id));
    }

    private static IEnumerable<UnrealLivePlayer> Find()
    {
        string? players = Environment.GetEnvironmentVariable(PlayersVariable);

        if (players is not null)
        {
            return Directory.Exists(players)
                ? Directory.EnumerateDirectories(players)
                    .Select(static folder => FromFolderName(folder))
                    .OfType<UnrealLivePlayer>()
                    .OrderBy(static player => player.Version)
                    .ThenBy(static player => player.Key, StringComparer.Ordinal)
                : [];
        }

        string? player = Environment.GetEnvironmentVariable(PlayerVariable);

        return player is not null && Directory.Exists(player)
            ? [FromFolderName(player) ?? new UnrealLivePlayer(player, _defaultVersion, DefaultKey)]
            : [];
    }

    // Split a name like unreal4-4.19.2-win-x64-shipping-27859bb210d428d1 into the engine, the player id and the hash of the cache key.
    private static UnrealLivePlayer? FromFolderName(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string[] parts = name.Split('-');

        return parts is [UnrealTarget.Unreal4, .., _]
            && UnrealPlayerNames.TryParseId(string.Join("-", parts, 1, parts.Length - 2), out var version, out string? arch, out string? config)
            && UnrealTarget.ParseVersion(version.ToString()) is { } parsed
            ? new UnrealLivePlayer(folder, parsed, $"win-{arch}-{config}")
            : null;
    }
}
