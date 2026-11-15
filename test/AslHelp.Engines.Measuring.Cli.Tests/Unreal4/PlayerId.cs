using AslHelp.Engines.Fixtures.Unreal4;
using System;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

/// <summary>
///     Describes the parts of a player id that a test writes a player folder from.
/// </summary>
/// <param name="Version">The engine version.</param>
/// <param name="Player">The player.</param>
internal sealed record PlayerId(
    Version Version,
    UnrealPlayer Player)
{
    /// <summary>
    ///     Reads a player id, such as <c>4.19.2-win-x64-shipping</c>.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>
    ///     The parts of the id.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> isn't a player id.</exception>
    public static PlayerId Parse(string id)
    {
        return UnrealPlayer.TryParseId(id, out var version, out var player)
            ? new PlayerId(version, player)
            : throw new ArgumentException(id, nameof(id));
    }

    /// <summary>
    ///     Gets the path of the executable of the player in <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <returns>
    ///     The path of the executable.
    /// </returns>
    public string ExecutablePath(string playerDirectory)
    {
        return Player.ExecutablePath(playerDirectory, Version);
    }

    /// <summary>
    ///     Gets the path of the PDB of the player in <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <returns>
    ///     The path of the PDB.
    /// </returns>
    public string PdbPath(string playerDirectory)
    {
        return Player.PdbPath(playerDirectory, Version);
    }
}
