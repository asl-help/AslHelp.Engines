using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Installs a UDK release, the free Unreal Engine 3 builds from 2009-11 to 2015-02, from the <c>udkinstaller</c> item on archive.org
///     and compiles the fixture package in it.
/// </summary>
/// <remarks>
///     Each version is pinned to the sha1 of its installer in <c>installers.json</c>, so every machine installs the
///     same bytes and the archive.org listing is never read. The installer is a zip after an UnSetup stub.
///     The zip is extracted directly, so UnSetup never runs and nothing is written to the registry.
/// </remarks>
/// <param name="downloader">The downloader for the installers.</param>
/// <param name="host">The host that the compiler and the server run in.</param>
internal sealed class Udk(
    Downloader downloader,
    IPlayerHost host) : IToolchain
{
    /// <summary>
    ///     Numbers the revision of the build steps that no other key input shows. Bump it when a step changes that way, so
    ///     every player is built again. Bump the value in <c>AddKeyInputs_PinnedVersion_AddsEveryBuildInput</c> too.
    /// </summary>
    private const int BuildVersion = 2;

    /// <inheritdoc/>
    public string Name => "unreal3";

    /// <summary>
    ///     Gets how long <see cref="CheckAsync"/> waits for the ready line once the server runs. The default is 2 minutes.
    /// </summary>
    public TimeSpan CheckTimeout { get; init; } = UdkServer.DefaultCheckTimeout;

    /// <inheritdoc/>
    /// <remarks>
    ///     The inputs are the version as in <see cref="UdkInstallers.All"/>, the installer sha1, the make and server command
    ///     lines, the line edit of <c>UDKGame/Config/DefaultEngineUDK.ini</c>, the delete of
    ///     <c>UDKGame/Config/UDKEngine.ini</c> and the build version. The runtime DLLs are no input, because the key is
    ///     built before the install exists. The installer sha1 pins the import tables they are read from and the redist
    ///     they come from. <c>BuildVersion</c> pins the code that reads them.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="UdkInstallers.All"/> has no installer of <paramref name="version"/>.</exception>
    public string AddKeyInputs(CacheKeyBuilder builder, string version)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Look the version up first, so an unpinned version adds nothing and every spelling of a version shares 1 key.
        var installer = UdkInstallers.Find(version);

        _ = builder
            .Add("version", installer.Version)
            .Add("installer-sha1", installer.Sha1)
            .Add("make-command", UdkProject.MakeCommand)
            .Add("server-command", UdkServer.Command)
            .Add("config-edit", UdkProject.ConfigEdit)
            .Add("config-delete", UdkProject.EngineConfigPath)
            .Add("build-version", BuildVersion.ToString(CultureInfo.InvariantCulture));

        return installer.Version;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The install goes to <c>udk-&lt;version&gt;</c> under <paramref name="outputDirectory"/> and the installer to
    ///     <c>downloads</c> next to it. The folder spells the version as in <see cref="UdkInstallers.All"/>. An existing
    ///     install folder sends no request, so a cached install works offline. A missing one sends 1 request, the pinned
    ///     download. An install whose redist has the Visual C++ 2008 installer, UDK 2009-11 to 2011-07, is refused once it
    ///     is extracted. An install without a redist passes. The runtime step reports a missing redist if it needs one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="UdkInstallers.All"/> has no installer of <paramref name="version"/> or the install needs the Visual C++ 2008 runtime.
    /// </exception>
    public async Task<string> InstallAsync(
        string version,
        string outputDirectory,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        // Look the version up first, so an unpinned version touches neither the network nor the disk
        // and every spelling of a version shares 1 install folder.
        var installer = UdkInstallers.Find(version);
        string output = Path.GetFullPath(outputDirectory);
        string installDirectory = Path.Combine(output, $"udk-{installer.Version}");

        // Check the install before the download, so a cached install needs no network
        // and no hash of an installer of up to 2 GB.
        if (Directory.Exists(installDirectory))
        {
            VisualCppRuntimes.ThrowIfVc2008(installDirectory, installer.Version);

            return installDirectory;
        }

        var file = installer.File;
        string installerPath = Path.Combine(output, "downloads", file.Name);

        await downloader.DownloadAsync(file.Url, installerPath, file.Hash, progress, cancellationToken).ConfigureAwait(false);
        // Extract the zip after the UnSetup stub directly, so UnSetup never runs and nothing goes into the registry.
        SelfExtractingZip.Extract(installerPath, installDirectory);

        // Check right after the extraction, so a version this tool cannot build fails before the build starts.
        VisualCppRuntimes.ThrowIfVc2008(installDirectory, installer.Version);

        return installDirectory;
    }

    /// <summary>
    ///     Adds the runtimes with <see cref="UdkRuntimes.AddAsync"/> and the fixture package with
    ///     <see cref="UdkProject.AddFixturePackage"/> and compiles it with <see cref="UdkProject.MakeAsync"/>.
    /// </summary>
    /// <remarks>
    ///     A missing <c>UDK.com</c> throws before anything in the install changes.
    /// </remarks>
    /// <param name="installDirectory">The extracted install from <see cref="InstallAsync"/>.</param>
    /// <param name="projectDirectory">The player project, with the <c>.uc</c> files of the package in <c>Classes</c>, outside the install.</param>
    /// <param name="cancellationToken">The token that stops the compile.</param>
    /// <returns>
    ///     A task that completes once <c>UDKGame/Script/Fixture.u</c> is written.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="installDirectory"/> or <paramref name="projectDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="projectDirectory"/> has no <c>Classes</c> folder.</exception>
    /// <exception cref="FileNotFoundException">
    ///     The install has no config file, no <c>UDK.com</c>, no <c>UDK.exe</c> or no redist for a missing DLL.
    /// </exception>
    /// <exception cref="IOException">A copy or the write fails.</exception>
    /// <exception cref="InvalidDataException">
    ///     The config file has no <c>ModEditPackages</c> line to change, a file that <c>UDK.exe</c> or a DLL it loads
    ///     imports is not a PE file or the redist does not give every DLL.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     A DLL imports a Visual C++ runtime other than 2010, no x86 cabinet of the
    ///     redist lists a missing DirectX DLL, the host cannot run anything or a runtime tool or <c>UDK.com</c> exits with a
    ///     code other than 0. <c>UDK.com</c> exits with 0 but does not write <c>UDKGame/Script/Fixture.u</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The compile runs longer than <see cref="UdkProject.DefaultMakeTimeout"/>.</exception>
    public async Task BuildAsync(string installDirectory, string projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        // Check the compiler before any write, so an install without it keeps its earlier package.
        UdkProject.ThrowIfNoCompiler(installDirectory);

        await UdkRuntimes.AddAsync(installDirectory, cancellationToken).ConfigureAwait(false);
        UdkProject.AddFixturePackage(installDirectory, Path.Combine(projectDirectory, UdkProject.ProjectClassesPath));
        await UdkProject.MakeAsync(host, installDirectory, UdkProject.DefaultMakeTimeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <see cref="UdkServer.CheckAsync"/> runs the server and waits up to <see cref="CheckTimeout"/> for the ready line.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The install has no <c>UDK.exe</c> or no map.</exception>
    /// <exception cref="InvalidOperationException">The host cannot run anything or the server exits before the check finds the ready line.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The ready line is not in the log within <see cref="CheckTimeout"/>.</exception>
    public Task CheckAsync(string installDirectory, CancellationToken cancellationToken)
    {
        return UdkServer.CheckAsync(host, installDirectory, CheckTimeout, cancellationToken);
    }
}
