using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Represents the part that measures the players of 1 engine.
/// </summary>
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
    /// <exception cref="InvalidDataException">The list is not valid.</exception>
    void LoadTargets(string? path);

    /// <summary>
    ///     Measures the player in <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="logger">The logger for the steps of the measurement.</param>
    /// <param name="cancellationToken">The token that stops the measurement.</param>
    /// <returns>
    ///     The values found, once the player has ended.
    /// </returns>
    Task<PlayerMeasurement> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken);
}
