using AslHelp.Engines.Fixtures.Cli.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Installs a pinned engine release from Epic through legendary, into a folder per app under <c>engines</c>.
/// </summary>
/// <remarks>
///     The embedded <c>install_engine.py</c> runs legendary through uv, so legendary and Python stay pinned and don't have to
///     be installed on the machine. The Epic login stays in the config folder of legendary and this class never reads it.
/// </remarks>
/// <param name="output">The writer of the step lines.</param>
internal sealed class UnrealEngine(
    TextWriter output)
{
    /// <summary>
    ///     Represents the folder of the engines under the output folder.
    /// </summary>
    public const string EnginesFolder = "engines";

    /// <summary>
    ///     Represents the pinned version of legendary. <c>justfile</c> and <c>readme.md</c> repeat it and a test keeps them equal.
    /// </summary>
    public const string LegendaryVersion = "0.21.1";

    /// <summary>
    ///     Represents the pinned version of Python. <c>justfile</c> and <c>readme.md</c> repeat it and a test keeps them equal.
    /// </summary>
    public const string PythonVersion = "3.14";

    private const int ExitFilesBad = 13;
    private const int ExitListedAsInstalled = 12;
    private const int ExitNotLoggedIn = 10;
    private const int ExitNotPinned = 11;
    private const int FileNotFoundError = 2;
    private const string LegendaryPackage = $"legendary-gl=={LegendaryVersion}";
    private const string ScriptFileName = "install_engine.py";
    private const string ScriptResource = "Unreal4/install_engine.py";

    /// <summary>
    ///     Gets the source of the free space, given a folder on the drive. The default is <see cref="EngineSpace.FreeBytes"/>.
    /// </summary>
    public Func<string, long> FreeBytes { get; init; } = EngineSpace.FreeBytes;

    /// <summary>
    ///     Gets the path of uv. The default is <c>uv</c> from PATH.
    /// </summary>
    public string UvPath { get; init; } = "uv";

    /// <summary>
    ///     Installs <paramref name="release"/> unless its folder has a marker with the pinned sha1.
    /// </summary>
    /// <remarks>
    ///     The engine goes to <c>engines/&lt;app&gt;</c> under <paramref name="outputDirectory"/> and its manifest to
    ///     <c>engines/&lt;app&gt;.manifest</c>. A marker with the pinned sha1 doesn't start the script. A marker with another sha1
    ///     deletes the folder first. A folder without a marker resumes and only its missing bytes count as needed space. Finished
    ///     engines of the folder make room as <see cref="EngineSpace.MakeRoom"/> does.
    /// </remarks>
    /// <param name="release">The release to install.</param>
    /// <param name="outputDirectory">The folder with the <c>engines</c> folder.</param>
    /// <param name="cancellationToken">The token that stops the install.</param>
    /// <returns>
    ///     The full path of the engine folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="release"/> or <paramref name="outputDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FileNotFoundException">uv is not installed.</exception>
    /// <exception cref="IOException">
    ///     The drive doesn't have room for the install plus the scratch. Or the output folder doesn't start with a drive letter.
    /// </exception>
    /// <exception cref="InvalidDataException">The manifest doesn't match its pin or a file fails the hash check.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Legendary isn't logged in, lists the app as installed or the script exits with another code.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string> InstallAsync(UnrealRelease release, string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        // Check the token before any delete, because a canceled call must leave every engine in place.
        cancellationToken.ThrowIfCancellationRequested();

        string engines = Path.Combine(Path.GetFullPath(outputDirectory), EnginesFolder);
        string engine = Path.Combine(engines, release.App);
        _ = Directory.CreateDirectory(engines);

        string? marker = EngineMarker.Read(engine);

        string[] scratches = StaleScratches(Path.GetFullPath(outputDirectory), release);

        if (marker == release.Sha1)
        {
            EngineSpace.MakeRoom(engines, engine, 0, scratches, FreeBytes, output);

            return engine;
        }

        // Delete an install of another pin, because a file only that build has would stay in the new install.
        if (marker is not null)
        {
            EngineSpace.Delete(engine);
        }

        // Subtract what an interrupted run left, so a resume doesn't remove a finished engine for bytes that are already there.
        long missingBytes = Math.Max(release.InstallBytes - EngineSpace.SizeOf(engine), 0);

        EngineSpace.MakeRoom(engines, engine, missingBytes, scratches, FreeBytes, output);
        await RunScriptAsync(release, engines, cancellationToken).ConfigureAwait(false);
        EngineMarker.Write(engine, release.Sha1);

        return engine;
    }

    /// <summary>
    ///     Lists the scratch folders that builds of other versions left in <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="outputDirectory">The full path of the folder with the <c>engines</c> folder.</param>
    /// <param name="release">The release to install, whose own scratch the build deletes and recreates.</param>
    /// <returns>
    ///     The scratch folders of every other version.
    /// </returns>
    private static string[] StaleScratches(string outputDirectory, UnrealRelease release)
    {
        string own = Path.Combine(outputDirectory, $"{Unreal.ToolchainName}-{release.Version}{UnrealPaths.ScratchSuffix}");

        return
        [
            .. Directory.EnumerateDirectories(outputDirectory, $"{Unreal.ToolchainName}-*{UnrealPaths.ScratchSuffix}")
                .Where(scratch => !scratch.Equals(own, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    /// <summary>
    ///     Creates the uv arguments that run <paramref name="script"/> with the pinned legendary.
    /// </summary>
    /// <param name="script">The full path of the script.</param>
    /// <param name="arguments">The arguments of the script.</param>
    /// <returns>
    ///     The arguments, 1 per item and unquoted.
    /// </returns>
    public static string[] UvArguments(string script, params string[] arguments)
    {
        // Pass -I, so Python ignores the PYTHON* variables and the user site packages of the machine.
        return ["run", "--no-project", "--python", PythonVersion, "--with", LegendaryPackage, "python", "-I", script, .. arguments];
    }

    /// <summary>
    ///     Writes the embedded install script to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file to create.</param>
    /// <exception cref="FileNotFoundException">The assembly doesn't have the script as a resource.</exception>
    public static void WriteScript(string path)
    {
        using var script = typeof(UnrealEngine).Assembly.GetManifestResourceStream(ScriptResource)
            ?? throw new FileNotFoundException(SR.FormatResourceNotEmbedded(ScriptResource), ScriptResource);
        using var file = File.Create(path);

        script.CopyTo(file);
    }

    /// <summary>
    ///     Runs the install script from a new temp folder and deletes that folder afterward.
    /// </summary>
    /// <param name="release">The release to install.</param>
    /// <param name="engines">The full path of the <c>engines</c> folder.</param>
    /// <param name="cancellationToken">The token that stops the script and every process it started.</param>
    /// <returns>
    ///     A task that completes once the script exits with code 0.
    /// </returns>
    private async Task RunScriptAsync(UnrealRelease release, string engines, CancellationToken cancellationToken)
    {
        string folder = Directory.CreateTempSubdirectory("fixtures-engine-").FullName;

        try
        {
            string script = Path.Combine(folder, ScriptFileName);
            WriteScript(script);

            string manifest = EngineSpace.ManifestPath(Path.Combine(engines, release.App));
            string installTags = release.InstallTags ? "1" : "0";
            string[] arguments = UvArguments(script, release.App, release.Build, release.Sha1, engines, installTags, manifest);
            var info = ProcessRunner.NewStartInfo(UvPath, arguments);

            // Decode UTF-8, because the script sets its streams to it and uv prints it.
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;

            // Run from the temp root, because killed children keep their working directory open for a moment and the delete below would fail.
            info.WorkingDirectory = Path.GetTempPath();

            Queue<string> tail = new(ProcessRunner.TailLineCount);

            int exitCode = await ProcessRunner.RunWithLinesAsync(
                info,
                line =>
                {
                    output.WriteLine(line);
                    ProcessRunner.AddToTail(tail, line);
                },
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw NewFailure(release, exitCode, string.Concat(tail.Select(line => Environment.NewLine + line)));
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == FileNotFoundError)
        {
            throw new FileNotFoundException(UnrealSR.FormatUvNotFound(UvPath), UvPath, exception);
        }
        finally
        {
            TempPaths.DeleteFolderQuietly(folder);
        }
    }

    /// <summary>
    ///     Creates the exception of a script that exited with a code other than 0.
    /// </summary>
    /// <param name="release">The release that failed.</param>
    /// <param name="exitCode">The code the script exited with.</param>
    /// <param name="tail">The last lines of the script, each after a line break.</param>
    /// <returns>
    ///     The exception with the fix for that code.
    /// </returns>
    private static Exception NewFailure(UnrealRelease release, int exitCode, string tail)
    {
        return exitCode switch
        {
            ExitFilesBad => new InvalidDataException(UnrealSR.FormatEngineFilesMismatch(release.App, tail)),
            ExitListedAsInstalled => new InvalidOperationException(UnrealSR.FormatEngineManagedByLegendary(release.App)),
            ExitNotLoggedIn => new InvalidOperationException(UnrealSR.FormatNotLoggedIn(tail)),
            ExitNotPinned => new InvalidDataException(UnrealSR.FormatManifestNotPinned(release.App, UnrealRelease.ReleasesPath, tail)),
            _ => new InvalidOperationException(UnrealSR.FormatEngineInstallFailed(release.App, exitCode, tail)),
        };
    }
}
