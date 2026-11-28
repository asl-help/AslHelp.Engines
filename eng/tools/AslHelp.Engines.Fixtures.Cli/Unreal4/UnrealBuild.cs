using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Unreal4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Creates and runs the build steps of the fixture, each a script of <c>eng/fixtures/unreal4/build</c> in a container of the
///     build image.
/// </summary>
/// <remarks>
///     The scripts have the build tool command lines. A step only adds the script arguments, the switches of the row and the log
///     redirect, so a person can run the same script with <c>docker run</c>.
/// </remarks>
internal static class UnrealBuild
{
    private const string EditorFlagsVariable = "FIXTURE_EDITOR_FLAGS";
    private const string PackageFlagsVariable = "FIXTURE_PACKAGE_FLAGS";
    private const string PathPrependVariable = "FIXTURE_PATH_PREPEND";

    /// <summary>
    ///     Represents how long <see cref="RunAsync"/> lets a step run by default, 90 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromMinutes(90);

    /// <summary>
    ///     Creates the step that copies the runtime DLLs of the image into <c>runtimes/x64</c> and <c>runtimes/x86</c> of
    ///     <see cref="UnrealPaths.Out"/>.
    /// </summary>
    /// <param name="image">The build image with its tag.</param>
    /// <param name="paths">The folders of the build.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public static BuildStep RuntimesStep(string image, UnrealPaths paths)
    {
        return NewStep(image, paths, new UnrealBuildStep.Runtimes(), "runtimes.bat", []);
    }

    /// <summary>
    ///     Creates the step that builds the editor target, which the cook needs.
    /// </summary>
    /// <param name="release">The row of the engine, for its editor switches and its PATH folder.</param>
    /// <param name="image">The build image with its tag.</param>
    /// <param name="paths">The folders of the build.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public static BuildStep EditorStep(UnrealRelease release, string image, UnrealPaths paths)
    {
        var environment = NewEnvironment(release, EditorFlagsVariable, release.EditorFlags);

        return NewStep(image, paths, new UnrealBuildStep.Editor(), "editor.bat", environment);
    }

    /// <summary>
    ///     Creates the step that builds, stages, paks and archives <paramref name="player"/> into <c>&lt;tag&gt;</c> of
    ///     <see cref="UnrealPaths.Out"/>.
    /// </summary>
    /// <param name="release">The row of the engine, for its package switches and its PATH folder.</param>
    /// <param name="image">The build image with its tag.</param>
    /// <param name="paths">The folders of the build.</param>
    /// <param name="player">The player.</param>
    /// <param name="cook">Whether the step cooks the content or reuses the content an earlier step cooked.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public static BuildStep PackageStep(UnrealRelease release, string image, UnrealPaths paths, UnrealPlayer player, UnrealCookMode cook)
    {
        var environment = NewEnvironment(release, PackageFlagsVariable, release.PackageFlags);
        string script = $"package.bat {player.Platform} {player.Configuration} {player.Tag} {(cook == UnrealCookMode.Cook ? "-cook" : "-skipcook")}";

        return NewStep(image, paths, new UnrealBuildStep.Package(player), script, environment);
    }

    /// <summary>
    ///     Runs <paramref name="step"/> in <paramref name="host"/> and waits for it.
    /// </summary>
    /// <param name="host">The host the step runs in.</param>
    /// <param name="step">The step.</param>
    /// <param name="buildStep">The step of the build, for its log name.</param>
    /// <param name="paths">The folders of the build, for the log.</param>
    /// <param name="timeout">The time limit of the step, counted from the start of its container.</param>
    /// <param name="cancellationToken">The token that stops the step and removes its container.</param>
    /// <returns>
    ///     A task that completes once the step exits with code 0.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The host cannot run the step or the step exits with another code. The message has the last lines of the log.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The step runs longer than <paramref name="timeout"/>.</exception>
    public static async Task RunAsync(
        IBuildHost host,
        BuildStep step,
        UnrealBuildStep buildStep,
        UnrealPaths paths,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        string logPath = paths.LogPath(buildStep);
        int exitCode;
        var run = await host.StartAsync(step, cancellationToken).ConfigureAwait(false);

        await using (run.ConfigureAwait(false))
        {
            // Start the clock once the step runs, so the start of the container doesn't count against it.
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await run.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(UnrealSR.FormatStepNotDone(buildStep.Name, DurationText.Format(timeout), logPath));
            }

            exitCode = run.ExitCode;
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(UnrealSR.FormatStepFailed(buildStep.Name, exitCode, logPath, Tail(logPath)));
        }
    }

    /// <summary>
    ///     Creates the variables of a step: the switches of the row and the PATH folder if the row has one.
    /// </summary>
    /// <param name="release">The row.</param>
    /// <param name="flagsVariable">The variable of the switches.</param>
    /// <param name="flags">The switches.</param>
    /// <returns>
    ///     The variables.
    /// </returns>
    private static Dictionary<string, string> NewEnvironment(UnrealRelease release, string flagsVariable, IReadOnlyList<string> flags)
    {
        Dictionary<string, string> environment = new() { [flagsVariable] = string.Join(' ', flags) };

        if (release.PathPrepend is { } folder)
        {
            environment[PathPrependVariable] = folder;
        }

        return environment;
    }

    /// <summary>
    ///     Creates a step that runs a script of <see cref="UnrealPaths.Steps"/> with its output in the log of the step.
    /// </summary>
    /// <param name="image">The build image with its tag.</param>
    /// <param name="paths">The folders of the build.</param>
    /// <param name="buildStep">The step of the build, for its log name.</param>
    /// <param name="script">The script file name and its arguments.</param>
    /// <param name="environment">The variables of the step.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    private static BuildStep NewStep(
        string image,
        UnrealPaths paths,
        UnrealBuildStep buildStep,
        string script,
        Dictionary<string, string> environment)
    {
        // Redirect both streams into the logs mount, so the log is a plain file on the host even after the container is gone.
        string commandLine = $@"{UnrealPaths.StepsMount}\{script} > {UnrealPaths.LogsMount}\{buildStep.Name}.log 2>&1";

        return new BuildStep(image, ["cmd", "/c", commandLine], paths.Mounts, environment, paths.Scratch);
    }

    /// <summary>
    ///     Reads the last lines of a step log that aren't blank.
    /// </summary>
    /// <param name="logPath">The log.</param>
    /// <returns>
    ///     The lines with a line break before each. Empty if the log doesn't exist.
    /// </returns>
    private static string Tail(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "";
        }

        Queue<string> tail = new(ProcessRunner.TailLineCount);

        // Read line by line, because a build log can have many MB. Decode with the OEM code page, because cmd redirects the
        // console output of the step tools as is.
        foreach (string line in File.ReadLines(logPath, ProcessRunner.OemEncoding))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                ProcessRunner.AddToTail(tail, line);
            }
        }

        return string.Concat(tail.Select(line => Environment.NewLine + line));
    }
}
