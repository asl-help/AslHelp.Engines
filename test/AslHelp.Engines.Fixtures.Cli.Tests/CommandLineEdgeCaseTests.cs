using AslHelp.Engines.Fixtures.Cli.Formats;
using AslHelp.Engines.Fixtures.Cli.Transfer;
using NUnit.Framework;
using System;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class CommandLineEdgeCaseTests
{
    private TestFolder _folder = null!;
    private string _output = null!;
    private string _projects = null!;
    private FakeCacheStore? _store;
    private int _storeOpens;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _projects = _folder.NewFolder();
        _output = _folder.NewFolder();
        _store = null;
        _storeOpens = 0;

        TestFolder.WriteBytes(_projects, "player/file.bin", [4, 5, 6]);
    }

    [TearDown]
    public void TearDown()
    {
        _store?.Dispose();
        _folder.Delete();
    }

    // A toolchain that lists 0 players for its options fails before the store, so a script never reads a step line as a player folder.
    [Test]
    public async Task RunAsync_BuildLists0Players_FailsBeforeTheStore()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { ListedPlayers = [] };

        int code = await RunBuildAsync(output, error, toolchain);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("player 1.0"));
            Assert.That(_storeOpens, Is.Zero);
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // 2 listed ids with 1 pinned version fail before the store, because the 2nd store needs the folder that the 1st store moved.
    [TestCase("1.0-a", "1.0-a")]
    [TestCase("1.0-a", "1.0-A")]
    public async Task RunAsync_BuildListsAPlayerTwice_FailsBeforeTheStore(string first, string second)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { ListedPlayers = [first, second], PinnedVersion = "1.0-a" };

        int code = await RunBuildAsync(output, error, toolchain);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("player 1.0 lists the player 1.0-a twice."));
            Assert.That(_storeOpens, Is.Zero);
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // A bad 2nd player fails with the good 1st player still unbuilt, because every key is built before the store opens.
    [Test]
    public async Task RunAsync_Build2ndPlayerKeyFails_FailsBeforeTheStore()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "../x");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("1.0-../x"));
            Assert.That(_storeOpens, Is.Zero);
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    // Each build goes to the key of its own player when a cached player sits between 2 missing players. The folders print in listed
    // order after every progress line. The install stays because each build came from its own folder.
    [Test]
    public async Task RunAsync_BuildCachedPlayerBetween2Missing_StoresEachBuildUnderItsOwnKey()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        _store = new() { Progress = new TransferProgress(1, 2) };
        string first = PlayerPath(toolchain, "1.0-a");
        string cached = PlayerPath(toolchain, "1.0-b");
        string last = PlayerPath(toolchain, "1.0-c");
        _ = Directory.CreateDirectory(cached);
        string install = Path.Combine(_output, "install");
        string project = Path.Combine(_projects, "player");
        string[] calls =
        [
            $"InstallAsync 1.0-a {_output}",
            $"BuildAsync {install} {project} 1.0-a,1.0-c",
            $"CheckAsync {BuiltPath("1.0-a")}",
            $"CheckAsync {BuiltPath("1.0-c")}",
        ];
        string[] stored =
        [
            RemotePath(toolchain, "1.0-a"),
            RemotePath(toolchain, "1.0-a", ".json"),
            RemotePath(toolchain, "1.0-c"),
            RemotePath(toolchain, "1.0-c", ".json"),
        ];

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b", "--variant", "c");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(toolchain.Calls, Is.EqualTo(calls));
            Assert.That(ReadFile(first), Is.EqualTo("1.0-a"));
            Assert.That(ReadFile(last), Is.EqualTo("1.0-c"));
            Assert.That(ManifestVersion(first), Is.EqualTo("1.0-a"));
            Assert.That(ManifestVersion(last), Is.EqualTo("1.0-c"));
            Assert.That(_store.Files.Keys, Is.EquivalentTo(stored));
            Assert.That(UnpackedFile(_store.Files[RemotePath(toolchain, "1.0-c")]), Is.EqualTo("1.0-c"));
            Assert.That(LastLines(output, 3), Is.EqualTo([first, cached, last]));
            Assert.That(output.ToString(), Does.Contain(": 1.0-a, 1.0-c"));
            Assert.That(Directory.Exists(install), Is.True);
            Assert.That(Directory.Exists(BuiltPath("1.0-a")), Is.False);
        }
    }

    // A missing player, a player in the store and a local player each take their own path in 1 run. Only the missing player gets built
    // and the store only gets requests for the first 2.
    [Test]
    public async Task RunAsync_BuildMissingStoredAndLocal_BuildsOnlyTheMissingOne()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        _store = new();
        _store.Files[RemotePath(toolchain, "1.0-b")] = PackedFile("stored");
        string local = PlayerPath(toolchain, "1.0-c");
        _ = Directory.CreateDirectory(local);
        string install = Path.Combine(_output, "install");
        string project = Path.Combine(_projects, "player");
        string[] calls =
        [
            $"InstallAsync 1.0-a {_output}",
            $"BuildAsync {install} {project} 1.0-a",
            $"CheckAsync {BuiltPath("1.0-a")}",
        ];
        string[] storeCalls =
        [
            $"Download {RemotePath(toolchain, "1.0-a")}",
            $"Download {RemotePath(toolchain, "1.0-b")}",
            $"Upload {RemotePath(toolchain, "1.0-a")}",
            $"Upload {RemotePath(toolchain, "1.0-a", ".json")}",
        ];

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b", "--variant", "c");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls, Is.EqualTo(calls));
            Assert.That(_store.Calls, Is.EqualTo(storeCalls));
            Assert.That(ReadFile(PlayerPath(toolchain, "1.0-b")), Is.EqualTo("stored"));
            Assert.That(LastLines(output, 3), Is.EqualTo([PlayerPath(toolchain, "1.0-a"), PlayerPath(toolchain, "1.0-b"), local]));
        }
    }

    // Another machine with an empty output folder gets every player from the store, each with its own files and manifest.
    [Test]
    public async Task RunAsync_BuildOnEmptyOutputAfterUpload_RestoresEveryPlayerFromTheStore()
    {
        using StringWriter firstOutput = new();
        using StringWriter secondOutput = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeToolchain restorer = new("player");
        _store = new();
        _ = await RunBuildAsync(firstOutput, error, toolchain, "--variant", "a", "--variant", "b");
        _output = _folder.NewFolder();

        int code = await RunBuildAsync(secondOutput, error, restorer, "--variant", "a", "--variant", "b");

        string first = PlayerPath(restorer, "1.0-a");
        string second = PlayerPath(restorer, "1.0-b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(restorer.Calls, Is.Empty);
            Assert.That(ReadFile(first), Is.EqualTo("1.0-a"));
            Assert.That(ReadFile(second), Is.EqualTo("1.0-b"));
            Assert.That(ManifestVersion(first), Is.EqualTo("1.0-a"));
            Assert.That(ManifestVersion(second), Is.EqualTo("1.0-b"));
            Assert.That(Directory.GetFiles(_output), Is.Empty);
            Assert.That(LastLines(secondOutput, 2), Is.EqualTo([first, second]));
        }
    }

    // A failed check on the 1st player stores nothing and skips the 2nd check.
    // The built folder doesn't get a manifest and the next run builds both.
    [Test]
    public async Task RunAsync_BuildCheckFailsOn1st_StoresNothingAndTheNextRunBuildsBoth()
    {
        using StringWriter firstOutput = new();
        using StringWriter secondOutput = new();
        using StringWriter error = new();
        string failing = $"CheckAsync {BuiltPath("1.0-a")}";
        FakeToolchain toolchain = new("player") { FailingStep = failing };
        FakeToolchain rerun = new("player");
        _store = new();

        int code = await RunBuildAsync(firstOutput, error, toolchain, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(toolchain.Calls[^1], Is.EqualTo(failing));
            Assert.That(_store.Files, Is.Empty);
            Assert.That(File.Exists(Path.Combine(BuiltPath("1.0-a"), "manifest.json")), Is.False);
            Assert.That(Directory.Exists(PlayerPath(toolchain, "1.0-a")), Is.False);
            Assert.That(Lines(firstOutput), Has.None.EqualTo(PlayerPath(toolchain, "1.0-b")));
        }

        int rerunCode = await RunBuildAsync(secondOutput, error, rerun, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rerunCode, Is.Zero);
            Assert.That(rerun.Calls[1], Does.EndWith(" 1.0-a,1.0-b"));
            Assert.That(LastLines(secondOutput, 2), Is.EqualTo([PlayerPath(rerun, "1.0-a"), PlayerPath(rerun, "1.0-b")]));
        }
    }

    // A failed upload of the 2nd of 3 keeps the 1st stored and the 2nd on disk with its archive.
    // The run doesn't check the 3rd or print a folder line.
    [Test]
    public async Task RunAsync_BuildUploadFailsOn2ndOf3_Keeps1stStoredAnd2ndOnDisk()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        string second = PlayerPath(toolchain, "1.0-b");
        _store = new() { FailingCall = $"Upload {RemotePath(toolchain, "1.0-b")}" };

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b", "--variant", "c");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("Upload failed in the fake store."));
            Assert.That(_store.Files.Keys, Is.EquivalentTo([RemotePath(toolchain, "1.0-a"), RemotePath(toolchain, "1.0-a", ".json")]));
            Assert.That(File.Exists(PlayerPath(toolchain, "1.0-a") + ".tar.zst"), Is.False);
            Assert.That(File.Exists(Path.Combine(second, "file.bin")), Is.True);
            Assert.That(File.Exists(second + ".tar.zst"), Is.True);
            Assert.That(toolchain.Calls, Has.None.EqualTo($"CheckAsync {BuiltPath("1.0-c")}"));
            Assert.That(Directory.Exists(PlayerPath(toolchain, "1.0-c")), Is.False);
            Assert.That(Lines(output), Has.None.EqualTo(PlayerPath(toolchain, "1.0-a")));
        }
    }

    // The next run after a failed upload finds the folder, doesn't upload it again and builds only the other player.
    [Test]
    public async Task RunAsync_BuildAfterFailedUpload_FindsThe1stAndBuildsThe2nd()
    {
        using StringWriter firstOutput = new();
        using StringWriter secondOutput = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeToolchain rerun = new("player");
        string first = PlayerPath(toolchain, "1.0-a");
        string second = PlayerPath(toolchain, "1.0-b");
        _store = new() { FailingCall = "Upload" };

        _ = await RunBuildAsync(firstOutput, error, toolchain, "--variant", "a", "--variant", "b");
        _store.Dispose();
        _store = new();
        string[] storeCalls =
        [
            $"Download {RemotePath(rerun, "1.0-b")}",
            $"Upload {RemotePath(rerun, "1.0-b")}",
            $"Upload {RemotePath(rerun, "1.0-b", ".json")}",
        ];

        int code = await RunBuildAsync(secondOutput, error, rerun, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(_store.Calls, Is.EqualTo(storeCalls));
            Assert.That(File.Exists(first + ".tar.zst"), Is.True);
            Assert.That(LastLines(secondOutput, 2), Is.EqualTo([first, second]));
        }
    }

    // After a failed upload the readme's way to build again is to delete the folder, so the old archive can't stop the new pack.
    [Test]
    public async Task RunAsync_BuildAfterFailedUploadAndDeletedFolder_ReplacesTheOldArchive()
    {
        using StringWriter firstOutput = new();
        using StringWriter secondOutput = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        FakeToolchain rerun = new("player");
        string first = PlayerPath(toolchain, "1.0-a");
        _store = new() { FailingCall = "Upload" };

        _ = await RunBuildAsync(firstOutput, error, toolchain, "--variant", "a", "--variant", "b");
        string[] leftovers = Directory.GetFiles(_output);

        Directory.Delete(first, recursive: true);
        _store.Dispose();
        _store = new();

        int code = await RunBuildAsync(secondOutput, error, rerun, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(leftovers, Is.EqualTo([first + ".tar.zst"]));
            Assert.That(code, Is.Zero);
            Assert.That(Directory.GetFiles(_output), Is.Empty);
            Assert.That(_store.Files.ContainsKey(RemotePath(rerun, "1.0-a")), Is.True);
            Assert.That(_store.Files.ContainsKey(RemotePath(rerun, "1.0-b")), Is.True);
        }
    }

    // A damaged 2nd archive stops the run before the install with the path of that archive and the 1st restored player stays.
    [Test]
    public async Task RunAsync_BuildDamaged2ndArchive_KeepsThe1stAndSkipsTheInstall()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        _store = new();
        _store.Files[RemotePath(toolchain, "1.0-a")] = PackedFile("stored");
        _store.Files[RemotePath(toolchain, "1.0-b")] = Encoding.ASCII.GetBytes("not a zstd frame");

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain(RemotePath(toolchain, "1.0-b")));
            Assert.That(ReadFile(PlayerPath(toolchain, "1.0-a")), Is.EqualTo("stored"));
            Assert.That(Directory.Exists(PlayerPath(toolchain, "1.0-b")), Is.False);
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(Lines(output), Has.None.EqualTo(PlayerPath(toolchain, "1.0-a")));
        }
    }

    // A failed install keeps the restored player, doesn't build and doesn't print that player as if the run was done.
    [Test]
    public async Task RunAsync_BuildInstallFailsWith1Restored_KeepsItAndPrintsNoFolder()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { FailingStep = "InstallAsync" };
        string cached = PlayerPath(toolchain, "1.0-a");
        _ = Directory.CreateDirectory(cached);

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(toolchain.Calls, Is.EqualTo([$"InstallAsync 1.0-b {_output}"]));
            Assert.That(Directory.Exists(cached), Is.True);
            Assert.That(Lines(output), Has.None.EqualTo(cached));
        }
    }

    // A toolchain that returns too few or too many folders fails before the 1st check, because the folders pair up with the players by position.
    [TestCase(0)]
    [TestCase(3)]
    public async Task RunAsync_BuildReturnsWrongFolderCount_FailsBeforeTheChecks(int count)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { ReturnedFolders = [.. Enumerable.Repeat(_output, count)] };
        _store = new();

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain($"player built 2 players but returned {count} folders."));
            Assert.That(toolchain.Calls, Has.None.StartsWith("CheckAsync"));
            Assert.That(_store.Files, Is.Empty);
        }
    }

    // A cancel during the 2nd check exits with a failure, doesn't print a failure text and keeps the 1st player stored.
    [Test]
    public async Task RunAsync_BuildCanceledDuring2ndCheck_Keeps1stAndPrintsNoFailure()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player")
        {
            FailingStep = $"CheckAsync {BuiltPath("1.0-b")}",
            StepException = new OperationCanceledException(),
        };
        _store = new();

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a", "--variant", "b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Not.Zero);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(_store.Files.ContainsKey(RemotePath(toolchain, "1.0-a")), Is.True);
            Assert.That(Directory.Exists(PlayerPath(toolchain, "1.0-b")), Is.False);
            Assert.That(_store.Disposed, Is.True);
            Assert.That(Lines(output), Has.None.EqualTo(PlayerPath(toolchain, "1.0-a")));
        }
    }

    // An --output with spaces, non-ASCII chars, brackets, a '..' segment and a trailing separator still prints plain full paths.
    [Test]
    public async Task RunAsync_BuildOddOutputPath_PrintsNormalizedFullPaths()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        const string Odd = "sp ace ü日 [x]";
        _output = Path.Combine(_folder.Root, Odd);
        string typed = Path.Combine(_folder.Root, Odd, "..", Odd) + Path.DirectorySeparatorChar;

        string[] arguments = ["build", "player", "1.0", "--output", typed, "--variant", "a", "--variant", "b"];

        int code = await CommandLine.RunAsync(arguments, output, error, [toolchain], OpenStore, _projects);

        string first = PlayerPath(toolchain, "1.0-a");
        string second = PlayerPath(toolchain, "1.0-b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(LastLines(output, 2), Is.EqualTo([first, second]));
            Assert.That(File.Exists(Path.Combine(first, "manifest.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(second, "manifest.json")), Is.True);
        }
    }

    // A player id with a non-ASCII char, a space, a bracket or a percent sign reaches the folder, the store path and the manifest as it is.
    // The bars of the install and the store show it unchanged.
    [TestCase("ü日")]
    [TestCase("a b")]
    [TestCase("[rc]")]
    [TestCase("a%20b")]
    public async Task RunAsync_BuildOddPlayerId_UsesItAsItIs(string variant)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player") { Progress = new TransferProgress(1, 2) };
        _store = new() { Progress = new TransferProgress(1, 2) };
        string id = $"1.0-{variant}";
        string player = PlayerPath(toolchain, id);

        int code = await RunBuildAsync(output, error, toolchain, "--variant", variant);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(LastLines(output, 1), Is.EqualTo([player]));
            Assert.That(ManifestVersion(player), Is.EqualTo(id));
            Assert.That(_store.Files.ContainsKey(RemotePath(toolchain, id)), Is.True);
            Assert.That(output.ToString(), Does.Contain($"player {id}: 50%").And.Contain($"{RemotePath(toolchain, id)}: 50%"));
        }
    }

    // A command line that doesn't fit the subcommand fails before the store and leaves the output folder empty. The test swaps 'OUT' for
    // the output folder. The last case puts --output in front of the toolchain.
    [TestCase("build player --output OUT")]
    [TestCase("build player 1.0 2.0 --output OUT")]
    [TestCase("build player 1.0 --output OUT --output OUT")]
    [TestCase("build player 1.0 --output")]
    [TestCase("build player 1.0 --output OUT --variant")]
    [TestCase("build player 1.0 --output OUT --unknown value")]
    [TestCase("build --output OUT player 1.0")]
    public async Task RunAsync_RefusedCommandLine_FailsBeforeTheStore(string line)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        string[] arguments = [.. CommandLineParser.SplitCommandLine(line).Select(token => token == "OUT" ? _output : token)];

        int code = await CommandLine.RunAsync(arguments, output, error, [toolchain], OpenStore, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Is.Not.Empty);
            Assert.That(_storeOpens, Is.Zero);
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(_output), Is.Empty);
        }
    }

    // 2 toolchains can use the same option name, because each subcommand reads its own option. The value reaches only the picked toolchain.
    [Test]
    public async Task RunAsync_Build2ToolchainsWithSameOptionName_ReachesOnlyThePickedOne()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain first = new("first");
        FakeToolchain second = new("second");
        TestFolder.WriteBytes(_projects, "second/file.bin", [7]);

        string[] arguments = ["build", "second", "1.0", "--output", _output, "--variant", "a"];

        int code = await CommandLine.RunAsync(arguments, output, error, [first, second], OpenStore, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(first.Calls, Is.Empty);
            Assert.That(second.Calls[0], Is.EqualTo($"InstallAsync 1.0-a {_output}"));
        }
    }

    // The options of 1 toolchain don't parse on another subcommand, so an option can't silently do nothing.
    [Test]
    public async Task RunAsync_BuildOptionOfAnotherToolchain_FailsBeforeTheStore()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain other = new("other");
        FakeToolchain toolchain = new("player", "--mode");

        string[] arguments = ["build", "player", "1.0", "--output", _output, "--variant", "a"];

        int code = await CommandLine.RunAsync(arguments, output, error, [other, toolchain], OpenStore, _projects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("--variant"));
            Assert.That(_storeOpens, Is.Zero);
            Assert.That(toolchain.Calls, Is.Empty);
            Assert.That(other.Calls, Is.Empty);
        }
    }

    // A built folder with a longer manifest from a stopped run gets a clean manifest, because the write replaces the whole file.
    [Test]
    public async Task RunAsync_BuildFolderWithOldManifest_ReplacesIt()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        TestFolder.WriteBytes(BuiltPath("1.0-a"), "manifest.json", TestFolder.Noise(64 * 1024));

        int code = await RunBuildAsync(output, error, toolchain, "--variant", "a");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(ManifestVersion(PlayerPath(toolchain, "1.0-a")), Is.EqualTo("1.0-a"));
        }
    }

    private Task<int> RunBuildAsync(StringWriter output, StringWriter error, FakeToolchain toolchain, params string[] options)
    {
        string[] arguments = ["build", toolchain.Name, "1.0", "--output", _output, .. options];

        return CommandLine.RunAsync(arguments, output, error, [toolchain], OpenStore, _projects);
    }

    private ICacheStore? OpenStore(TextWriter writer)
    {
        _storeOpens++;

        return _store;
    }

    private string BuiltPath(string player)
    {
        return Path.Combine(_output, "install", "players", player);
    }

    private string PlayerPath(FakeToolchain toolchain, string player)
    {
        return Path.Combine(_output, $"{toolchain.Name}-{player}-{KeyOf(toolchain, player).ShortHash}");
    }

    private string RemotePath(FakeToolchain toolchain, string player, string extension = ".tar.zst")
    {
        return $"players/{toolchain.Name}/{player}/{KeyOf(toolchain, player).ShortHash}{extension}";
    }

    private CacheKey KeyOf(FakeToolchain toolchain, string player)
    {
        return PlayerCache.KeyOf(toolchain, player, Path.Combine(_projects, toolchain.Name)).Key;
    }

    private byte[] PackedFile(string content)
    {
        string source = _folder.NewFolder();
        TestFolder.WriteBytes(source, "file.bin", Encoding.UTF8.GetBytes(content));

        string archive = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(source, archive);

        return File.ReadAllBytes(archive);
    }

    private string UnpackedFile(byte[] archiveBytes)
    {
        string folder = _folder.NewFolder();
        string archive = Path.Combine(folder, "file.tar.zst");
        File.WriteAllBytes(archive, archiveBytes);

        string destination = Path.Combine(folder, "unpacked");
        Archive.Unpack(archive, destination);

        return ReadFile(destination);
    }

    private static string ReadFile(string folder)
    {
        return File.ReadAllText(Path.Combine(folder, "file.bin"));
    }

    private static string? ManifestVersion(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "manifest.json")));

        return document.RootElement.GetProperty("version").GetString();
    }

    private static string[] Lines(StringWriter output)
    {
        return output.ToString().Split(Environment.NewLine);
    }

    private static string[] LastLines(StringWriter output, int count)
    {
        return output.ToString().TrimEnd().Split(Environment.NewLine)[^count..];
    }
}
