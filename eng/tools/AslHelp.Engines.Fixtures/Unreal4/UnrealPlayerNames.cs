using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace AslHelp.Engines.Fixtures.Unreal4;

/// <summary>
///     Provides the rules for the ids and the executable paths of Unreal Engine 4 and 5 players.
/// </summary>
/// <remarks>
///     The live tests of AslHelp.Engines.Unreal link this file, so it builds on .NET Standard 2.0 and .NET Framework 4.8.1 too.
/// </remarks>
internal static class UnrealPlayerNames
{
    private const int FirstMajor = 4;

    private static readonly Version _firstShippingName = new(4, 3);

    /// <summary>
    ///     Tries to split a player id into its version, its architecture and its configuration.
    /// </summary>
    /// <param name="id">The id, such as <c>4.19.2-win-x64-shipping</c>.</param>
    /// <param name="version">The engine version or <see langword="null"/> if <paramref name="id"/> isn't an id.</param>
    /// <param name="arch">The architecture, <c>x64</c> or <c>x86</c>, or <see langword="null"/> if <paramref name="id"/> isn't an id.</param>
    /// <param name="config">
    ///     The configuration, <c>development</c> or <c>shipping</c>, or <see langword="null"/> if <paramref name="id"/> isn't an id.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if <paramref name="id"/> is a version from 4.0 on, <c>win</c>, an architecture and a configuration.
    /// </returns>
    public static bool TryParseId(
        string? id,
        [NotNullWhen(true)] out Version? version,
        [NotNullWhen(true)] out string? arch,
        [NotNullWhen(true)] out string? config)
    {
        version = null;
        arch = null;
        config = null;

        // Split at every dash, because a version doesn't have one and a tag has 2.
        if (id?.Split('-') is not [var versionText, "win", var archName and ("x64" or "x86"), var configName and ("development" or "shipping")]
            || !Version.TryParse(versionText, out var parsed)
            || parsed.Major < FirstMajor)
        {
            return false;
        }

        version = parsed;
        arch = archName;
        config = configName;

        return true;
    }

    /// <summary>
    ///     Gets the path of the executable of a player.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="version">The engine version, such as 4.19.2.</param>
    /// <param name="is64Bit">Whether the player is an x64 build.</param>
    /// <param name="isShipping">Whether the player is a shipping build.</param>
    /// <returns>
    ///     The path of the executable.
    /// </returns>
    public static string ExecutablePath(string playerDirectory, Version version, bool is64Bit, bool isShipping)
    {
        string platform = Platform(is64Bit);

        // Pick the plain name before 4.3 too, because those shipping builds stage the executable and the PDB under it.
        string name = isShipping && new Version(version.Major, version.Minor) >= _firstShippingName
            ? $"Fixture-{platform}-Shipping.exe"
            : "Fixture.exe";

        return Path.Combine(playerDirectory, "Fixture", "Binaries", platform, name);
    }

    /// <summary>
    ///     Gets the platform of the build tools.
    /// </summary>
    /// <param name="is64Bit">Whether the player is an x64 build.</param>
    /// <returns>
    ///     <c>Win64</c> or <c>Win32</c>.
    /// </returns>
    public static string Platform(bool is64Bit)
    {
        return is64Bit ? "Win64" : "Win32";
    }
}
