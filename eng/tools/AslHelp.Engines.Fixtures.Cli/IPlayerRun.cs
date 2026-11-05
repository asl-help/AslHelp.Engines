using System;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Represents an executable that an <see cref="IPlayerHost"/> started.
/// </summary>
internal interface IPlayerRun : IAsyncDisposable
{
    /// <summary>
    ///     Gets a value indicating whether the executable has exited.
    /// </summary>
    bool HasExited { get; }

    /// <summary>
    ///     Gets the exit code once <see cref="HasExited"/> is <see langword="true"/>.
    /// </summary>
    int ExitCode { get; }

    /// <summary>
    ///     Waits for the executable to exit.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the wait. The executable keeps running.</param>
    /// <returns>
    ///     A task that completes once the executable has exited.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task WaitForExitAsync(CancellationToken cancellationToken);
}
