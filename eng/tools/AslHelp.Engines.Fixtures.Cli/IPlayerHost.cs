using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Represents the place that the executables of an install run in during a build.
/// </summary>
internal interface IPlayerHost
{
    /// <summary>
    ///     Starts <paramref name="executablePath"/> of the install <paramref name="installDirectory"/> in the folder of the executable.
    /// </summary>
    /// <param name="installDirectory">The full path of the install. The executable writes its files into this folder.</param>
    /// <param name="executablePath">The path of the executable, relative to the install.</param>
    /// <param name="arguments">The arguments, separated by spaces. Pass an empty string for none.</param>
    /// <param name="cancellationToken">The token that stops the start.</param>
    /// <returns>
    ///     The running executable. Disposing it ends the executable and frees the install.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException">The host is not ready to run anything.</exception>
    /// <exception cref="System.OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IPlayerRun> StartAsync(string installDirectory, string executablePath, string arguments, CancellationToken cancellationToken);
}
