using System;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Hosting;

/// <summary>
///     Represents an executable that an <see cref="IPlayerHost"/> or an <see cref="IBuildHost"/> started.
/// </summary>
internal interface IHostedRun : IAsyncDisposable
{
    /// <summary>
    ///     Gets a value indicating whether the executable has exited or the host has lost it.
    /// </summary>
    bool HasExited { get; }

    /// <summary>
    ///     Gets the exit code once <see cref="HasExited"/> is <see langword="true"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host lost the executable before it exited.</exception>
    int ExitCode { get; }

    /// <summary>
    ///     Waits for the executable to exit.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the wait. The executable keeps running.</param>
    /// <returns>
    ///     A task that completes once the executable has exited.
    /// </returns>
    /// <exception cref="InvalidOperationException">The host lost the executable before it exited.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task WaitForExitAsync(CancellationToken cancellationToken);
}
