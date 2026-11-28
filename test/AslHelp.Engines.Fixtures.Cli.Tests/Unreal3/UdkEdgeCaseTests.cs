using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Transfer;
using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeHttpHandler;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkEdgeCaseTests
{
    private const string PinnedVersion = "2011-03-BETA2";
    private const string TypedVersion = "2011-03-beta2";

    private Downloader _downloader = null!;
    private TestFolder _folder = null!;
    private string _output = null!;
    private string _projects = null!;

    [SetUp]
    public void SetUp()
    {
        _downloader = new Downloader(new FakeHttpHandler(_ => Status(HttpStatusCode.NotFound)));
        _folder = new TestFolder();
        _output = _folder.NewFolder();
        _projects = _folder.NewFolder();

        WriteBytes(_projects, "unreal3/Classes/FixtureGame.uc", [1, 2, 3]);
    }

    [TearDown]
    public void TearDown()
    {
        _downloader.Dispose();
        _folder.Delete();
    }

    // Every pinned version lists 1 player in any casing. AddKeyInputs returns that id for it, so the later steps get the pinned spelling.
    [Test]
    public void ListPlayers_EveryPinnedVersion_ListsTheValueThatAddKeyInputsReturns()
    {
        Udk udk = new(_downloader, new FakeUdkHost());
        var parseResult = new RootCommand().Parse([]);

        using (Assert.EnterMultipleScope())
        {
            foreach (var release in EmbeddedReleases<UdkRelease>.All)
            {
                var players = udk.ListPlayers(release.Version.Replace("BETA", "beta", StringComparison.Ordinal), parseResult);

                Assert.That(players, Is.EqualTo([release.Version]));
                Assert.That(udk.AddKeyInputs(new CacheKeyBuilder("player"), players[0], _projects), Is.EqualTo(release.Version));
            }
        }
    }

    // BuildAsync returns the install as the only player folder, so StoreAsync moves the install.
    [Test]
    public async Task BuildAsync_CompilerWritesThePackage_ReturnsTheInstallAsTheOnlyFolder()
    {
        string install = WriteBuildableInstall();
        FakeUdkHost host = new();
        Udk udk = new(_downloader, host);

        var folders = await udk.BuildAsync(install, Path.Combine(_projects, "unreal3"), [PinnedVersion], CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(folders, Is.EqualTo([install]));
            Assert.That(host.Starts, Is.EqualTo(["UDK.com"]));
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Script", "Fixture.u")), Is.True);
        }
    }

    // A full build moves the install to the player folder and stores it, both under the pinned spelling.
    [Test]
    public async Task RunAsync_BuildCasingVariant_MovesTheInstallToThePlayerFolderAndTheStorePath()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        using FakeCacheStore store = new();
        string install = WriteBuildableInstall();
        FakeUdkHost host = new();
        Udk udk = new(_downloader, host);
        var key = PlayerCache.KeyOf(udk, PinnedVersion, Path.Combine(_projects, "unreal3")).Key;
        string player = Path.Combine(_output, $"unreal3-{PinnedVersion}-{key.ShortHash}");
        string remote = $"players/unreal3/{PinnedVersion}/{key.ShortHash}";

        int code = await RunBuildAsync(output, error, udk, store, TypedVersion);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(host.Starts, Is.EqualTo(["UDK.com", "UDK.exe"]));
            Assert.That(Directory.Exists(install), Is.False);
            Assert.That(File.Exists(Path.Combine(player, "UDKGame", "Script", "Fixture.u")), Is.True);
            Assert.That(File.Exists(Path.Combine(player, "UDKGame", "Logs", "Launch.log")), Is.False);
            Assert.That(ManifestVersion(player), Is.EqualTo(PinnedVersion));
            Assert.That(store.Files.Keys, Is.EquivalentTo([$"{remote}.tar.zst", $"{remote}.json"]));
            Assert.That(output.ToString().TrimEnd().Split(Environment.NewLine)[^1], Is.EqualTo(player));
        }
    }

    // A 2nd run finds the player folder from the 1st, so it doesn't install or compile again.
    [Test]
    public async Task RunAsync_BuildTwice_StartsNothingInThe2ndRun()
    {
        using StringWriter firstOutput = new();
        using StringWriter secondOutput = new();
        using StringWriter error = new();
        _ = WriteBuildableInstall();
        FakeUdkHost host = new();
        Udk udk = new(_downloader, host);
        _ = await RunBuildAsync(firstOutput, error, udk, null, PinnedVersion);
        host.Starts.Clear();

        int code = await RunBuildAsync(secondOutput, error, udk, null, TypedVersion);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(host.Starts, Is.Empty);
            Assert.That(
                secondOutput.ToString().TrimEnd().Split(Environment.NewLine)[^1],
                Is.EqualTo(firstOutput.ToString().TrimEnd().Split(Environment.NewLine)[^1]));
        }
    }

    // A server that exits during the check leaves the install where it is without a manifest, so a later run can't take it for a player.
    [Test]
    public async Task RunAsync_BuildServerExitsDuringCheck_KeepsTheInstallAndStoresNothing()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        using FakeCacheStore store = new();
        string install = WriteBuildableInstall();
        Udk udk = new(_downloader, new FakeUdkHost { ServerExits = true });

        int code = await RunBuildAsync(output, error, udk, store, PinnedVersion);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("code 3"));
            Assert.That(Directory.Exists(install), Is.True);
            Assert.That(File.Exists(Path.Combine(install, "manifest.json")), Is.False);
            Assert.That(Directory.GetDirectories(_output, "unreal3-*"), Is.Empty);
            Assert.That(store.Files, Is.Empty);
        }
    }

    // Unreal3 doesn't add an option, so the parser refuses an option of another toolchain before the output folder exists.
    [Test]
    public async Task RunAsync_BuildWithOptionOfAnotherToolchain_FailsBeforeTheOutputFolder()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        string folder = Path.Combine(_folder.Root, "new");
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "unreal3", "2015-02", "--output", folder, "--arch", "x64"],
            output,
            error,
            [new Udk(_downloader, new FakeUdkHost())],
            _ =>
            {
                opened = true;
                return null;
            },
            _projects,
            _ => long.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("--arch"));
            Assert.That(opened, Is.False);
            Assert.That(Directory.Exists(folder), Is.False);
        }
    }

    // An empty or blank version isn't pinned, so it fails with the list of pinned versions and never becomes a path.
    [TestCase("")]
    [TestCase(" ")]
    public async Task RunAsync_BuildBlankVersion_FailsBeforeTheStore(string version)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "unreal3", version, "--output", _output],
            output,
            error,
            [new Udk(_downloader, new FakeUdkHost())],
            _ =>
            {
                opened = true;
                return null;
            },
            _projects,
            _ => long.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("2015-02"));
            Assert.That(opened, Is.False);
            Assert.That(Directory.GetFileSystemEntries(_output), Is.Empty);
        }
    }

    // The command line of the real tool has a subcommand for unreal3 with the arguments that the readme lists.
    [Test]
    public async Task RunAsync_RealToolchainsHelp_ListsUnreal3AndItsArguments()
    {
        using StringWriter buildOutput = new();
        using StringWriter toolchainOutput = new();
        using StringWriter error = new();

        int buildCode = await CommandLine.RunAsync(["build", "--help"], buildOutput, error);
        int toolchainCode = await CommandLine.RunAsync(["build", "unreal3", "--help"], toolchainOutput, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buildCode, Is.Zero);
            Assert.That(buildOutput.ToString(), Does.Contain("unreal3"));
            Assert.That(toolchainCode, Is.Zero);
            Assert.That(toolchainOutput.ToString(), Does.Contain("<version>").And.Contain("--output"));
        }
    }

    private Task<int> RunBuildAsync(StringWriter output, StringWriter error, Udk udk, FakeCacheStore? store, string version)
    {
        string[] arguments = ["build", udk.Name, version, "--output", _output];

        return CommandLine.RunAsync(arguments, output, error, [udk], _ => store, _projects, _ => long.MaxValue);
    }

    // Write the files every build step reads at the install folder that InstallAsync finds, so nothing gets downloaded.
    private string WriteBuildableInstall()
    {
        string install = Path.Combine(_output, $"udk-{PinnedVersion}");
        _ = Directory.CreateDirectory(Binaries(install));
        WriteBytes(install, "UDKGame/Config/DefaultEngineUDK.ini", Encoding.ASCII.GetBytes(DefaultIni));
        WriteBytes(install, "UDKGame/Content/Maps/Examples/ExampleMap.udk", [1]);
        WriteBytes(install, "Binaries/Win32/UDK.com", [1]);
        WriteServer(install);

        return install;
    }

    private static string? ManifestVersion(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "manifest.json")));

        return document.RootElement.GetProperty("version").GetString();
    }
}

/// <summary>
///     Writes what the compiler and the server would write and doesn't run anything.
/// </summary>
file sealed class FakeUdkHost : IPlayerHost
{
    /// <summary>
    ///     Gets a value indicating whether the server exits with code 3 right after it starts.
    /// </summary>
    public bool ServerExits { get; init; }

    /// <summary>
    ///     Gets the file of each started executable in order, such as <c>UDK.com</c>.
    /// </summary>
    public List<string> Starts { get; } = [];

    /// <inheritdoc/>
    public Task<IHostedRun> StartAsync(string installDirectory, string executablePath, string arguments, CancellationToken cancellationToken)
    {
        string executable = Path.GetFileName(executablePath);
        Starts.Add(executable);

        if (executable == "UDK.com")
        {
            WriteBytes(installDirectory, "UDKGame/Script/Fixture.u", [1]);

            return Task.FromResult<IHostedRun>(new FixedRun(HasExited: true, ExitCode: 0));
        }

        if (ServerExits)
        {
            return Task.FromResult<IHostedRun>(new FixedRun(HasExited: true, ExitCode: 3));
        }

        WriteBytes(installDirectory, "UDKGame/Logs/Launch.log", Encoding.ASCII.GetBytes("[0001.00] ScriptLog: Fixture ready\r\n"));

        return Task.FromResult<IHostedRun>(new FixedRun(HasExited: false, ExitCode: 0));
    }
}

/// <summary>
///     Represents a run with a fixed state.
/// </summary>
/// <param name="HasExited">The value of <see cref="IHostedRun.HasExited"/>.</param>
/// <param name="ExitCode">The value of <see cref="IHostedRun.ExitCode"/>.</param>
file sealed record FixedRun(
    bool HasExited,
    int ExitCode) : IHostedRun
{
    /// <inheritdoc/>
    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
