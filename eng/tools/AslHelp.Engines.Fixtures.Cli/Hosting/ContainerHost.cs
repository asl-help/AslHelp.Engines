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
///     needs nothing that only the build machine has. Runs build steps in containers of their build image too.
/// </summary>
/// <param name="client">The client of the Docker engine, which the host disposes.</param>
internal sealed class ContainerHost(
    DockerClient client) : IPlayerHost, IBuildHost, IDisposable
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
    private const string OwnerLabel = "asl-help.fixtures.owner";

    private readonly DockerClient _client = client;

    private bool _imageReady;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContainerHost"/> class with a client of the Docker engine of this machine.
    /// </summary>
    public ContainerHost()
        : this(new DockerClientBuilder().Build())
    {
    }

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
    /// <exception cref="InvalidOperationException">The Docker engine does not run.</exception>
    public async Task<bool> HasImageAsync(string image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            return await ImageExistsAsync(image, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            throw new InvalidOperationException(SR.FormatDockerLookupFailed(image, exception.Message), exception);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The image isn't pulled, so a missing image fails the start. The container gets the owner of the step as a label, so
    ///     <see cref="RemoveStepsAsync"/> finds it after a <c>fixtures</c> process that gets killed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The Docker engine does not run or refuses the image or the container.</exception>
    public async Task<IHostedRun> StartAsync(BuildStep step, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        try
        {
            return await RunAsync(NewStepParameters(step), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            throw new InvalidOperationException(SR.FormatDockerFailed(step.Image, exception.Message), exception);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The Docker engine does not run or refuses the removal.</exception>
    public async Task RemoveStepsAsync(string owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        try
        {
            ContainersListParameters parameters = new()
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [$"{OwnerLabel}={owner}"] = true },
                },
            };

            foreach (var container in await _client.Containers.ListContainersAsync(parameters, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    ContainerRemoveParameters remove = new() { Force = true };
                    await _client.Containers.RemoveContainerAsync(container.ID, remove, cancellationToken).ConfigureAwait(false);
                }
                catch (DockerContainerNotFoundException)
                {
                    // Skip the container, because something else removed it after the list.
                }
            }
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            throw new InvalidOperationException(SR.FormatDockerRemoveFailed(owner, exception.Message), exception);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Gives this user full control of <paramref name="directory"/> and of everything created in it later. A file that a
    ///     container writes belongs to the administrator of the container, so without the inherited rule this user can neither
    ///     delete nor move it once the container is gone. A folder that has the rule already stays as it is.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">This user cannot change the rules of the folder.</exception>
    public void GrantCurrentUser(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        DirectoryInfo info = new(directory);
        var security = info.GetAccessControl();
        var user = WindowsIdentity.GetCurrent().User!;
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        // Skip a folder that has the rule, because each set propagates the rule through the whole tree again, which takes minutes on a large folder.
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference == user
                && rule.AccessControlType == AccessControlType.Allow
                && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)
                && rule.InheritanceFlags == inheritance
                && rule.PropagationFlags == PropagationFlags.None)
            {
                return;
            }
        }

        security.AddAccessRule(
            new FileSystemAccessRule(user, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

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
    ///     Creates the parameters of the container that runs <paramref name="step"/>.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <returns>
    ///     The parameters.
    /// </returns>
    internal static CreateContainerParameters NewStepParameters(BuildStep step)
    {
        List<string> binds = [];

        foreach (var mount in step.Mounts)
        {
            binds.Add(mount.ReadOnly ? $"{mount.HostPath}:{mount.ContainerPath}:ro" : $"{mount.HostPath}:{mount.ContainerPath}");
        }

        List<string> environment = [];

        foreach (var (name, value) in step.Environment)
        {
            environment.Add($"{name}={value}");
        }

        return new CreateContainerParameters
        {
            Image = step.Image,
            Cmd = [.. step.Command],
            Env = environment,
            HostConfig = new HostConfig { Binds = binds },
            Labels = new Dictionary<string, string> { [OwnerLabel] = step.Owner },
        };
    }

    /// <summary>
    ///     Removes the container <paramref name="containerId"/>, also a running one, and ignores a Docker failure of the removal,
    ///     so the failure of the start or the timeout or exit of the run reaches the caller.
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
            // Leave the container on the machine, because the failure of the start or the run has the reason to show.
        }
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

        if (!await ImageExistsAsync(Image, cancellationToken).ConfigureAwait(false))
        {
            ImagesCreateParameters parameters = new() { FromImage = ImageName, Tag = ImageTag };
            await _client.Images.CreateImageAsync(parameters, null, new Progress<JSONMessage>(), cancellationToken).ConfigureAwait(false);
        }

        _imageReady = true;
    }

    /// <summary>
    ///     Checks whether the Docker engine has <paramref name="image"/>.
    /// </summary>
    /// <param name="image">The image with its tag.</param>
    /// <param name="cancellationToken">The token that stops the check.</param>
    /// <returns>
    ///     <see langword="true"/> if it does.
    /// </returns>
    private async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            _ = await _client.Images.InspectImageAsync(image, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
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
            // Wrap the failure like the start, because the exit code and the wait rethrow it to the caller.
            throw new InvalidOperationException(SR.FormatDockerLost(image, exception.Message), exception);
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
    public int ExitCode => unchecked((int)exit.GetAwaiter().GetResult().StatusCode);

    /// <inheritdoc/>
    public bool HasExited => exit.IsCompleted;

    /// <inheritdoc/>
    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return exit.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Removes the container, also a running one, so the mounted folders are free once the call completes.
    /// </summary>
    /// <remarks>
    ///     Ignores a Docker failure of the removal, so the timeout or the exit of the run stays the failure that the caller sees.
    /// </remarks>
    /// <returns>
    ///     A task that completes once the removal is done or has failed.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        // Observe a failure of the wait, because the removal of a running container fails it and nobody awaits it after a timeout.
        _ = exit.ContinueWith(
            static wait => wait.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await host.TryRemoveAsync(containerId).ConfigureAwait(false);
    }
}
