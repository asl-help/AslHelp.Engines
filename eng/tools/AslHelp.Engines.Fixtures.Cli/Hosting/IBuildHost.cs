using System;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Hosting;

/// <summary>
///     Represents the place that the build steps of an engine run in, each in a container of a build image.
/// </summary>
internal interface IBuildHost
{
    /// <summary>
    ///     Checks whether the host has <paramref name="image"/>.
    /// </summary>
    /// <param name="image">The image with its tag, such as <c>build:0123456789abcdef</c>.</param>
    /// <param name="cancellationToken">The token that stops the check.</param>
    /// <returns>
    ///     <see langword="true"/> if the host has the image.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The host is not ready to run anything.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> HasImageAsync(string image, CancellationToken cancellationToken);

    /// <summary>
    ///     Starts <paramref name="step"/>.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="cancellationToken">The token that stops the start.</param>
    /// <returns>
    ///     The running step. Disposing it removes its container, also a running one.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="step"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The host is not ready to run anything or refuses the step.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IHostedRun> StartAsync(BuildStep step, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the containers of the steps of <paramref name="owner"/> that are still on the host, also running ones.
    /// </summary>
    /// <remarks>
    ///     A killed run leaves its step container, which keeps the folders of the build open.
    /// </remarks>
    /// <param name="owner">The folder of the build, as in <see cref="BuildStep.Owner"/>.</param>
    /// <param name="cancellationToken">The token that stops the removal.</param>
    /// <returns>
    ///     A task that completes once every container of <paramref name="owner"/> is gone.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The host is not ready to run anything.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task RemoveStepsAsync(string owner, CancellationToken cancellationToken);

    /// <summary>
    ///     Lets this user delete and move what the steps write into <paramref name="directory"/>, also once the steps are gone.
    /// </summary>
    /// <remarks>
    ///     Call it before the 1st step that mounts the folder, because the files a step writes keep the rules of the folder.
    /// </remarks>
    /// <param name="directory">The host folder that steps mount.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">This user cannot change the rules of the folder.</exception>
    void GrantCurrentUser(string directory);
}
