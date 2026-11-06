using Microsoft.Extensions.Logging;
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
    ///     Gets the name of the engine, which is also the start of its player folders.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Loads the list of values to find, so a bad list fails before the 1st player starts.
    /// </summary>
    /// <param name="path">The file with the list or <see langword="null"/> for the list built into the measurer.</param>
    /// <returns>
    ///     A failure when the list is not valid.
    /// </returns>
    Result LoadTargets(string? path);

    /// <summary>
    ///     Measures the player in <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="logger">The logger for the steps of the measurement.</param>
    /// <param name="cancellationToken">The token that stops the measurement.</param>
    /// <returns>
    ///     The values found once the player has ended or a failure with what failed.
    /// </returns>
    Task<Result<PlayerMeasurement>> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken);
}
