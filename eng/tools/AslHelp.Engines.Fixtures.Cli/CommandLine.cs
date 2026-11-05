using AslHelp.Engines.Fixtures.Cli.Unreal3;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Parses and runs the fixture commands.
/// </summary>
internal static class CommandLine
{
    private const string ProjectsPath = "projects";

    /// <summary>
    ///     Parses <paramref name="args"/> and runs the command in them with every toolchain and the WebDAV cache.
    /// </summary>
    /// <param name="args">The command and its arguments.</param>
    /// <param name="output">The writer for help and progress.</param>
    /// <param name="error">The writer for parse errors and failures.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        using Downloader downloader = new();

        return await RunAsync(
            args,
            output,
            error,
            [new Udk(downloader)],
            DavStore.FromEnvironmentIfSet,
            Path.Combine(AppContext.BaseDirectory, ProjectsPath)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Parses <paramref name="args"/> and runs the command in them with <paramref name="toolchains"/>.
    /// </summary>
    /// <param name="args">The command and its arguments.</param>
    /// <param name="output">The writer for help and progress.</param>
    /// <param name="error">The writer for parse errors and failures.</param>
    /// <param name="toolchains">The toolchains the commands can pick by name.</param>
    /// <param name="openStore">
    ///     The function that opens the cache store for a build or returns <see langword="null"/> to keep the player local.
    ///     The function gets the writer for the steps, for a line about how to set up the store.
    /// </param>
    /// <param name="projectsDirectory">The folder with 1 player project folder per toolchain name.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyList<IToolchain> toolchains,
        Func<TextWriter, ICacheStore?> openStore,
        string projectsDirectory)
    {
        RootCommand root = [with(SR.RootDescription)];

        // Print the help and fail, so a script that passes no command stops.
        root.SetAction(parseResult =>
        {
            _ = new HelpAction().Invoke(parseResult);
            return 1;
        });

        root.Subcommands.Add(NewBuildCommand(output, error, toolchains, openStore, projectsDirectory));

        InvocationConfiguration configuration = new()
        {
            Output = output,
            Error = error,
        };

        return root.Parse(args).InvokeAsync(configuration);
    }

    /// <summary>
    ///     Creates the <c>build</c> command, which installs a toolchain, builds its player, checks the player and caches it.
    /// </summary>
    /// <param name="output">The writer for the steps and the player folder.</param>
    /// <param name="error">The writer for a failed step.</param>
    /// <param name="toolchains">The toolchains the command can pick by name.</param>
    /// <param name="openStore">
    ///     The function that opens the cache store with the writer for the steps or returns <see langword="null"/> for none.
    /// </param>
    /// <param name="projectsDirectory">The folder with 1 player project folder per toolchain name.</param>
    /// <returns>
    ///     The command.
    /// </returns>
    private static Command NewBuildCommand(
        TextWriter output,
        TextWriter error,
        IReadOnlyList<IToolchain> toolchains,
        Func<TextWriter, ICacheStore?> openStore,
        string projectsDirectory)
    {
        string[] names = new string[toolchains.Count];

        for (int i = 0; i < names.Length; i++)
        {
            names[i] = toolchains[i].Name;
        }

        Argument<string> toolchainArgument = new("toolchain") { Description = SR.ToolchainDescription };
        toolchainArgument.AcceptOnlyFromAmong(names);

        Argument<string> versionArgument = new("version") { Description = SR.VersionDescription };
        Option<string> outputOption = new("--output") { Description = SR.OutputDescription, Required = true };

        Command build = new("build", SR.BuildDescription)
        {
            toolchainArgument,
            versionArgument,
            outputOption,
        };

        build.SetAction((parseResult, cancellationToken) =>
        {
            string name = parseResult.GetRequiredValue(toolchainArgument);
            var toolchain = FindToolchain(toolchains, name);

            return BuildAsync(
                toolchain,
                parseResult.GetRequiredValue(versionArgument),
                parseResult.GetRequiredValue(outputOption),
                openStore,
                projectsDirectory,
                output,
                error,
                cancellationToken);
        });

        return build;
    }

    /// <summary>
    ///     Gets the toolchain called <paramref name="name"/>, compared ordinally.
    /// </summary>
    /// <param name="toolchains">The toolchains.</param>
    /// <param name="name">The toolchain name from the command line, already checked by the parser.</param>
    /// <returns>
    ///     The toolchain.
    /// </returns>
    private static IToolchain FindToolchain(IReadOnlyList<IToolchain> toolchains, string name)
    {
        for (int i = 0; i < toolchains.Count; i++)
        {
            if (toolchains[i].Name.Equals(name, StringComparison.Ordinal))
            {
                return toolchains[i];
            }
        }

        throw new UnreachableException(SR.Format(SR.ToolchainNotListed, name));
    }

    /// <summary>
    ///     Restores the player of <paramref name="version"/> of <paramref name="toolchain"/> from the cache
    ///     or installs the toolchain, builds the player, checks it and caches it.
    /// </summary>
    /// <param name="toolchain">The toolchain.</param>
    /// <param name="version">The version to install.</param>
    /// <param name="outputDirectory">The folder for the downloads, the install and the player.</param>
    /// <param name="openStore">
    ///     The function that opens the cache store with the writer for the steps or returns <see langword="null"/> for none.
    /// </param>
    /// <param name="projectsDirectory">The folder with 1 player project folder per toolchain name.</param>
    /// <param name="output">The writer for the steps and the player folder.</param>
    /// <param name="error">The writer for a failed step.</param>
    /// <param name="cancellationToken">The token that stops the running step.</param>
    /// <returns>
    ///     0 once the player folder is ready or 1 after a failed step.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private static async Task<int> BuildAsync(
        IToolchain toolchain,
        string version,
        string outputDirectory,
        Func<TextWriter, ICacheStore?> openStore,
        string projectsDirectory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build the key first, so a missing project folder or an unknown version fails before the store opens and before the install.
            string project = Path.Combine(projectsDirectory, toolchain.Name);
            var key = PlayerCache.KeyOf(toolchain, version, project);

            using var store = openStore(output);

            // Draw the bars on the writer of the steps, so the bars and the step lines come out in order.
            var bars = TransferBars.Create(output);
            PlayerCache cache = new(store, outputDirectory, output, bars);
            string? player = await cache.TryRestoreAsync(key, cancellationToken).ConfigureAwait(false);

            if (player is null)
            {
                string installing = SR.Format(SR.Installing, toolchain.Name, key.Version, outputDirectory);
                await output.WriteLineAsync(installing.AsMemory(), cancellationToken).ConfigureAwait(false);
                string install = await bars.ShowAsync(
                    $"{toolchain.Name} {key.Version}",
                    progress => toolchain.InstallAsync(key.Version, outputDirectory, progress, cancellationToken)).ConfigureAwait(false);

                await output.WriteLineAsync(SR.Format(SR.Building, project, install).AsMemory(), cancellationToken).ConfigureAwait(false);
                await toolchain.BuildAsync(install, project, cancellationToken).ConfigureAwait(false);

                await output.WriteLineAsync(SR.Format(SR.Checking, install).AsMemory(), cancellationToken).ConfigureAwait(false);
                await toolchain.CheckAsync(install, cancellationToken).ConfigureAwait(false);

                player = await cache.StoreAsync(key, install, cancellationToken).ConfigureAwait(false);
            }

            await output.WriteLineAsync(player.AsMemory(), cancellationToken).ConfigureAwait(false);

            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Print the causes under the message, because a failure of the network or the disk keeps its reason in an inner exception.
            await error.WriteLineAsync(FailureText(exception).AsMemory(), CancellationToken.None).ConfigureAwait(false);

            return 1;
        }
    }

    /// <summary>
    ///     Joins the message of <paramref name="exception"/> with the message of each inner exception that it does not
    ///     contain yet, 1 per line.
    /// </summary>
    /// <param name="exception">The exception of the failed step.</param>
    /// <returns>
    ///     The text to print.
    /// </returns>
    internal static string FailureText(Exception exception)
    {
        string text = exception.Message;

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (!text.Contains(inner.Message, StringComparison.Ordinal))
            {
                text += Environment.NewLine + inner.Message;
            }
        }

        return text;
    }
}
