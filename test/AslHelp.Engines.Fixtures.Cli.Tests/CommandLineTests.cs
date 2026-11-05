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

    // An unknown toolchain fails before any step and the error lists every toolchain, so the user can pick 1.
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
            Assert.That(error.ToString(), Does.Contain("third").And.Contain("first").And.Contain("second"));
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
            Assert.That(toolchain.Calls, Is.EqualTo([$"InstallAsync 1.0 {_output}", $"BuildAsync {install} {project}", $"CheckAsync {install}"]));
            Assert.That(other.Calls, Is.Empty);
            Assert.That(LastLine(output), Is.EqualTo(PlayerPath(toolchain)));
            Assert.That(error.ToString(), Is.Empty);
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

    // A miss moves the install to the player folder, writes its manifest, uploads the archive and then the manifest and deletes the local archive.
    [Test]
    public async Task RunAsync_BuildMiss_MovesPacksAndUploadsThePlayer()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        string remote = RemotePath(toolchain);
        string manifest = RemotePath(toolchain, ".json");
        string player = PlayerPath(toolchain);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(Directory.Exists(Path.Combine(_output, "install")), Is.False);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(player, "file.bin")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(UnpackedFile(store.Files[remote]), Is.EqualTo(new byte[] { 1, 2, 3 }));
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

    // A damaged archive in the store fails with the store path, deletes the download and leaves no player folder behind.
    [Test]
    public async Task RunAsync_BuildDamagedArchive_FailsAndLeavesNoPlayerFolder()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeCacheStore store = new();
        store.Files[RemotePath(toolchain)] = Encoding.ASCII.GetBytes("not a zstd frame");
        string player = PlayerPath(toolchain);

        int code = await RunBuildAsync(output, error, toolchain, store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain(RemotePath(toolchain)));
            Assert.That(File.Exists(player + ".tar.zst"), Is.False);
            Assert.That(Directory.Exists(player), Is.False);
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // A failed upload keeps the player and its archive, so the archive can be uploaded by hand.
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

    private Task<int> RunBuildAsync(StringWriter output, StringWriter error, FakeToolchain toolchain, FakeCacheStore? store)
    {
        return CommandLine.RunAsync(["build", toolchain.Name, "1.0", "--output", _output], output, error, [toolchain], _ => store, _projects);
    }

    private string PlayerPath(FakeToolchain toolchain)
    {
        return Path.Combine(_output, $"{toolchain.Name}-1.0-{KeyOf(toolchain).ShortHash}");
    }

    private string RemotePath(FakeToolchain toolchain, string extension = ".tar.zst")
    {
        return $"players/{toolchain.Name}/1.0/{KeyOf(toolchain).ShortHash}{extension}";
    }

    private CacheKey KeyOf(FakeToolchain toolchain)
    {
        return PlayerCache.KeyOf(toolchain, "1.0", Path.Combine(_projects, toolchain.Name)).Key;
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
}
