using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Represents the part that measures the players of 1 engine.
/// </summary>
/// <remarks>
///     Returns a failed <see cref="Result"/> for an expected failure, with a message that lists what failed.
///     Throws only for cancellation, programmer errors and a player that does not start.
/// </remarks>
internal interface IEngineMeasurer
{
    /// <summary>
    ///     Gets the name of the engine, which is also its subcommand and the start of its player folders.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Gets the options that this measurer adds to its subcommand. <see cref="SelectPlayers"/> reads their values.
    /// </summary>
    IReadOnlyList<Option> Options { get; }

    /// <summary>
    ///     Gets the most players that this measurer measures at once, at least 1. The <c>--jobs</c> option can lower it.
    /// </summary>
    int MaxDegreeOfParallelism { get; }

    /// <summary>
    ///     Loads the list of values to find, so a bad list fails before the 1st player starts.
    /// </summary>
    /// <param name="path">The file with the list or <see langword="null"/> for the list built into the measurer.</param>
    /// <returns>
    ///     A failure when the list is not valid.
    /// </returns>
    Result LoadTargets(string? path);

    /// <summary>
    ///     Picks the players to measure.
    /// </summary>
    /// <param name="players">The player folders of the engine in version order.</param>
    /// <param name="parseResult">The parsed command line with the values of <see cref="Options"/>.</param>
    /// <param name="logger">The logger for a player that gets skipped.</param>
    /// <returns>
    ///     The players to measure in version order. An empty list fails the run.
    /// </returns>
    IReadOnlyList<PlayerFolder> SelectPlayers(IReadOnlyList<PlayerFolder> players, ParseResult parseResult, ILogger logger);

    /// <summary>
    ///     Gets the key of the versions that can share a group with <paramref name="version"/>.
    /// </summary>
    /// <remarks>
    ///     A keyed version ends with <c>-</c> and the key. The profile documents cut that end off.
    /// </remarks>
    /// <param name="version">The version of a player.</param>
    /// <returns>
    ///     The key or <see langword="null"/> if a group needs nothing but equal offsets.
    /// </returns>
    string? GroupOf(string version);

    /// <summary>
    ///     Checks whether a library reads an offset, so the offset decides the groups. The report keeps every other offset in the rows.
    /// </summary>
    /// <param name="type">The type of the offset.</param>
    /// <param name="member">The member of the offset under its type.</param>
    /// <returns>
    ///     <see langword="true"/> if 2 versions with other values for the offset belong in 2 groups.
    /// </returns>
    bool IsNeeded(string type, string member);

    /// <summary>
    ///     Measures <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The player folder.</param>
    /// <param name="logger">The logger for the steps of the measurement.</param>
    /// <param name="cancellationToken">The token that stops the measurement.</param>
    /// <returns>
    ///     The values found or a failure with what failed. A player that the measurer started has ended by then.
    /// </returns>
    /// <exception cref="InvalidOperationException">The player exits before it is ready.</exception>
    /// <exception cref="IOException">A file of the player is missing or does not read.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The player is not ready in time.</exception>
    /// <exception cref="UnauthorizedAccessException">A file or a lock of the player is not accessible.</exception>
    /// <exception cref="Win32Exception">The process of the player cannot be created.</exception>
    Task<Result<PlayerMeasurement>> MeasureAsync(PlayerFolder player, ILogger logger, CancellationToken cancellationToken);
}
