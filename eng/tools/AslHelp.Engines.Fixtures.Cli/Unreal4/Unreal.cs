using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Transfer;
using AslHelp.Engines.Fixtures.Unreal4;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Installs an Unreal Engine 4 or 5 release from Epic and builds the players of the fixture project in it, 1 per
///     architecture and configuration that its row lists.
/// </summary>
/// <remarks>
///     Each version is pinned to the sha1 of its manifest in <c>releases.json</c>. The build tools run only in containers of the
///     build image, which a person builds from <c>eng/fixtures/unreal4/image</c>. Each step is a script of
///     <c>eng/fixtures/unreal4/build</c>. What differs between versions is a field of the row.
/// </remarks>
/// <param name="engine">The installer of the engines.</param>
/// <param name="host">The host that the players run in for the check.</param>
/// <param name="buildHost">The host that the build steps run in.</param>
/// <param name="buildDirectory">The folder with the copied image folder in <c>image</c> and the step scripts in <c>steps</c>.</param>
internal sealed class Unreal(
    UnrealEngine engine,
    IPlayerHost host,
    IBuildHost buildHost,
    string buildDirectory) : IToolchain
{
    /// <summary>
    ///     Represents the toolchain name. The folder with the image and the steps under <c>build</c> has the same name.
    /// </summary>
    public const string ToolchainName = "unreal4";

    /// <summary>
    ///     Numbers the revision of the build steps that the other key inputs don't show. Bump it when a step changes that way, so
    ///     every player is built again. Bump the value in <c>AddKeyInputs_PinnedPlayer_AddsEveryBuildInput</c> too.
    /// </summary>
    private const int BuildVersion = 1;

    private const string ImageFolder = "image";

    // Matches the folder that runtimes.bat copies the DLLs of the image into, under the out mount.
    private const string RuntimesFolder = "runtimes";

    private const string StepsFolder = "steps";

    private readonly Option<Architecture?> _archOption = UnrealPlayerOptions.Arch(UnrealSR.ArchDescription);

    private readonly Option<UnrealConfig?> _configOption = UnrealPlayerOptions.Config(UnrealSR.ConfigDescription);

    /// <inheritdoc/>
    public string Name => ToolchainName;

    /// <inheritdoc/>
    public IReadOnlyList<Option> Options => [_archOption, _configOption];

    /// <summary>
    ///     Gets how long <see cref="CheckAsync"/> waits for the ready file once the player runs. The default is 3 minutes.
    /// </summary>
    public TimeSpan CheckTimeout { get; init; } = UnrealGame.DefaultCheckTimeout;

    /// <summary>
    ///     Gets how long each build step may run once it runs. The default is 90 minutes.
    /// </summary>
    public TimeSpan StepTimeout { get; init; } = UnrealBuild.DefaultStepTimeout;

    /// <inheritdoc/>
    /// <remarks>
    ///     The ids are <c>&lt;version&gt;-&lt;tag&gt;</c> with the version as in <see cref="EmbeddedReleases{UnrealRelease}.All"/>,
    ///     such as <c>4.19.2-win-x64-shipping</c>, in the order of the row. <c>--arch</c> and <c>--config</c> leave out the
    ///     players that don't fit.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="EmbeddedReleases{UnrealRelease}.All"/> doesn't have a row of <paramref name="version"/>.
    /// </exception>
    public IReadOnlyList<string> ListPlayers(string version, ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        var release = EmbeddedReleases<UnrealRelease>.Find(version);
        var arch = parseResult.GetValue(_archOption);
        var config = parseResult.GetValue(_configOption);
        List<string> players = [];

        foreach (var player in release.Players)
        {
            if ((arch is null || player.Arch == arch) && (config is null || player.Config == config))
            {
                players.Add(player.IdOf(release.Version));
            }
        }

        return players;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The inputs are the player id as <see cref="ListPlayers"/> returns it, the manifest sha1, the tag of the build image,
    ///     the project as <see cref="UnrealFixtureSource.RenderFiles"/> renders it for the version, the step scripts, the
    ///     switches and the PATH folder of the row, the source of the runtime DLLs and the build version. A change to the
    ///     project that the version drops leaves its key alone. The runtime DLLs themselves aren't an input, because the key
    ///     is built before the install exists. The manifest sha1 and the image tag pin the files they come from.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">
    ///     The build folder doesn't have the image folder or the step scripts or <paramref name="projectDirectory"/> does not
    ///     exist.
    /// </exception>
    /// <exception cref="FormatException">The tag part of <paramref name="player"/> isn't a player tag.</exception>
    /// <exception cref="InvalidDataException">A directive line of the project is bad. Nothing is added.</exception>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="EmbeddedReleases{UnrealRelease}.All"/> doesn't have a row of the version part of <paramref name="player"/>.
    /// </exception>
    public string AddKeyInputs(CacheKeyBuilder builder, string player, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Look the player up first, so an unpinned version adds nothing and every spelling of a version shares 1 key.
        var (release, parsed) = ParseId(player);
        string id = parsed.IdOf(release.Version);
        string image = UnrealImage.Tag(Path.Combine(buildDirectory, ImageFolder));
        var project = UnrealFixtureSource.RenderFiles(projectDirectory, Version.Parse(release.Version));

        _ = builder
            .Add("version", id)
            .Add("manifest-sha1", release.Sha1)
            .Add("image", image)
            .AddFiles("project", project)
            .AddDirectory("steps", Path.Combine(buildDirectory, StepsFolder))
            .Add("editor-flags", string.Join(' ', release.EditorFlags))
            .Add("package-flags", string.Join(' ', release.PackageFlags))
            .Add("path-prepend", release.PathPrepend ?? "")
            .Add("runtimes", release.Runtimes == UnrealRuntimeSource.Image ? "image" : "engine")
            .Add("build-version", BuildVersion.ToString(CultureInfo.InvariantCulture));

        return id;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <see cref="UnrealEngine.InstallAsync"/> installs the engine into <c>engines/&lt;app&gt;</c> and makes room for the engine
    ///     and the cook scratch first. The install script prints its own progress, so <paramref name="progress"/> doesn't get a report.
    /// </remarks>
    /// <exception cref="FileNotFoundException">uv is not installed.</exception>
    /// <exception cref="FormatException">The tag part of <paramref name="player"/> isn't a player tag.</exception>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="EmbeddedReleases{UnrealRelease}.All"/> doesn't have a row of the version part of <paramref name="player"/>,
    ///     legendary isn't logged in or the install fails.
    /// </exception>
    public Task<string> InstallAsync(
        string player,
        string outputDirectory,
        DriveRoom room,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var (release, _) = ParseId(player);

        return engine.InstallAsync(release, outputDirectory, room, cancellationToken);
    }

    /// <summary>
    ///     Renders the project for the version into <c>unreal4-&lt;version&gt;-build</c> next to the <c>engines</c> folder and
    ///     builds every player there with the step scripts in containers of the build image.
    /// </summary>
    /// <remarks>
    ///     The build image must be on the machine, which is checked before anything is written. The step containers of an earlier
    ///     build of the version are removed and its scratch folder is deleted. <see cref="IBuildHost.GrantCurrentUser"/> runs on
    ///     the scratch and the engine before the 1st container, because the files a container writes belong to its
    ///     administrator. Rows of engines without <c>AppLocalDependencies</c> copy the runtime DLLs out of the image 1st. Then
    ///     the editor target builds, the 1st player cooks and every later player reuses the cooked content. Each archive moves
    ///     to <c>players/&lt;tag&gt;</c> and gets the runtime DLLs its executable imports, <c>fixture.json</c> and the logs of
    ///     its steps in <c>logs</c>. Once every player is there, the project and the archives are deleted. A failed step keeps
    ///     them.
    /// </remarks>
    /// <param name="installDirectory">The engine from <see cref="InstallAsync"/>.</param>
    /// <param name="projectDirectory">The gated project, such as the copy of <c>eng/fixtures/unreal4</c> under <c>projects</c>.</param>
    /// <param name="players">The player ids of 1 version from <see cref="ListPlayers"/>.</param>
    /// <param name="cancellationToken">The token that stops the build and removes the running step container.</param>
    /// <returns>
    ///     The folder <c>players/&lt;tag&gt;</c> of each player, in the order of <paramref name="players"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="installDirectory"/>, <paramref name="projectDirectory"/> or <paramref name="players"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    ///     The build folder doesn't have the image folder or <paramref name="projectDirectory"/> does not exist.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    ///     An archive doesn't have an executable or a player imports a DLL that neither the runtimes nor Windows has.
    /// </exception>
    /// <exception cref="FormatException">The tag part of a player isn't a player tag.</exception>
    /// <exception cref="InvalidDataException">
    ///     The project has a bad directive, a file an executable loads is not a PE file or the runtimes have 2 different files
    ///     of 1 DLL.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="EmbeddedReleases{UnrealRelease}.All"/> doesn't have a row of the version part of a player, the build image
    ///     isn't on the machine, the host cannot run a step, a step exits with a code other than 0 or a package step doesn't
    ///     archive the player.
    /// </exception>
    /// <exception cref="IOException">A delete, a copy or a move fails.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">A step runs longer than <see cref="StepTimeout"/>.</exception>
    public async Task<IReadOnlyList<string>> BuildAsync(
        string installDirectory,
        string projectDirectory,
        IReadOnlyList<string> players,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentNullException.ThrowIfNull(players);

        var release = ParseId(players[0]).Release;
        var parsed = new UnrealPlayer[players.Count];

        for (int i = 0; i < parsed.Length; i++)
        {
            parsed[i] = ParseId(players[i]).Player;
        }

        // Check the image before any write, so a missing image keeps the scratch of the last build.
        string imageDirectory = Path.Combine(buildDirectory, ImageFolder);
        string image = UnrealImage.Tag(imageDirectory);
        await UnrealImage.ThrowIfMissingAsync(buildHost, image, imageDirectory, cancellationToken).ConfigureAwait(false);

        // Put the scratch next to the engines folder, because UnrealEngine puts the engine at <output>/engines/<app>.
        string enginePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        string outputDirectory = Path.GetDirectoryName(Path.GetDirectoryName(enginePath))!;
        string scratch = Path.Combine(outputDirectory, $"{Name}-{release.Version}{UnrealPaths.ScratchSuffix}");
        UnrealPaths paths = new(enginePath, scratch, Path.Combine(buildDirectory, StepsFolder));

        // Remove the step containers of a killed run first, because a running one keeps the scratch open.
        await buildHost.RemoveStepsAsync(paths.Scratch, cancellationToken).ConfigureAwait(false);

        if (Directory.Exists(paths.Scratch))
        {
            FolderTree.Delete(paths.Scratch);
        }

        _ = Directory.CreateDirectory(paths.Out);
        _ = Directory.CreateDirectory(paths.Logs);
        _ = Directory.CreateDirectory(paths.Players);

        // Grant before the 1st container, so this user can delete what the containers write, also in the next build.
        buildHost.GrantCurrentUser(paths.Scratch);
        buildHost.GrantCurrentUser(paths.Engine);

        UnrealFixtureSource.Render(projectDirectory, paths.Project, Version.Parse(release.Version));

        if (release.Runtimes == UnrealRuntimeSource.Image)
        {
            var runtimes = UnrealBuild.RuntimesStep(image, paths);
            await UnrealBuild.RunAsync(buildHost, runtimes, new UnrealBuildStep.Runtimes(), paths, StepTimeout, cancellationToken)
                .ConfigureAwait(false);
        }

        var editor = UnrealBuild.EditorStep(release, image, paths);
        await UnrealBuild.RunAsync(buildHost, editor, new UnrealBuildStep.Editor(), paths, StepTimeout, cancellationToken)
            .ConfigureAwait(false);

        for (int i = 0; i < parsed.Length; i++)
        {
            // Cook with the 1st player only, because the cooked content is the same for every arch and config.
            var cook = i == 0 ? UnrealCookMode.Cook : UnrealCookMode.SkipCook;
            var package = UnrealBuild.PackageStep(release, image, paths, parsed[i], cook);
            await UnrealBuild.RunAsync(buildHost, package, new UnrealBuildStep.Package(parsed[i]), paths, StepTimeout, cancellationToken)
                .ConfigureAwait(false);
        }

        string[] folders = new string[parsed.Length];

        for (int i = 0; i < parsed.Length; i++)
        {
            folders[i] = AddPlayer(release, paths, parsed[i]);
        }

        // Delete the project and the archives, because they take GBs per version and the players have their logs.
        FolderTree.Delete(paths.Project);
        FolderTree.Delete(paths.Out);

        return folders;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The player and the engine version come from <c>fixture.json</c> in the folder, which <see cref="BuildAsync"/> writes.
    ///     <see cref="UnrealGame.CheckAsync"/> runs the executable and waits up to <see cref="CheckTimeout"/> for the ready file.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The folder doesn't have <c>fixture.json</c> or the executable.</exception>
    /// <exception cref="FormatException">The player of <c>fixture.json</c> isn't a player tag or its version isn't a version.</exception>
    /// <exception cref="InvalidDataException"><c>fixture.json</c> is the JSON <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The host cannot run anything or the player exits during the check.</exception>
    /// <exception cref="JsonException"><c>fixture.json</c> isn't the JSON of a manifest.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The player doesn't write the ready file within <see cref="CheckTimeout"/>.</exception>
    public async Task CheckAsync(string playerDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playerDirectory);

        var manifest = UnrealFixtureManifest.Read(playerDirectory);
        var player = UnrealPlayer.Parse(manifest.Player);
        var version = Version.Parse(manifest.EngineVersion);

        await UnrealGame.CheckAsync(host, playerDirectory, player, version, CheckTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Splits a player id into the row of its version and its player.
    /// </summary>
    /// <param name="id">The player id, such as <c>4.19.2-win-x64-shipping</c>.</param>
    /// <returns>
    ///     The row and the player.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The part after the version isn't a player tag.</exception>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="EmbeddedReleases{UnrealRelease}.All"/> doesn't have a row of the version.
    /// </exception>
    private static (UnrealRelease Release, UnrealPlayer Player) ParseId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        // Split at the 1st dash, because a version doesn't have one and a tag has 2.
        int dash = id.IndexOf('-', StringComparison.Ordinal);
        var release = EmbeddedReleases<UnrealRelease>.Find(dash < 0 ? id : id[..dash]);

        return (release, UnrealPlayer.Parse(id[(dash + 1)..]));
    }

    /// <summary>
    ///     Moves the archive of <paramref name="player"/> to its player folder and adds the runtime DLLs, the manifest and the logs.
    /// </summary>
    /// <param name="release">The row of the engine.</param>
    /// <param name="paths">The folders of the build.</param>
    /// <param name="player">The player.</param>
    /// <returns>
    ///     The player folder.
    /// </returns>
    private static string AddPlayer(UnrealRelease release, UnrealPaths paths, UnrealPlayer player)
    {
        // Take the 1 folder the package step archived into, because 4.x calls it WindowsNoEditor and 5.x Windows.
        string archives = Path.Combine(paths.Out, player.Tag);
        UnrealBuildStep.Package package = new(player);

        if (!Directory.Exists(archives) || Directory.GetDirectories(archives) is not [var archive])
        {
            throw new InvalidOperationException(UnrealSR.FormatArchiveNotFound(player.Tag, archives, paths.LogPath(package)));
        }

        string playerDirectory = Path.Combine(paths.Players, player.Tag);
        Directory.Move(archive, playerDirectory);

        string executablePath = player.ExecutablePath(playerDirectory, Version.Parse(release.Version));
        string pool = release.Runtimes == UnrealRuntimeSource.Image
            ? Path.Combine(paths.Out, RuntimesFolder, UnrealPlayer.ArchName(player.Arch))
            : Path.Combine(paths.Engine, "Engine", "Binaries", "ThirdParty", "AppLocalDependencies", player.Platform);

        // Look Windows DLLs up in the host's system folder of the player's arch, because the base image ships the same ones.
        // The pool comes 1st, so a runtime that only the host has installed still gets copied.
        string system = player.Arch == Architecture.X64
            ? Environment.SystemDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

        UnrealRuntimes.Add(executablePath, pool, system);
        UnrealFixtureManifest.FromProject(paths.Project, release.Version, player.Tag, player.Config).Write(playerDirectory);

        string logs = Directory.CreateDirectory(Path.Combine(playerDirectory, "logs")).FullName;

        foreach (var buildStep in (UnrealBuildStep[])[new UnrealBuildStep.Editor(), package])
        {
            File.Copy(paths.LogPath(buildStep), Path.Combine(logs, $"{buildStep.Name}.log"));
        }

        return playerDirectory;
    }
}
