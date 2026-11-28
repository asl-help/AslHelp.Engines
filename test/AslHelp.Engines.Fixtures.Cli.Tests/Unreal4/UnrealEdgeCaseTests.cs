using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Unreal4;
using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealEdgeCaseTests
{
    private static readonly string _buildDirectory = Path.Combine(AppContext.BaseDirectory, "build", "unreal4");
    private static readonly string _projectDirectory = Path.Combine(AppContext.BaseDirectory, "projects", "unreal4");

    private string _archiveName = null!;
    private TestFolder _folder = null!;
    private StringWriter _output = null!;
    private string _outputPath = null!;

    [SetUp]
    public void SetUp()
    {
        _archiveName = "WindowsNoEditor";
        _folder = new TestFolder();
        _output = new StringWriter();
        _outputPath = _folder.NewFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _folder.Delete();
    }

    // ListPlayers lists every player of every row for any casing of the app and AddKeyInputs returns that same id, so the later steps
    // get the pinned spelling.
    [Test]
    public void ListPlayers_EveryPinnedVersion_ListsTheIdsThatAddKeyInputsReturns()
    {
        var unreal = NewUnreal();

        using (Assert.EnterMultipleScope())
        {
            foreach (var release in EmbeddedReleases<UnrealRelease>.All)
            {
                var players = unreal.ListPlayers(release.App.Replace("UE_", "ue_", StringComparison.Ordinal), Parse(unreal));

                Assert.That(players, Is.EqualTo(release.Players.Select(player => player.IdOf(release.Version))), release.Version);

                foreach (string player in players)
                {
                    Assert.That(unreal.AddKeyInputs(new CacheKeyBuilder("player"), player, _projectDirectory), Is.EqualTo(player));
                }
            }
        }
    }

    // The app and the minor spelling of a player id give the key of the pinned spelling, so a typed spelling never builds a 2nd time.
    [TestCase("ue_4.19-win-x64-shipping")]
    [TestCase("4.19-win-x64-shipping")]
    public void AddKeyInputs_OtherSpellingOfTheVersion_GivesThePinnedKey(string player)
    {
        var unreal = NewUnreal();

        Assert.That(KeyOf(unreal, player), Is.EqualTo(KeyOf(unreal, "4.19.2-win-x64-shipping")));
    }

    // 1 changed byte in a step script changes the key, so an edited step builds every player again.
    [Test]
    public void AddKeyInputs_StepScriptChanged_GivesOtherKey()
    {
        string copy = CopyBuildDirectory();
        var before = KeyOf(NewUnreal(buildDirectory: copy), "4.19.2-win-x64-shipping");

        File.AppendAllText(Path.Combine(copy, "steps", "package.bat"), " ");

        Assert.That(KeyOf(NewUnreal(buildDirectory: copy), "4.19.2-win-x64-shipping"), Is.Not.EqualTo(before));
    }

    // A player id without a valid tag throws with the tag before any add, so a bad id never hashes the image folder.
    [TestCase("4.19.2-win-arm64-shipping")]
    [TestCase("4.19.2-")]
    [TestCase("4.19.2")]
    public void AddKeyInputs_BadTag_ThrowsAndAddsNothing(string player)
    {
        CacheKeyBuilder builder = new("player");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => NewUnreal().AddKeyInputs(builder, player, _projectDirectory), Throws.TypeOf<FormatException>());
            Assert.That(builder.Build(), Is.EqualTo(new CacheKeyBuilder("player").Build()));
        }
    }

    // A missing image fails before the delete, so the players of the last build stay.
    [Test]
    public async Task BuildAsync_ImageMissing_KeepsThePlayersOfTheLastBuild()
    {
        var players = await BuildAsync(NewBuildHost(), "UE_4.19", "4.19.2-win-x64-shipping");
        FakeBuildHost buildHost = new(_ => 0) { HasImage = false };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"), Throws.InvalidOperationException);
            Assert.That(File.Exists(Path.Combine(players[0], "fixture.json")), Is.True);
        }
    }

    // The step containers of a killed run go before the scratch delete and the 1st step, because a running one keeps the scratch open.
    // Each step has the scratch as its owner, so the next build finds its container.
    [Test]
    public async Task BuildAsync_AnyRow_RemovesLeftoverStepsOfTheScratchFirst()
    {
        string scratch = Path.Combine(_outputPath, "unreal4-4.19.2-build");
        var buildHost = NewBuildHost();

        _ = await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buildHost.RemovedOwners, Is.EqualTo([(scratch, 0)]));
            Assert.That(buildHost.Steps.Select(static step => step.Owner), Is.All.EqualTo(scratch));
        }
    }

    // On 4.0 the runtimes step is the 1st container, so the rule for this user is set before it runs.
    [Test]
    public async Task BuildAsync_ImageRuntimesRow_GrantsBeforeTheRuntimesStep()
    {
        string scratch = Path.Combine(_outputPath, "unreal4-4.0.2-build");
        string engine = WriteEngine("UE_4.0");
        string? firstScript = null;
        var buildHost = NewBuildHost(step => firstScript ??= step.Command[^1].Split(' ')[0]);

        _ = await BuildAsync(buildHost, "UE_4.0", "4.0.2-win-x64-development");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstScript, Is.EqualTo(@"C:\r\runtimes.bat"));
            Assert.That(buildHost.GrantedFolders, Is.EqualTo([(scratch, 0), (engine, 0)]));
        }
    }

    // A 4.0 x64 player gets the x64 DLL of the runtimes step and not the x86 one of the same name.
    [Test]
    public async Task BuildAsync_ImageRuntimesRowX64Player_CopiesTheX64Runtime()
    {
        byte[]? x64 = null;

        // Read the DLL right after the step, because the build deletes the archives folder at the end.
        var buildHost = NewBuildHost(step =>
        {
            if (step.Command[^1].StartsWith(@"C:\r\runtimes.bat", StringComparison.Ordinal))
            {
                x64 = File.ReadAllBytes(Path.Combine(HostPath(step, @"C:\o"), "runtimes", "x64", "runtime.dll"));
            }
        });

        var players = await BuildAsync(buildHost, "UE_4.0", "4.0.2-win-x64-development");

        string runtime = Path.Combine(players[0], "Fixture", "Binaries", "Win64", "runtime.dll");
        Assert.That(await File.ReadAllBytesAsync(runtime), Is.EqualTo(x64));
    }

    // A 4.19 x86 player gets the DLL of AppLocalDependencies/Win32 and not the Win64 one of the same name.
    [Test]
    public async Task BuildAsync_EngineRuntimesRowX86Player_CopiesFromWin32()
    {
        var players = await BuildAsync(NewBuildHost(), "UE_4.19", "4.19.2-win-x86-shipping");

        string runtime = Path.Combine(players[0], "Fixture", "Binaries", "Win32", "runtime.dll");
        string dependencies = Path.Combine(EnginePath("UE_4.19"), "Engine", "Binaries", "ThirdParty", "AppLocalDependencies");
        string win32 = Path.Combine(dependencies, "Win32", "Misc", "runtime.dll");
        Assert.That(await File.ReadAllBytesAsync(runtime), Is.EqualTo(await File.ReadAllBytesAsync(win32)));
    }

    // An engine folder with a trailing separator still gets the scratch next to engines, so the cleanup of old installs never
    // removes it.
    [Test]
    public async Task BuildAsync_EngineWithTrailingSeparator_PutsTheScratchNextToEngines()
    {
        string engine = WriteEngine("UE_4.19");

        _ = await NewUnreal(NewBuildHost()).BuildAsync(
            engine + Path.DirectorySeparatorChar,
            _projectDirectory,
            ["4.19.2-win-x64-shipping"],
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(_outputPath, "unreal4-4.19.2-build")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(_outputPath, DiskSpace.EnginesFolder, "unreal4-4.19.2-build")), Is.False);
        }
    }

    // 5.x archives into Windows in place of WindowsNoEditor, so the only folder of the archive is the player whatever its name.
    [Test]
    public async Task BuildAsync_ArchiveFolderOf5x_BecomesThePlayer()
    {
        _archiveName = "Windows";

        var players = await BuildAsync(NewBuildHost(), "UE_5.0", "5.0.3-win-x64-shipping");

        Assert.That(File.Exists(Path.Combine(players[0], "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.exe")), Is.True);
    }

    // An archive with 2 folders throws with the out folder, so the build never picks 1 of them at random.
    [Test]
    public void BuildAsync_TwoArchiveFolders_ThrowsWithTheOutFolder()
    {
        var buildHost = NewBuildHost(step =>
        {
            if (step.Command[^1].Split(' ') is [@"C:\r\package.bat", _, _, string tag, ..])
            {
                _ = Directory.CreateDirectory(Path.Combine(HostPath(step, @"C:\o"), tag, "Windows"));
            }
        });

        Assert.That(
            async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"),
            Throws.InvalidOperationException.With.Message.Contains(Path.Combine(_outputPath, "unreal4-4.19.2-build", "out", "win-x64-shipping")));
    }

    // A player that imports a DLL of neither the pool nor Windows throws with the DLL and doesn't get a manifest.
    [Test]
    public void BuildAsync_ImportNowhere_ThrowsWithTheDllAndWritesNoManifest()
    {
        FakeBuildHost buildHost = new(step => RunFakeStep(step, "missing_runtime.dll"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"),
                Throws.TypeOf<FileNotFoundException>().With.Message.Contains("missing_runtime.dll"));
            Assert.That(File.Exists(Path.Combine(_outputPath, "unreal4-4.19.2-build", "players", "win-x64-shipping", "fixture.json")), Is.False);
        }
    }

    // A failed 2nd package stops the build. Every started container is removed and no player folder is made.
    [Test]
    public void BuildAsync_SecondPackageFails_RemovesEveryContainerAndMakesNoPlayer()
    {
        FakeBuildHost buildHost = new(step =>
            step.Command[^1].Contains("win-x64-development -skipcook", StringComparison.Ordinal) ? 3 : RunFakeStep(step));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await BuildAsync(
                    buildHost,
                    "UE_4.19",
                    "4.19.2-win-x64-shipping",
                    "4.19.2-win-x64-development",
                    "4.19.2-win-x86-shipping"),
                Throws.InvalidOperationException.With.Message.Contains("package-win-x64-development").And.Message.Contains("code 3"));
            Assert.That(buildHost.Steps, Has.Count.EqualTo(3));
            Assert.That(buildHost.DisposedRuns, Is.EqualTo(3));
            Assert.That(Directory.GetFileSystemEntries(Path.Combine(_outputPath, "unreal4-4.19.2-build", "players")), Is.Empty);
        }
    }

    // A step that crashes with an NTSTATUS code throws with the code in hex, such as 0xC0000135 for a DLL that isn't found.
    [Test]
    public void BuildAsync_StepCrashesWithNtStatus_ThrowsWithTheHexCode()
    {
        FakeBuildHost buildHost = new(step => step.Command[^1].Contains("editor.bat", StringComparison.Ordinal) ? unchecked((int)0xC0000135) : 0);

        Assert.That(
            async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"),
            Throws.InvalidOperationException.With.Message.Contains("(0xC0000135)"));
    }

    // An output folder with a space and a char outside ASCII builds, because every path reaches the steps as a mount.
    [Test]
    public async Task BuildAsync_OutputWithSpaceAndUnicode_BuildsThePlayer()
    {
        _outputPath = Path.Combine(_folder.NewFolder(), "fixtures ü 1");
        var buildHost = NewBuildHost();

        var players = await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buildHost.Steps[0].Mounts[0].HostPath, Is.EqualTo(EnginePath("UE_4.19")));
            Assert.That(File.Exists(Path.Combine(players[0], "fixture.json")), Is.True);
        }
    }

    // Each player gets the editor log and its own package log, never the log of another player.
    [Test]
    public async Task BuildAsync_TwoPlayers_EachGetsOnlyItsOwnPackageLog()
    {
        var players = await BuildAsync(NewBuildHost(), "UE_4.19", "4.19.2-win-x64-shipping", "4.19.2-win-x86-shipping");

        Assert.That(
            Directory.GetFiles(Path.Combine(players[1], "logs")).Select(Path.GetFileName),
            Is.EquivalentTo((string[])["editor.log", "package-win-x86-shipping.log"]));
    }

    // A folder without fixture.json throws with the path of the file before any start.
    [Test]
    public void CheckAsync_NoManifest_ThrowsFileNotFoundException()
    {
        string player = _folder.NewFolder();

        Assert.That(
            async () => await NewUnreal().CheckAsync(player, CancellationToken.None),
            Throws.TypeOf<FileNotFoundException>().With.Message.Contains(Path.Combine(player, UnrealFixtureManifest.FileName)));
    }

    // A manifest whose player isn't a tag throws with that player before any start.
    [Test]
    public void CheckAsync_ManifestPlayerNotATag_ThrowsWithThePlayer()
    {
        string player = _folder.NewFolder();
        new UnrealFixtureManifest("4.19.2", "win-arm64-shipping", UnrealConfig.Shipping, [], []).Write(player);

        Assert.That(
            async () => await NewUnreal().CheckAsync(player, CancellationToken.None),
            Throws.TypeOf<FormatException>().With.Message.Contains("win-arm64-shipping"));
    }

    // An arch outside x64 and x86 fails the parse before the store opens and before the output folder exists.
    [Test]
    public async Task RunAsync_BuildUnknownArch_FailsBeforeTheOutputFolder()
    {
        using StringWriter error = new();
        string folder = Path.Combine(_folder.Root, "new");
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "unreal4", "4.19", "--output", folder, "--arch", "arm64"],
            _output,
            error,
            [NewUnreal()],
            _ =>
            {
                opened = true;
                return null;
            },
            Path.Combine(AppContext.BaseDirectory, "projects"),
            _ => long.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("arm64"));
            Assert.That(opened, Is.False);
            Assert.That(Directory.Exists(folder), Is.False);
        }
    }

    // 4.0 doesn't have an x86 development player, so the options leave none and the run fails before the store and the install.
    [Test]
    public async Task RunAsync_BuildNarrowedToNoPlayer_FailsBeforeTheStore()
    {
        using StringWriter error = new();
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "unreal4", "4.0", "--output", _outputPath, "--arch", "x86", "--config", "development"],
            _output,
            error,
            [NewUnreal()],
            _ =>
            {
                opened = true;
                return null;
            },
            Path.Combine(AppContext.BaseDirectory, "projects"),
            _ => long.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("unreal4 4.0"));
            Assert.That(opened, Is.False);
            Assert.That(Directory.GetFileSystemEntries(_outputPath), Is.Empty);
        }
    }

    // The command line of the real tool has a subcommand for unreal4 with --arch and --config.
    [Test]
    public async Task RunAsync_RealToolchainsHelp_ListsUnreal4AndItsOptions()
    {
        using StringWriter buildOutput = new();
        using StringWriter toolchainOutput = new();
        using StringWriter error = new();

        int buildCode = await CommandLine.RunAsync(["build", "--help"], buildOutput, error);
        int toolchainCode = await CommandLine.RunAsync(["build", "unreal4", "--help"], toolchainOutput, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buildCode, Is.Zero);
            Assert.That(buildOutput.ToString(), Does.Contain("unreal4"));
            Assert.That(toolchainCode, Is.Zero);
            Assert.That(toolchainOutput.ToString(), Does.Contain("<version>").And.Contain("--arch").And.Contain("--config"));
        }
    }

    private static string HostPath(BuildStep step, string containerPath)
    {
        return step.Mounts.Single(mount => mount.ContainerPath == containerPath).HostPath;
    }

    private static CacheKey KeyOf(Unreal unreal, string player)
    {
        CacheKeyBuilder builder = new("player");
        _ = unreal.AddKeyInputs(builder, player, _projectDirectory);

        return builder.Build();
    }

    private static ParseResult Parse(Unreal unreal, params string[] args)
    {
        return new RootCommand { Options = { unreal.Options[0], unreal.Options[1] } }.Parse(args);
    }

    // Write what each step writes for real: its log, an x64 and an x86 runtime with other bytes or the archive of a player.
    private int? RunFakeStep(BuildStep step, string import = "runtime.dll")
    {
        string command = step.Command[^1];
        string[] words = command.Split(' ');
        string name = command[(command.IndexOf(@"C:\l\", StringComparison.Ordinal) + 5)..command.IndexOf(".log", StringComparison.Ordinal)];
        WriteBytes(HostPath(step, @"C:\l"), $"{name}.log", "done"u8.ToArray());

        if (words[0] == @"C:\r\runtimes.bat")
        {
            FakeExecutable.Write(Path.Combine(HostPath(step, @"C:\o"), "runtimes", "x64", "runtime.dll"), "KERNEL32.dll");
            FakeExecutable.Write(Path.Combine(HostPath(step, @"C:\o"), "runtimes", "x86", "runtime.dll"));
        }
        else if (words[0] == @"C:\r\package.bat")
        {
            string archive = Path.Combine(HostPath(step, @"C:\o"), words[3], _archiveName);
            var player = UnrealPlayer.Parse(words[3]);

            // The scratch folder is unreal4-<version>-build, so its 2nd part is the version of the build.
            var version = Version.Parse(Path.GetFileName(Path.GetDirectoryName(HostPath(step, @"C:\o")))!.Split('-')[1]);
            FakeExecutable.Write(player.ExecutablePath(archive, version), import, "KERNEL32.dll");
            WriteBytes(archive, Path.GetRelativePath(archive, player.PdbPath(archive, version)), [1]);
        }

        return 0;
    }

    private FakeBuildHost NewBuildHost(Action<BuildStep>? before = null)
    {
        return new FakeBuildHost(step =>
        {
            int? code = RunFakeStep(step);
            before?.Invoke(step);

            return code;
        });
    }

    private Task<IReadOnlyList<string>> BuildAsync(FakeBuildHost buildHost, string app, params string[] players)
    {
        string engine = EnginePath(app);

        if (!Directory.Exists(engine))
        {
            _ = WriteEngine(app);
        }

        return NewUnreal(buildHost).BuildAsync(engine, _projectDirectory, players, CancellationToken.None);
    }

    private string CopyBuildDirectory()
    {
        string copy = _folder.NewFolder();

        foreach (string file in Directory.EnumerateFiles(_buildDirectory, "*", SearchOption.AllDirectories))
        {
            WriteBytes(copy, Path.GetRelativePath(_buildDirectory, file), File.ReadAllBytes(file));
        }

        return copy;
    }

    private string EnginePath(string app)
    {
        return Path.Combine(_outputPath, DiskSpace.EnginesFolder, app);
    }

    // Write an engine with the pinned marker and a runtime DLL with other bytes in AppLocalDependencies of each platform.
    private string WriteEngine(string app)
    {
        string engine = EnginePath(app);
        _ = Directory.CreateDirectory(engine);
        EngineMarker.Write(engine, EmbeddedReleases<UnrealRelease>.Find(app).Sha1);

        string dependencies = Path.Combine(engine, "Engine", "Binaries", "ThirdParty", "AppLocalDependencies");
        FakeExecutable.Write(Path.Combine(dependencies, "Win64", "Misc", "runtime.dll"), "KERNEL32.dll");
        FakeExecutable.Write(Path.Combine(dependencies, "Win32", "Misc", "runtime.dll"));

        return engine;
    }

    private Unreal NewUnreal(FakeBuildHost? buildHost = null, string? buildDirectory = null)
    {
        UnrealEngine engine = new(_output)
        {
            UvPath = Path.Combine(_folder.Root, "missing.exe"),
        };

        buildHost ??= new FakeBuildHost(step => RunFakeStep(step));

        return new Unreal(engine, new LocalPlayerHost(), buildHost, buildDirectory ?? _buildDirectory);
    }
}
