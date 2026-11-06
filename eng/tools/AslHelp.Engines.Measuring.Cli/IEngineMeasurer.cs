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
    ///     Measures the player in <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="cancellationToken">The token that stops the measurement.</param>
    /// <returns>
    ///     A task that completes once the player has ended.
    /// </returns>
    Task MeasureAsync(string playerDirectory, CancellationToken cancellationToken);
}
