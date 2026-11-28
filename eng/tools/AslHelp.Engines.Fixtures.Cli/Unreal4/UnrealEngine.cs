using AslHelp.Engines.Fixtures.Cli.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
    ///     Represents the cook scratch that a build writes next to the engine: 20 GiB.
    /// </summary>
    public const long CookBytes = 20L << 30;

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
    private const string InstallBytesPrefix = "install bytes: ";
    private const string LegendaryPackage = $"legendary-gl=={LegendaryVersion}";
    private const string MeasureSwitch = "--measure";
    private const string ScriptFileName = "install_engine.py";
    private const string ScriptResource = "Unreal4/install_engine.py";

    /// <summary>
    ///     Gets the path of uv. The default is <c>uv</c> from PATH.
    /// </summary>
    public string UvPath { get; init; } = "uv";

    /// <summary>
    ///     Installs <paramref name="release"/> unless its folder has a marker with the pinned sha1.
    /// </summary>
    /// <remarks>
    ///     The engine goes to <c>engines/&lt;app&gt;</c> under <paramref name="outputDirectory"/> and its manifest to
    ///     <c>engines/&lt;app&gt;.manifest</c>. A marker with the pinned sha1 doesn't start the script and only makes room for
    ///     <see cref="CookBytes"/>. A marker with another sha1 deletes the folder first. A folder without a marker resumes. Before
    ///     the download, the script measures the pinned manifest and <paramref name="room"/> makes room for the files that the
    ///     folder doesn't have yet and for <see cref="CookBytes"/>. The engine folder stays and the scratch folders of other
    ///     versions may go.
    /// </remarks>
    /// <param name="release">The release to install.</param>
    /// <param name="outputDirectory">The folder with the <c>engines</c> folder.</param>
    /// <param name="room">The room maker of the drive of <paramref name="outputDirectory"/>.</param>
    /// <param name="cancellationToken">The token that stops the install.</param>
    /// <returns>
    ///     The full path of the engine folder.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="release"/>, <paramref name="outputDirectory"/> or <paramref name="room"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FileNotFoundException">uv is not installed.</exception>
    /// <exception cref="InvalidDataException">
    ///     The manifest doesn't match its pin, the measure doesn't print the size or a file fails the hash check.
    /// </exception>
    /// <exception cref="IOException">The drive doesn't have room for the engine and the cook scratch.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Legendary isn't logged in, lists the app as installed or the script exits with another code.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string> InstallAsync(UnrealRelease release, string outputDirectory, DriveRoom room, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(room);

        // Check the token before any delete, because a canceled call must leave every engine in place.
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(outputDirectory);
        string engines = Path.Combine(root, DiskSpace.EnginesFolder);
        string engine = Path.Combine(engines, release.App);
        _ = Directory.CreateDirectory(engines);

        string? marker = EngineMarker.Read(engine);

        // Make room for the cook scratch of an installed engine too, so a build never starts on a full drive.
        if (marker == release.Sha1)
        {
            room.MakeFor(new InstallSpace(0, CookBytes, [engine], StaleScratches(root, release)));

            return engine;
        }

        // Delete an install of another pin, because a file only that build has would stay in the new install.
        if (marker is not null)
        {
            DiskSpace.Delete(engine);
        }

        long installBytes = await MeasureAsync(release, engines, cancellationToken).ConfigureAwait(false);
        room.MakeFor(new InstallSpace(Math.Max(installBytes - DiskSpace.SizeOf(engine), 0), CookBytes, [engine], StaleScratches(root, release)));

        string installTags = release.InstallTags ? "1" : "0";
        string[] arguments = [release.App, release.Build, release.Sha1, engines, installTags, ManifestOf(engines, release)];
        await RunScriptAsync(release, arguments, static _ => { }, cancellationToken).ConfigureAwait(false);
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
    ///     Gets the path of the manifest of the engine of <paramref name="release"/>.
    /// </summary>
    /// <param name="engines">The full path of the <c>engines</c> folder.</param>
    /// <param name="release">The release.</param>
    /// <returns>
    ///     The path of <c>engines/&lt;app&gt;.manifest</c>.
    /// </returns>
    private static string ManifestOf(string engines, UnrealRelease release)
    {
        return DiskSpace.ManifestPath(Path.Combine(engines, release.App));
    }

    /// <summary>
    ///     Runs the script with <c>--measure</c> and reads the bytes of the files that the pinned manifest installs.
    /// </summary>
    /// <param name="release">The release to measure.</param>
    /// <param name="engines">The full path of the <c>engines</c> folder.</param>
    /// <param name="cancellationToken">The token that stops the script.</param>
    /// <returns>
    ///     The bytes of the install.
    /// </returns>
    /// <exception cref="InvalidDataException">The manifest doesn't match its pin or the script doesn't print the size.</exception>
    private async Task<long> MeasureAsync(UnrealRelease release, string engines, CancellationToken cancellationToken)
    {
        string installTags = release.InstallTags ? "1" : "0";
        string[] arguments = [MeasureSwitch, release.App, release.Build, release.Sha1, installTags, ManifestOf(engines, release)];
        long? installBytes = null;

        await RunScriptAsync(
            release,
            arguments,
            line =>
            {
                if (line.StartsWith(InstallBytesPrefix, StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(InstallBytesPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long bytes))
                {
                    installBytes = bytes;
                }
            },
            cancellationToken).ConfigureAwait(false);

        return installBytes ?? throw new InvalidDataException(UnrealSR.FormatEngineSizeMissing(release.App));
    }

    /// <summary>
    ///     Runs the install script from a new temp folder and deletes that folder afterward.
    /// </summary>
    /// <param name="release">The release that the script installs or measures.</param>
    /// <param name="scriptArguments">The arguments of the script.</param>
    /// <param name="onLine">The receiver of each line, after the line goes to the output.</param>
    /// <param name="cancellationToken">The token that stops the script and every process it started.</param>
    /// <returns>
    ///     A task that completes once the script exits with code 0.
    /// </returns>
    private async Task RunScriptAsync(UnrealRelease release, string[] scriptArguments, Action<string> onLine, CancellationToken cancellationToken)
    {
        string folder = Directory.CreateTempSubdirectory("fixtures-engine-").FullName;

        try
        {
            string script = Path.Combine(folder, ScriptFileName);
            WriteScript(script);

            var info = ProcessRunner.NewStartInfo(UvPath, UvArguments(script, scriptArguments));

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
                    onLine(line);
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
