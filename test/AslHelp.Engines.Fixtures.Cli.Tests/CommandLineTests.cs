using AslHelp.Engines.Fixtures.Cli.Formats;
using AslHelp.Engines.Fixtures.Cli.Transfer;
using NUnit.Framework;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class CommandLineTests
{
    private TestFolder _folder = null!;
    private string _output = null!;
    private string _projects = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _projects = _folder.NewFolder();
        _output = _folder.NewFolder();

        TestFolder.WriteBytes(_projects, "player/file.bin", [4, 5, 6]);
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // The help of the build command opens no store, because it runs no build.
    [Test]
    public async Task RunAsync_BuildHelp_OpensNoStore()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        bool opened = false;

        int code = await CommandLine.RunAsync(["build", "--help"], output, error, [new FakeToolchain("player")], _ =>
        {
            opened = true;
            return null;
        }, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(opened, Is.False);
        }
    }

    // The help of a toolchain's subcommand lists its own options with --output and <version>, leaves out the options of another toolchain and
    // doesn't run a step.
    [Test]
    public async Task RunAsync_BuildToolchainHelp_ListsOnlyItsOwnOptions()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeToolchain other = new("other", "--mode");
        bool opened = false;

        int code = await CommandLine.RunAsync(["build", "player", "--help"], output, error, [other, toolchain], _ =>
        {
            opened = true;
            return null;
        }, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(
                output.ToString(),
                Does.Contain("--variant").And.Contain("The variant to build.").And.Contain("--output").And.Contain("<version>"));
            Assert.That(output.ToString(), Does.Not.Contain("--mode"));
            Assert.That(opened, Is.False);
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // A build without a toolchain prints the help of build and fails, so a script with a missing argument stops.
    [Test]
    public async Task RunAsync_BuildWithoutToolchain_PrintsHelpAndFails()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        bool opened = false;

        int code = await CommandLine.RunAsync(["build"], output, error, [new FakeToolchain("first"), new FakeToolchain("second")], _ =>
        {
            opened = true;
            return null;
        }, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("Builds players with first.").And.Contain("Builds players with second."));
            Assert.That(opened, Is.False);
        }
    }

    // An unknown toolchain fails before any step. The error has the typo and the help of build lists every toolchain, so the user can pick 1.
    [Test]
    public async Task RunAsync_BuildUnknownToolchain_FailsListingTheToolchains()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain first = new("first");
        FakeToolchain second = new("second");
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "third", "1.0", "--output", _output],
            output,
            error,
            [first, second],
            _ =>
            {
                opened = true;
                return null;
            },
            _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("third"));
            Assert.That(output.ToString(), Does.Contain("first").And.Contain("second"));
            Assert.That(first.Calls, Is.Empty);
            Assert.That(second.Calls, Is.Empty);
            Assert.That(opened, Is.False);
        }
    }

    // A miss runs install, build and check on the picked toolchain in that order and prints the player folder last.
    [Test]
    public async Task RunAsync_BuildFakeToolchain_RunsInstallBuildCheckInOrder()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain other = new("other");
        FakeToolchain toolchain = new("player");
        string install = Path.Combine(_output, "install");
        string project = Path.Combine(_projects, "player");

        string[] arguments = ["build", "player", "1.0", "--output", _output];

        int code = await CommandLine.RunAsync(arguments, output, error, [other, toolchain], _ => null, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(
                toolchain.Calls,
                Is.EqualTo([$"InstallAsync 1.0 {_output}", $"BuildAsync {install} {project} 1.0", $"CheckAsync {BuiltPath("1.0")}"]));
            Assert.That(other.Calls, Is.Empty);
            Assert.That(LastLine(output), Is.EqualTo(PlayerPath(toolchain)));
            Assert.That(error.ToString(), Is.Empty);
        }
    }

    // 1 typed version with 2 players installs once and builds both in 1 pass.
    [Test]
    public async Task RunAsync_BuildVersionWith2Players_BuildsBothIn1PassWith1Install()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        string install = Path.Combine(_output, "install");
        string project = Path.Combine(_projects, "player");
        string[] expected =
        [
            $"InstallAsync 1.0-a {_output}",
            $"BuildAsync {install} {project} 1.0-a,1.0-b",
            $"CheckAsync {BuiltPath("1.0-a")}",
            $"CheckAsync {BuiltPath("1.0-b")}",
        ];

        int code = await RunBuildAsync(output, error, toolchain, null, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls, Is.EqualTo(expected));
            Assert.That(File.Exists(Path.Combine(PlayerPath(toolchain, "1.0-a"), "file.bin")), Is.True);
            Assert.That(File.Exists(Path.Combine(PlayerPath(toolchain, "1.0-b"), "file.bin")), Is.True);
        }
    }

    // A player in the cache isn't rebuilt, so the pass builds and checks only the other player and the folders print in listed order.
    [Test]
    public async Task RunAsync_Build1Of2InCache_BuildsOnlyTheOther()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        string cached = PlayerPath(toolchain, "1.0-a");
        _ = Directory.CreateDirectory(cached);
        string install = Path.Combine(_output, "install");
        string project = Path.Combine(_projects, "player");
        string[] expected =
        [
            $"InstallAsync 1.0-b {_output}",
            $"BuildAsync {install} {project} 1.0-b",
            $"CheckAsync {BuiltPath("1.0-b")}",
        ];

        int code = await RunBuildAsync(output, error, toolchain, null, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls, Is.EqualTo(expected));
            Assert.That(LastLines(output, 2), Is.EqualTo([cached, PlayerPath(toolchain, "1.0-b")]));
        }
    }

    // With all players in the cache the toolchain doesn't run a step and the store doesn't get a request.
    [Test]
    public async Task RunAsync_BuildAllInCache_RunsNoStepAndSendsNoRequest()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        string first = PlayerPath(toolchain, "1.0-a");
        string second = PlayerPath(toolchain, "1.0-b");
        _ = Directory.CreateDirectory(first);
        _ = Directory.CreateDirectory(second);

        int code = await RunBuildAsync(output, error, toolchain, store, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(store.Calls, Is.Empty);
            Assert.That(LastLines(output, 2), Is.EqualTo([first, second]));
        }
    }

    // A failed check on the 2nd player keeps the 1st in the folder and the store and doesn't print a folder line.
    // The next run builds only the 2nd and prints both in listed order.
    [Test]
    public async Task RunAsync_BuildCheckFailsOn2nd_Keeps1stStoredAndExits1()
    {
        using StringWriter output = new();
        using StringWriter rerunOutput = new();
        using StringWriter error = new();
        string failing = $"CheckAsync {BuiltPath("1.0-b")}";
        FakeToolchain toolchain = new("player") { FailingStep = failing };
        FakeToolchain rerun = new("player");
        FakeCacheStore store = new();
        string first = PlayerPath(toolchain, "1.0-a");
        string second = PlayerPath(toolchain, "1.0-b");
        string install = Path.Combine(_output, "install");
        string project = Path.Combine(_projects, "player");
        string[] rerunCalls =
        [
            $"InstallAsync 1.0-b {_output}",
            $"BuildAsync {install} {project} 1.0-b",
            $"CheckAsync {BuiltPath("1.0-b")}",
        ];

        int code = await RunBuildAsync(output, error, toolchain, store, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain($"{failing} failed in the fake toolchain."));
            Assert.That(output.ToString().Split(Environment.NewLine), Has.None.EqualTo(first));
            Assert.That(Directory.Exists(first), Is.True);
            Assert.That(store.Files.Keys, Is.EquivalentTo([RemotePath(toolchain, "1.0-a"), RemotePath(toolchain, "1.0-a", ".json")]));
            Assert.That(Directory.Exists(second), Is.False);
        }

        int rerunCode = await RunBuildAsync(rerunOutput, error, rerun, store, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rerunCode, Is.Zero);
            Assert.That(rerun.Calls, Is.EqualTo(rerunCalls));
            Assert.That(store.Files.ContainsKey(RemotePath(rerun, "1.0-b")), Is.True);
            Assert.That(LastLines(rerunOutput, 2), Is.EqualTo([first, second]));
        }
    }

    // An option of a toolchain parses on its subcommand and reaches the listing, so the player id and its key come from the option.
    [Test]
    public async Task RunAsync_BuildWithToolchainOption_ReachesThePlayerListing()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");

        int code = await RunBuildAsync(output, error, toolchain, null, "--variant", "x");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls[0], Is.EqualTo($"InstallAsync 1.0-x {_output}"));
            Assert.That(LastLine(output), Is.EqualTo(PlayerPath(toolchain, "1.0-x")));
        }
    }

    // The folders print in the listed order as the last lines, so a script can read them from the end of the output.
    [Test]
    public async Task RunAsync_BuildSeveralPlayers_PrintsTheFoldersInListedOrder()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");

        int code = await RunBuildAsync(output, error, toolchain, null, "--variant", "b", "--variant", "a");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(LastLines(output, 2), Is.EqualTo([PlayerPath(toolchain, "1.0-b"), PlayerPath(toolchain, "1.0-a")]));
        }
    }

    // The install and its step line get the version as the toolchain pins it, so only AddKeyInputs sees the typed spelling.
    [Test]
    public async Task RunAsync_BuildTypedVersionSpelling_InstallsThePinnedVersion()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { PinnedVersion = "1.0-BETA" };

        string[] arguments = ["build", "player", "1.0-beta", "--output", _output];

        int code = await CommandLine.RunAsync(arguments, output, error, [toolchain], _ => null, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(toolchain.Calls[0], Is.EqualTo($"InstallAsync 1.0-BETA {_output}"));
            Assert.That(output.ToString(), Does.Contain($"Installing player 1.0-BETA into {_output}"));
        }
    }

    // An existing player folder is the player, so the run needs no toolchain step and no store.
    [Test]
    public async Task RunAsync_BuildLocalHit_SkipsToolchainAndStore()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        string player = PlayerPath(toolchain);
        _ = Directory.CreateDirectory(player);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(store.Calls, Is.Empty);
            Assert.That(LastLine(output), Is.EqualTo(player));
        }
    }

    // A player in the store is downloaded and unpacked into the player folder, so the toolchain never runs.
    [Test]
    public async Task RunAsync_BuildRemoteHit_UnpacksArchiveAndSkipsToolchain()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        string remote = RemotePath(toolchain);
        store.Files[remote] = PackedFile([7, 8, 9]);
        string player = PlayerPath(toolchain);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(player, "file.bin")), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Assert.That(File.Exists(player + ".tar.zst"), Is.False);
            Assert.That(store.Calls, Is.EqualTo([$"Download {remote}"]));
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(output.ToString(), Does.Contain($"Downloading {remote}").And.Contain("Unpacking"));
            Assert.That(LastLine(output), Is.EqualTo(player));
        }
    }

    // A miss moves the built folder to the player folder, writes its manifest, uploads the archive and then the manifest and deletes the
    // local archive.
    [Test]
    public async Task RunAsync_BuildMiss_MovesPacksAndUploadsThePlayer()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        string remote = RemotePath(toolchain);
        string manifest = RemotePath(toolchain, extension: ".json");
        string player = PlayerPath(toolchain);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(Directory.Exists(BuiltPath("1.0")), Is.False);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(player, "file.bin")), Is.EqualTo("1.0"u8.ToArray()));
            Assert.That(UnpackedFile(store.Files[remote]), Is.EqualTo("1.0"u8.ToArray()));
            Assert.That(File.Exists(player + ".tar.zst"), Is.False);
            Assert.That(store.Calls, Is.EqualTo([$"Download {remote}", $"Upload {remote}", $"Upload {manifest}"]));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(player, "manifest.json")), Is.EqualTo(store.Files[manifest]));
            Assert.That(LastLine(output), Is.EqualTo(player));
        }
    }

    // A run with no store prints the line of the store opener first and then builds the player locally.
    [Test]
    public async Task RunAsync_BuildWithoutStore_BuildsLocallyAndPrintsTheOpenerLine()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        string player = PlayerPath(toolchain);

        int code = await CommandLine.RunAsync(
            ["build", toolchain.Name, "1.0", "--output", _output],
            output,
            error,
            [toolchain],
            writer =>
            {
                writer.WriteLine("no store");
                return null;
            },
            _projects);

        string[] lines = output.ToString().Split(Environment.NewLine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls, Has.Count.EqualTo(3));
            Assert.That(File.Exists(Path.Combine(player, "file.bin")), Is.True);
            Assert.That(Directory.GetFiles(_output, "*.tar.zst"), Is.Empty);
            Assert.That(lines[0], Is.EqualTo("no store"));
            Assert.That(LastLine(output), Is.EqualTo(player));
        }
    }

    // A failed download stops the run before the toolchain, because the store is broken and not empty.
    [Test]
    public async Task RunAsync_BuildDownloadFails_FailsBeforeAnyToolchainStep()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new() { FailingCall = "Download" };

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("Download failed in the fake store."));
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // A 2nd run into an output folder that another run has fails before the store and the install, because the 2 runs would
    // delete each other's .partial files.
    [Test]
    public async Task RunAsync_BuildOutputLockedByAnotherRun_FailsBeforeAnyStep()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();

        using (new FileStream(Path.Combine(_output, ".fixtures.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            int code = await RunBuildAsync(output, error, toolchain, store);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(code, Is.EqualTo(1));
                Assert.That(error.ToString(), Does.Contain($"The folder {_output} is in use by another fixtures run."));
                Assert.That(store.Calls, Is.Empty);
                Assert.That(toolchain.Calls, Is.Empty);
            }
        }
    }

    // A run removes its lock file at the end, so the next run into the folder starts.
    [Test]
    public async Task RunAsync_BuildDone_RemovesTheLockFile()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await RunBuildAsync(output, error, new FakeToolchain("player"), store: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(File.Exists(Path.Combine(_output, ".fixtures.lock")), Is.False);
        }
    }

    // A damaged archive in the store counts as a miss with a line that has the store path, so the run builds the player
    // again and its upload replaces the damaged copy.
    [Test]
    public async Task RunAsync_BuildDamagedArchive_RebuildsAndReplacesTheStoreCopy()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        byte[] damaged = Encoding.ASCII.GetBytes("not a zstd frame");
        store.Files[RemotePath(toolchain)] = damaged;
        string player = PlayerPath(toolchain);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(output.ToString(), Does.Contain($"Store copy damaged: {RemotePath(toolchain)} ("));
            Assert.That(Directory.Exists(player), Is.True);
            Assert.That(File.Exists(player + ".tar.zst"), Is.False);
            Assert.That(store.Files[RemotePath(toolchain)], Is.Not.EqualTo(damaged));
            Assert.That(toolchain.Calls, Is.Not.Empty);
        }
    }

    // A failed upload keeps the player and its archive, so the next run uploads the archive.
    [Test]
    public async Task RunAsync_BuildUploadFails_FailsAndKeepsPlayerAndArchive()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new() { FailingCall = "Upload" };
        string player = PlayerPath(toolchain);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("Upload failed in the fake store."));
            Assert.That(File.Exists(Path.Combine(player, "file.bin")), Is.True);
            Assert.That(File.Exists(player + ".tar.zst"), Is.True);
        }
    }

    // A failing step prints its message to the error writer and fails. The later steps do not run and nothing is stored.
    [Test]
    public async Task RunAsync_BuildStepThrows_FailsWithTheMessage()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { FailingStep = "BuildAsync" };
        FakeCacheStore store = new();

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("BuildAsync failed in the fake toolchain."));
            Assert.That(toolchain.Calls, Has.Count.EqualTo(2));
            Assert.That(store.Files, Is.Empty);
            Assert.That(Directory.Exists(PlayerPath(toolchain)), Is.False);
        }
    }

    // The store is disposed once the run ends, after a success and after a failed step.
    [TestCase(null)]
    [TestCase("BuildAsync")]
    public async Task RunAsync_BuildWithStore_DisposesTheStore(string? failingStep)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { FailingStep = failingStep };
        FakeCacheStore store = new();

        _ = await RunBuildAsync(output, error, toolchain, store);

        Assert.That(store.Disposed, Is.True);
    }

    // A toolchain with no project folder fails at the key, before the store opens and before any step.
    [Test]
    public async Task RunAsync_BuildMissingProjectFolder_FailsBeforeStoreAndInstall()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("other");
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "other", "1.0", "--output", _output],
            output,
            error,
            [toolchain],
            _ =>
            {
                opened = true;
                return null;
            },
            _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain(Path.Combine(_projects, "other")));
            Assert.That(opened, Is.False);
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // A build without --output fails before any step, because the tool picks no folder on its own.
    [Test]
    public async Task RunAsync_BuildWithoutOutput_Fails()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        bool opened = false;

        int code = await CommandLine.RunAsync(["build", "player", "1.0"], output, error, [toolchain], _ =>
        {
            opened = true;
            return null;
        }, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("--output"));
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(opened, Is.False);
        }
    }

    // A build draws the bar of the install between the install line and the build line.
    [Test]
    public async Task RunAsync_BuildWithInstallProgress_PrintsTheBarBetweenTheSteps()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { Progress = new TransferProgress(5, 10) };

        _ = await RunBuildAsync(output, error, toolchain, null);

        string[] lines = output.ToString().Split(Environment.NewLine);
        int bar = Array.IndexOf(lines, "player 1.0: 50%");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bar, Is.GreaterThan(0));
            Assert.That(lines[bar - 1], Does.StartWith("Installing "));
            Assert.That(lines[bar + 1], Does.StartWith("Building "));
        }
    }

    private Task<int> RunBuildAsync(
        StringWriter output,
        StringWriter error,
        FakeToolchain toolchain,
        FakeCacheStore? store,
        params string[] options)
    {
        string[] arguments = ["build", toolchain.Name, "1.0", "--output", _output, .. options];

        return CommandLine.RunAsync(arguments, output, error, [toolchain], _ => store, _projects);
    }

    private string BuiltPath(string player)
    {
        return Path.Combine(_output, "install", "players", player);
    }

    private string PlayerPath(FakeToolchain toolchain, string player = "1.0")
    {
        return Path.Combine(_output, $"{toolchain.Name}-{player}-{KeyOf(toolchain, player).ShortHash}");
    }

    private string RemotePath(FakeToolchain toolchain, string player = "1.0", string extension = ".tar.zst")
    {
        return $"players/{toolchain.Name}/{player}/{KeyOf(toolchain, player).ShortHash}{extension}";
    }

    private CacheKey KeyOf(FakeToolchain toolchain, string player)
    {
        return PlayerCache.KeyOf(toolchain, player, Path.Combine(_projects, toolchain.Name)).Key;
    }

    private byte[] PackedFile(byte[] bytes)
    {
        string source = _folder.NewFolder();
        TestFolder.WriteBytes(source, "file.bin", bytes);

        string archive = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(source, archive);

        return File.ReadAllBytes(archive);
    }

    private byte[] UnpackedFile(byte[] archiveBytes)
    {
        string archive = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        File.WriteAllBytes(archive, archiveBytes);

        string destination = Path.Combine(_folder.Root, "unpacked");
        Archive.Unpack(archive, destination);

        return File.ReadAllBytes(Path.Combine(destination, "file.bin"));
    }

    // A cause that the message does not contain goes on its own line, so a network or disk failure shows its reason.
    [Test]
    public void FailureText_InnerCauseNotInMessage_PutsItOnTheNextLine()
    {
        IOException cause = new("The remote certificate is invalid.");
        InvalidOperationException exception = new("The SSL connection could not be established.", cause);

        Assert.That(
            CommandLine.FailureText(exception),
            Is.EqualTo("The SSL connection could not be established." + Environment.NewLine + "The remote certificate is invalid."));
    }

    // A cause that the message quotes already is not printed again.
    [Test]
    public void FailureText_InnerCauseInMessage_PrintsTheMessageOnly()
    {
        EndOfStreamException cause = new("The stream ends early.");
        InvalidDataException exception = new("The file 'file.bin' is damaged. The stream ends early. Download it again.", cause);

        Assert.That(CommandLine.FailureText(exception), Is.EqualTo("The file 'file.bin' is damaged. The stream ends early. Download it again."));
    }

    // Each level of a chain of causes gets a line.
    [Test]
    public void FailureText_ChainOf3_PrintsEveryLevel()
    {
        IOException cause = new("Unable to read data.", new IOException("Connection reset."));
        HttpRequestException exception = new("Error while copying content to a stream.", cause);
        string[] expected = ["Error while copying content to a stream.", "Unable to read data.", "Connection reset."];

        Assert.That(CommandLine.FailureText(exception).Split(Environment.NewLine), Is.EqualTo(expected));
    }

    private static string LastLine(StringWriter output)
    {
        return output.ToString().TrimEnd().Split(Environment.NewLine)[^1];
    }

    private static string[] LastLines(StringWriter output, int count)
    {
        return output.ToString().TrimEnd().Split(Environment.NewLine)[^count..];
    }
}
