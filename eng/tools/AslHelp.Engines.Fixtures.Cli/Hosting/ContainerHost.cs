using Docker.DotNet;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Hosting;

/// <summary>
///     Runs the executables of an install in a Windows container with the install mounted, so a build proves that the player
///     needs nothing that only the build machine has.
/// </summary>
internal sealed class ContainerHost : IPlayerHost, IDisposable
{
    /// <summary>
    ///     Represents the image of the container.
    /// </summary>
    /// <remarks>
    ///     The Server image is the smallest one with <c>dsound.dll</c> and <c>dinput8.dll</c>, which Server Core lacks.
    /// </remarks>
    public const string Image = $"{ImageName}:{ImageTag}";

    private const string ImageName = "mcr.microsoft.com/windows/server";
    private const string ImageTag = "ltsc2025";
    private const string MountPath = @"C:\player";

    private readonly DockerClient _client = new DockerClientBuilder().Build();

    private bool _imageReady;

    /// <inheritdoc/>
    /// <remarks>
    ///     The 1st start pulls <see cref="Image"/> if this machine does not have it, which takes minutes.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The Docker engine does not run or refuses the image or the container.</exception>
    public async Task<IHostedRun> StartAsync(string installDirectory, string executablePath, string arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        GrantCurrentUser(installDirectory);

        try
        {
            await EnsureImageAsync(cancellationToken).ConfigureAwait(false);

            return await RunAsync(NewContainerParameters(installDirectory, executablePath, arguments), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            throw new InvalidOperationException(SR.FormatDockerFailed(Image, exception.Message), exception);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    ///     Gives this user full control of <paramref name="directory"/> and of everything created in it later.
    /// </summary>
    /// <remarks>
    ///     A file that a container writes belongs to the administrator of the container, so without the inherited rule this
    ///     user can neither delete nor move it once the container is gone.
    /// </remarks>
    /// <param name="directory">The folder.</param>
    /// <exception cref="UnauthorizedAccessException">This user cannot change the rules of the folder.</exception>
    internal static void GrantCurrentUser(string directory)
    {
        DirectoryInfo info = new(directory);
        var security = info.GetAccessControl();

        security.AddAccessRule(
            new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User!,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

        info.SetAccessControl(security);
    }

    /// <summary>
    ///     Creates the parameters of the container that runs <paramref name="executablePath"/>.
    /// </summary>
    /// <param name="installDirectory">The full path of the install.</param>
    /// <param name="executablePath">The path of the executable, relative to the install.</param>
    /// <param name="arguments">The arguments, separated by spaces.</param>
    /// <returns>
    ///     The parameters.
    /// </returns>
    internal static CreateContainerParameters NewContainerParameters(string installDirectory, string executablePath, string arguments)
    {
        string path = Path.Combine(MountPath, executablePath);
        List<string> command = [path, .. arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        return new CreateContainerParameters
        {
            Image = Image,
            Cmd = command,
            WorkingDir = Path.GetDirectoryName(path)!,
            HostConfig = new HostConfig { Binds = [$"{installDirectory}:{MountPath}"] },
        };
    }

    /// <summary>
    ///     Checks whether <paramref name="exception"/> comes from a Docker engine that doesn't run or refuses a request.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>
    ///     <see langword="true"/> if it does.
    /// </returns>
    private static bool IsDockerFailure(Exception exception)
    {
        return exception is DockerApiException or HttpRequestException or IOException or TimeoutException;
    }

    /// <summary>
    ///     Pulls <see cref="Image"/> unless this machine has it.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the pull.</param>
    /// <returns>
    ///     A task that completes once the machine has the image.
    /// </returns>
    private async Task EnsureImageAsync(CancellationToken cancellationToken)
    {
        if (_imageReady)
        {
            return;
        }

        try
        {
            _ = await _client.Images.InspectImageAsync(Image, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerImageNotFoundException)
        {
            ImagesCreateParameters parameters = new() { FromImage = ImageName, Tag = ImageTag };
            await _client.Images.CreateImageAsync(parameters, null, new Progress<JSONMessage>(), cancellationToken).ConfigureAwait(false);
        }

        _imageReady = true;
    }

    /// <summary>
    ///     Creates and starts the container of <paramref name="parameters"/>.
    /// </summary>
    /// <param name="parameters">The parameters of the container.</param>
    /// <param name="cancellationToken">The token that stops the start.</param>
    /// <returns>
    ///     The started container.
    /// </returns>
    private async Task<IHostedRun> RunAsync(CreateContainerParameters parameters, CancellationToken cancellationToken)
    {
        var container = await _client.Containers.CreateContainerAsync(parameters, cancellationToken).ConfigureAwait(false);

        try
        {
            _ = await _client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Remove the container, because a container that never started stays on the machine.
            await TryRemoveAsync(container.ID).ConfigureAwait(false);

            throw;
        }

        // Wait after the start, because the wait returns at once for a container that never ran. A container that has
        // exited stays until DisposeAsync removes it, so the wait still gets its exit code.
        var exit = WaitAsync(container.ID, parameters.Image);

        return new ContainerRun(this, container.ID, exit);
    }

    /// <summary>
    ///     Waits for the container <paramref name="containerId"/> to exit.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="image">The image of the container, for messages.</param>
    /// <returns>
    ///     The response of the wait, with the exit code.
    /// </returns>
    /// <exception cref="InvalidOperationException">The Docker engine stops or loses the container during the wait.</exception>
    private async Task<ContainerWaitResponse> WaitAsync(string containerId, string image)
    {
        try
        {
            return await _client.Containers.WaitContainerAsync(containerId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            // Wrap the failure the way StartAsync does, because ExitCode and WaitForExitAsync rethrow it to the check of the player.
            throw new InvalidOperationException(SR.FormatDockerLost(image, exception.Message), exception);
        }
    }

    /// <summary>
    ///     Removes the container <paramref name="containerId"/>, also a running one. Catches a Docker failure of the removal,
    ///     because its exception would hide the failed start or the timeout or exit of the run.
    /// </summary>
    /// <param name="containerId">The ID of the container.</param>
    /// <returns>
    ///     A task that completes once the removal is done or has failed.
    /// </returns>
    internal async Task TryRemoveAsync(string containerId)
    {
        try
        {
            ContainerRemoveParameters remove = new() { Force = true };
            await _client.Containers.RemoveContainerAsync(containerId, remove, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            // Skip the failed removal, because its exception would hide the failure of the start or of the run.
        }
    }
}

/// <summary>
///     Represents a started container, whose exit code is the exit code of the executable.
/// </summary>
/// <param name="host">The host that started the container.</param>
/// <param name="containerId">The ID of the container.</param>
/// <param name="exit">The wait that completes once the container has exited.</param>
file sealed class ContainerRun(
    ContainerHost host,
    string containerId,
    Task<ContainerWaitResponse> exit) : IHostedRun
{
    /// <inheritdoc/>
    public bool HasExited => exit.IsCompleted;

    /// <inheritdoc/>
    public int ExitCode => unchecked((int)exit.GetAwaiter().GetResult().StatusCode);

    /// <inheritdoc/>
    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return exit.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Removes the container, also a running one, so the mounted install is free once the call completes.
    /// </summary>
    /// <remarks>
    ///     Catches a Docker failure of the removal, because its exception would hide the timeout or the exit of the run.
    /// </remarks>
    /// <returns>
    ///     A task that completes once the removal is done or has failed.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        // Observe a failure of the wait, because the removal of a running container fails it and no caller awaits it after a timeout.
        _ = exit.ContinueWith(
            static wait => wait.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await host.TryRemoveAsync(containerId).ConfigureAwait(false);
    }
}
