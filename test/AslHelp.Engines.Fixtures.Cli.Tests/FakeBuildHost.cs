using AslHelp.Engines.Fixtures.Cli.Hosting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Records the build steps and runs a callback per step in place of a container.
/// </summary>
/// <param name="onStep">
///     The callback that does what the step would do and returns its exit code or <see langword="null"/> for a step that never exits.
/// </param>
internal sealed class FakeBuildHost(
    Func<BuildStep, int?> onStep) : IBuildHost
{
    /// <summary>
    ///     Gets the number of disposed runs. Each one stands for a removed container.
    /// </summary>
    public int DisposedRuns { get; private set; }

    /// <summary>
    ///     Gets the folders of <see cref="GrantCurrentUser"/>, each with the number of steps started before the call.
    /// </summary>
    public List<(string Directory, int StepsBefore)> GrantedFolders { get; } = [];

    /// <summary>
    ///     Gets a value indicating whether <see cref="HasImageAsync"/> finds every image. The default is <see langword="true"/>.
    /// </summary>
    public bool HasImage { get; init; } = true;

    /// <summary>
    ///     Gets the owners of <see cref="RemoveStepsAsync"/>, each with the number of steps started before the call.
    /// </summary>
    public List<(string Owner, int StepsBefore)> RemovedOwners { get; } = [];

    /// <summary>
    ///     Gets the steps in start order.
    /// </summary>
    public List<BuildStep> Steps { get; } = [];

    /// <inheritdoc/>
    public Task<bool> HasImageAsync(string image, CancellationToken cancellationToken)
    {
        return Task.FromResult(HasImage);
    }

    /// <inheritdoc/>
    public Task<IHostedRun> StartAsync(BuildStep step, CancellationToken cancellationToken)
    {
        Steps.Add(step);

        return Task.FromResult<IHostedRun>(new FakeRun(onStep(step), () => DisposedRuns++));
    }

    /// <inheritdoc/>
    public Task RemoveStepsAsync(string owner, CancellationToken cancellationToken)
    {
        RemovedOwners.Add((owner, Steps.Count));

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void GrantCurrentUser(string directory)
    {
        GrantedFolders.Add((directory, Steps.Count));
    }
}

/// <summary>
///     Represents a step that has exited with a code or never exits.
/// </summary>
/// <param name="exitCode">The exit code or <see langword="null"/> for a step that never exits.</param>
/// <param name="onDispose">The callback of the dispose.</param>
file sealed class FakeRun(
    int? exitCode,
    Action onDispose) : IHostedRun
{
    /// <inheritdoc/>
    public int ExitCode => exitCode ?? throw new InvalidOperationException();

    /// <inheritdoc/>
    public bool HasExited => exitCode is not null;

    /// <inheritdoc/>
    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return exitCode is null ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        onDispose();

        return ValueTask.CompletedTask;
    }
}
