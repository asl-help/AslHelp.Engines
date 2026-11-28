using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Fixtures.Unreal4;

/// <summary>
///     Describes 1 player of an Unreal Engine 4 or 5 version by its tag, such as <c>win-x64-shipping</c>, which the command lines
///     that build and measure the players share.
/// </summary>
/// <param name="Arch">The architecture, <see cref="Architecture.X64"/> or <see cref="Architecture.X86"/>.</param>
/// <param name="Config">The build configuration.</param>
/// <exception cref="ArgumentOutOfRangeException">
///     <paramref name="Arch"/> is not <see cref="Architecture.X64"/> or <see cref="Architecture.X86"/> or <paramref name="Config"/>
///     is not an <see cref="UnrealConfig"/> member.
/// </exception>
public sealed record UnrealPlayer(
    Architecture Arch,
    UnrealConfig Config)
{
    /// <summary>
    ///     Gets the architecture, <see cref="Architecture.X64"/> or <see cref="Architecture.X86"/>.
    /// </summary>
    public Architecture Arch { get; } = Arch is Architecture.X64 or Architecture.X86
        ? Arch
        : throw new ArgumentOutOfRangeException(nameof(Arch), Arch, SR.FormatPlayerArchInvalid(Arch));

    /// <summary>
    ///     Gets the build configuration.
    /// </summary>
    public UnrealConfig Config { get; } = Enum.IsDefined(Config)
        ? Config
        : throw new ArgumentOutOfRangeException(nameof(Config), Config, SR.FormatPlayerConfigInvalid(Config));

    /// <summary>
    ///     Gets the configuration of the build tools, <c>Shipping</c> or <c>Development</c>.
    /// </summary>
    public string Configuration => Config switch
    {
        UnrealConfig.Shipping => "Shipping",
        _ => "Development",
    };

    /// <summary>
    ///     Gets the platform of the build tools, <c>Win64</c> or <c>Win32</c>.
    /// </summary>
    public string Platform => UnrealPlayerNames.Platform(Arch == Architecture.X64);

    /// <summary>
    ///     Gets the tag, such as <c>win-x64-shipping</c>.
    /// </summary>
    public string Tag => $"win-{ArchName(Arch)}-{ConfigName(Config)}";

    /// <summary>
    ///     Gets the player id of this player in <paramref name="version"/>, such as <c>4.19.2-win-x64-shipping</c>.
    /// </summary>
    /// <param name="version">The engine version.</param>
    /// <returns>
    ///     The player id.
    /// </returns>
    public string IdOf(string version)
    {
        return $"{version}-{Tag}";
    }

    /// <summary>
    ///     Gets the path of the executable of this player in <paramref name="version"/>.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="version">The engine version, such as 4.19.2.</param>
    /// <returns>
    ///     The path of the executable.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public string ExecutablePath(string playerDirectory, Version version)
    {
        ArgumentNullException.ThrowIfNull(playerDirectory);
        ArgumentNullException.ThrowIfNull(version);

        return UnrealPlayerNames.ExecutablePath(playerDirectory, version, Arch == Architecture.X64, Config == UnrealConfig.Shipping);
    }

    /// <summary>
    ///     Gets the path of the PDB of this player in <paramref name="version"/>, beside the executable.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="version">The engine version, such as 4.19.2.</param>
    /// <returns>
    ///     The path of the PDB.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public string PdbPath(string playerDirectory, Version version)
    {
        return Path.ChangeExtension(ExecutablePath(playerDirectory, version), ".pdb");
    }

    /// <summary>
    ///     Gets the name of <paramref name="arch"/> in a player tag.
    /// </summary>
    /// <param name="arch">The architecture.</param>
    /// <returns>
    ///     <c>x64</c> or <c>x86</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="arch"/> is not <see cref="Architecture.X64"/> or <see cref="Architecture.X86"/>.
    /// </exception>
    public static string ArchName(Architecture arch)
    {
        return arch switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => throw new ArgumentOutOfRangeException(nameof(arch), arch, SR.FormatPlayerArchInvalid(arch)),
        };
    }

    /// <summary>
    ///     Gets the name of <paramref name="config"/> in a player tag.
    /// </summary>
    /// <param name="config">The build configuration.</param>
    /// <returns>
    ///     <c>development</c> or <c>shipping</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="config"/> is not an <see cref="UnrealConfig"/> member.</exception>
    public static string ConfigName(UnrealConfig config)
    {
        return config switch
        {
            UnrealConfig.Development => "development",
            UnrealConfig.Shipping => "shipping",
            _ => throw new ArgumentOutOfRangeException(nameof(config), config, SR.FormatPlayerConfigInvalid(config)),
        };
    }

    /// <summary>
    ///     Reads a player tag.
    /// </summary>
    /// <param name="tag">The tag, such as <c>win-x64-shipping</c>.</param>
    /// <returns>
    ///     The player.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="tag"/> isn't <c>win</c>, an architecture and a configuration.</exception>
    public static UnrealPlayer Parse(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return TryParse(tag, out var player) ? player : throw new FormatException(SR.FormatPlayerTagInvalid(tag));
    }

    /// <summary>
    ///     Tries to read a player tag.
    /// </summary>
    /// <param name="tag">The tag, such as <c>win-x64-shipping</c>.</param>
    /// <param name="player">The player or <see langword="null"/> if <paramref name="tag"/> isn't a tag.</param>
    /// <returns>
    ///     <see langword="true"/> if <paramref name="tag"/> is <c>win</c>, an architecture and a configuration.
    /// </returns>
    public static bool TryParse(string? tag, [NotNullWhen(true)] out UnrealPlayer? player)
    {
        player = tag?.Split('-') is ["win", var archName, var configName]
            && TryParseArch(archName, out var arch)
            && TryParseConfig(configName, out var config)
            ? new UnrealPlayer(arch, config)
            : null;

        return player is not null;
    }

    /// <summary>
    ///     Tries to read the name of an architecture in a player tag.
    /// </summary>
    /// <param name="name">The name, <c>x64</c> or <c>x86</c>.</param>
    /// <param name="arch">The architecture or <see langword="default"/> if <paramref name="name"/> isn't 1 of them.</param>
    /// <returns>
    ///     <see langword="true"/> if <paramref name="name"/> is <c>x64</c> or <c>x86</c>.
    /// </returns>
    public static bool TryParseArch(string? name, out Architecture arch)
    {
        Architecture? parsed = name switch
        {
            "x64" => Architecture.X64,
            "x86" => Architecture.X86,
            _ => null,
        };

        arch = parsed.GetValueOrDefault();

        return parsed is not null;
    }

    /// <summary>
    ///     Tries to read the name of a build configuration in a player tag.
    /// </summary>
    /// <param name="name">The name, <c>development</c> or <c>shipping</c>.</param>
    /// <param name="config">The build configuration or <see langword="default"/> if <paramref name="name"/> isn't 1 of them.</param>
    /// <returns>
    ///     <see langword="true"/> if <paramref name="name"/> is <c>development</c> or <c>shipping</c>.
    /// </returns>
    public static bool TryParseConfig(string? name, out UnrealConfig config)
    {
        UnrealConfig? parsed = name switch
        {
            "development" => UnrealConfig.Development,
            "shipping" => UnrealConfig.Shipping,
            _ => null,
        };

        config = parsed.GetValueOrDefault();

        return parsed is not null;
    }

    /// <summary>
    ///     Tries to read a player id.
    /// </summary>
    /// <param name="id">The id, such as <c>4.19.2-win-x64-shipping</c>.</param>
    /// <param name="version">The engine version or <see langword="null"/> if <paramref name="id"/> isn't an id.</param>
    /// <param name="player">The player or <see langword="null"/> if <paramref name="id"/> isn't an id.</param>
    /// <returns>
    ///     <see langword="true"/> if <paramref name="id"/> is a version from 4.0 on and a tag.
    /// </returns>
    public static bool TryParseId(
        string? id,
        [NotNullWhen(true)] out Version? version,
        [NotNullWhen(true)] out UnrealPlayer? player)
    {
        player = UnrealPlayerNames.TryParseId(id, out version, out string? archName, out string? configName)
            && TryParseArch(archName, out var arch)
            && TryParseConfig(configName, out var config)
            ? new UnrealPlayer(arch, config)
            : null;

        return player is not null;
    }
}
