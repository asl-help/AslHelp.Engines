using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Transfer;
using AslHelp.Engines.Fixtures.Cli.Unreal3;
using AslHelp.Engines.Fixtures.Cli.Unreal4;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Parses and runs the fixture commands.
/// </summary>
internal static class CommandLine
{
    private const string BuildPath = "build";
    private const string LockFileName = ".fixtures.lock";
    private const string ProjectsPath = "projects";

    // Match ERROR_SHARING_VIOLATION as an HRESULT, because another run has the lock file open.
    private const int SharingViolation = unchecked((int)0x80070020);

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
        using ContainerHost host = new();
        Unreal unreal = new(new UnrealEngine(output), host, host, Path.Combine(AppContext.BaseDirectory, BuildPath, Unreal.ToolchainName));

        return await RunAsync(
            args,
            output,
            error,
            [new Udk(downloader, host), unreal],
            DavStore.FromEnvironmentIfSet,
            Path.Combine(AppContext.BaseDirectory, ProjectsPath)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Parses <paramref name="args"/> and runs the command in them with <paramref name="toolchains"/>.
    /// </summary>
    /// <param name="args">The command and its arguments.</param>
    /// <param name="output">The writer for help and progress.</param>
    /// <param name="error">The writer for parse errors and failures.</param>
    /// <param name="toolchains">The toolchains, each with its own subcommand of <c>build</c>.</param>
    /// <param name="openStore">
    ///     The function that opens the cache store for a build or returns <see langword="null"/> to keep the players local.
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
        Argument<string> versionArgument = new("version") { Description = SR.VersionDescription };
        Option<string> outputOption = new("--output") { Description = SR.OutputDescription, Required = true };

        Command build = new("build", SR.BuildDescription);
        build.SetAction(PrintHelpAndFail);

        foreach (var toolchain in toolchains)
        {
            Command command =
            [
                with(toolchain.Name, SR.FormatToolchainBuildDescription(toolchain.Name)),
                versionArgument,
                outputOption,
                .. toolchain.Options,
            ];

            command.SetAction((parseResult, cancellationToken) => BuildAsync(
                toolchain,
                parseResult.GetRequiredValue(versionArgument),
                parseResult.GetRequiredValue(outputOption),
                parseResult,
                openStore,
                projectsDirectory,
                output,
                error,
                cancellationToken));

            build.Subcommands.Add(command);
        }

        RootCommand root = [with(SR.RootDescription), build];
        root.SetAction(PrintHelpAndFail);

        return root.Parse(args).InvokeAsync(new() { Output = output, Error = error });
    }

    /// <summary>
    ///     Prints the help of the parsed command and fails, so a script without a subcommand stops.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>
    ///     1, the exit code of a failed run.
    /// </returns>
    private static int PrintHelpAndFail(ParseResult parseResult)
    {
        _ = new HelpAction().Invoke(parseResult);

        return 1;
    }

    /// <summary>
    ///     Restores the players of <paramref name="version"/> of <paramref name="toolchain"/> from the cache. If any are
    ///     missing, it installs the toolchain once, builds them in 1 pass and checks and caches each in order.
    /// </summary>
    /// <param name="toolchain">The toolchain.</param>
    /// <param name="version">The version as the user typed it.</param>
    /// <param name="outputDirectory">The folder for the downloads, the install and the players.</param>
    /// <param name="parseResult">The parsed command line with the values of the toolchain options.</param>
    /// <param name="openStore">
    ///     The function that opens the cache store with the writer for the steps or returns <see langword="null"/> for none.
    /// </param>
    /// <param name="projectsDirectory">The folder with 1 player project folder per toolchain name.</param>
    /// <param name="output">The writer for the steps and the player folders.</param>
    /// <param name="error">The writer for a failed step.</param>
    /// <param name="cancellationToken">The token that stops the running step.</param>
    /// <returns>
    ///     0 once every player folder is ready or 1 after a failed step. Players cached before the failed step stay cached.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private static async Task<int> BuildAsync(
        IToolchain toolchain,
        string version,
        string outputDirectory,
        ParseResult parseResult,
        Func<TextWriter, ICacheStore?> openStore,
        string projectsDirectory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var listed = toolchain.ListPlayers(version, parseResult);

            // Fail on an empty list, because a run that exits with 0 without a player folder looks like a success to a script.
            if (listed.Count == 0)
            {
                throw new InvalidOperationException(SR.FormatNoPlayers(toolchain.Name, version));
            }

            // Build the keys first, so a missing project folder or an unknown version fails before the store opens and before the install.
            string project = Path.Combine(projectsDirectory, toolchain.Name);
            PlayerKey[] keys = [.. listed.Select(player => PlayerCache.KeyOf(toolchain, player, project))];

            // Fail on a repeated player here, because after the install and the build its 2nd store needs the folder that the 1st store moved.
            if (keys.CountBy(static key => key.Version).FirstOrDefault(static entry => entry.Value > 1).Key is { } repeated)
            {
                throw new InvalidOperationException(SR.FormatPlayerListedTwice(toolchain.Name, version, repeated));
            }

            // Lock the output folder for the whole run, because a 2nd run into it deletes the .partial files of the 1st.
            using var outputLock = LockOutput(outputDirectory);

            using var store = openStore(output);

            // Draw the bars on the writer of the steps, so the bars and the step lines come out in order.
            var bars = TransferBars.Create(output);
            PlayerCache cache = new(store, outputDirectory, output, bars);

            string?[] folders = new string?[keys.Length];
            List<int> missing = [];

            for (int i = 0; i < keys.Length; i++)
            {
                folders[i] = await cache.TryRestoreAsync(keys[i], cancellationToken).ConfigureAwait(false);

                if (folders[i] is null)
                {
                    missing.Add(i);
                }
            }

            if (missing.Count > 0)
            {
                string first = keys[missing[0]].Version;
                string installing = SR.FormatInstalling(toolchain.Name, first, outputDirectory);
                await output.WriteLineAsync(installing.AsMemory(), cancellationToken).ConfigureAwait(false);
                string install = await bars.ShowAsync(
                    $"{toolchain.Name} {first}",
                    progress => toolchain.InstallAsync(first, outputDirectory, progress, cancellationToken)).ConfigureAwait(false);

                string[] players = [.. missing.Select(index => keys[index].Version)];
                string building = SR.FormatBuilding(project, install, string.Join(", ", players));
                await output.WriteLineAsync(building.AsMemory(), cancellationToken).ConfigureAwait(false);
                var built = await toolchain.BuildAsync(install, project, players, cancellationToken).ConfigureAwait(false);

                // Fail on a wrong count, because the folders pair up with the players by position and a wrong pair caches the wrong player.
                if (built.Count != players.Length)
                {
                    throw new InvalidOperationException(SR.FormatBuiltFolderCount(toolchain.Name, players.Length, built.Count));
                }

                for (int i = 0; i < missing.Count; i++)
                {
                    await output.WriteLineAsync(SR.FormatChecking(built[i]).AsMemory(), cancellationToken).ConfigureAwait(false);
                    await toolchain.CheckAsync(built[i], cancellationToken).ConfigureAwait(false);

                    folders[missing[i]] = await cache.StoreAsync(keys[missing[i]], built[i], cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (string? folder in folders)
            {
                await output.WriteLineAsync(folder.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

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
    ///     Creates the folder and opens its lock file with no sharing, so a 2nd run into the folder fails at once.
    /// </summary>
    /// <param name="outputDirectory">The folder for the downloads, the install and the players.</param>
    /// <returns>
    ///     The open lock file, which is deleted once it is disposed.
    /// </returns>
    /// <exception cref="IOException">Another run has the folder or the lock file cannot be created.</exception>
    private static FileStream LockOutput(string outputDirectory)
    {
        string folder = Directory.CreateDirectory(outputDirectory).FullName;
        string path = Path.Combine(folder, LockFileName);

        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException exception) when (exception.HResult == SharingViolation)
        {
            throw new IOException(SR.FormatOutputInUse(folder), exception);
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
