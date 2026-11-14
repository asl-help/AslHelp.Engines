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

public sealed class UnrealTests
{
    // Set FIXTURES_UNREAL_BUILD to a pinned version and FIXTURES_UNREAL_BUILD_OUTPUT to a folder with that engine in
    // engines/ and the build image in Docker to build every player of that version for real.
    private const string BuildVariable = "FIXTURES_UNREAL_BUILD";
    private const string OutputVariable = "FIXTURES_UNREAL_BUILD_OUTPUT";

    private static readonly string _buildDirectory = Path.Combine(AppContext.BaseDirectory, "build", "unreal4");
    private static readonly string _projectDirectory = Path.Combine(AppContext.BaseDirectory, "projects", "unreal4");

    private TestFolder _folder = null!;
    private StringWriter _output = null!;
    private string _outputPath = null!;

    [SetUp]
    public void SetUp()
    {
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

    // Without options the ids are every player of the row in row order, with the pinned version in front.
    [TestCase("4.19", new[] { "4.19.2-win-x64-shipping", "4.19.2-win-x64-development", "4.19.2-win-x86-shipping", "4.19.2-win-x86-development" })]
    [TestCase("4.0", new[] { "4.0.2-win-x64-development", "4.0.2-win-x86-shipping" })]
    public void ListPlayers_NoOptions_ListsEveryRowPlayerInOrder(string version, string[] expected)
    {
        var unreal = NewUnreal();

        Assert.That(unreal.ListPlayers(version, Parse(unreal)), Is.EqualTo(expected));
    }

    // --arch and --config leave out the players that don't fit, also down to none, which the command line then refuses.
    [TestCase("4.19", "x86", "shipping", new[] { "4.19.2-win-x86-shipping" })]
    [TestCase("4.19", "x64", null, new[] { "4.19.2-win-x64-shipping", "4.19.2-win-x64-development" })]
    [TestCase("4.0", "x86", "development", new string[0])]
    public void ListPlayers_ArchAndConfig_NarrowTheList(string version, string? arch, string? config, string[] expected)
    {
        var unreal = NewUnreal();
        string[] args = [.. arch is null ? [] : new[] { "--arch", arch }, .. config is null ? [] : new[] { "--config", config }];

        Assert.That(unreal.ListPlayers(version, Parse(unreal, args)), Is.EqualTo(expected));
    }

    // A casing variant lists the pinned spelling, so the key and the folders never use the typed spelling.
    [Test]
    public void ListPlayers_CasingVariant_ReturnsThePinnedSpelling()
    {
        var unreal = NewUnreal();

        Assert.That(unreal.ListPlayers("ue_4.19", Parse(unreal))[0], Is.EqualTo("4.19.2-win-x64-shipping"));
    }

    // The key has the player id, the manifest sha1, the image tag, the project as it renders for 4.19, the step scripts, the row
    // switches, the PATH folder, the runtime source and build-version 1 and nothing else, so a change to any of them shows here first.
    [Test]
    public void AddKeyInputs_PinnedPlayer_AddsEveryBuildInput()
    {
        var expected = new CacheKeyBuilder("player")
            .Add("version", "4.19.2-win-x64-shipping")
            .Add("manifest-sha1", "28a1be22262c06e6735c6e0f85ffcb2a8a40e181")
            .Add("image", UnrealImage.Tag(Path.Combine(_buildDirectory, "image")))
            .AddFiles("project", UnrealFixtureSource.RenderFiles(_projectDirectory, new Version(4, 19)))
            .AddDirectory("steps", Path.Combine(_buildDirectory, "steps"))
            .Add("editor-flags", "-waitmutex")
            .Add("package-flags", "")
            .Add("path-prepend", @"C:\VS2017\VC\Tools\MSVC\14.16.27023\bin\HostX64\x64")
            .Add("runtimes", "engine")
            .Add("build-version", "1")
            .Build();

        Assert.That(KeyOf(NewUnreal(), "4.19.2-win-x64-shipping"), Is.EqualTo(expected));
    }

    // The project input is the hash of the folder that Render writes for the version, so the key covers that folder and nothing else.
    [Test]
    public void AddKeyInputs_PinnedPlayer_HashesTheRenderedProject()
    {
        string rendered = Path.Combine(_folder.NewFolder(), "project");
        UnrealFixtureSource.Render(_projectDirectory, rendered, new Version(4, 19));

        var expected = new CacheKeyBuilder("player").AddDirectory("project", rendered);
        _ = expected.Build();

        Assert.That(ProjectInputOf(NewUnreal(), "4.19.2-win-x64-shipping", _projectDirectory), Is.EqualTo(expected.Inputs[0].Value));
    }

    // 2 versions whose projects render to the same bytes get the same project input and a version that renders other bytes doesn't.
    [Test]
    public void AddKeyInputs_VersionsWithEqualRenders_GiveEqualProjectInputs()
    {
        string project = _folder.NewFolder();
        WriteBytes(project, "Source/Fixture.h", "//@if >=4.14\nA\n//@endif\nB\n"u8.ToArray());
        var unreal = NewUnreal();

        string first = ProjectInputOf(unreal, "4.19.2-win-x64-shipping", project);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProjectInputOf(unreal, "4.20.3-win-x64-shipping", project), Is.EqualTo(first));
            Assert.That(ProjectInputOf(unreal, "4.4.3-win-x64-development", project), Is.Not.EqualTo(first));
        }
    }

    // A change behind a gate that a version drops keeps that version's key, so an edit for 1 version doesn't build the others again.
    [Test]
    public void AddKeyInputs_ChangeBehindGateTheVersionDrops_KeepsItsKey()
    {
        string project = _folder.NewFolder();
        WriteBytes(project, "Source/Fixture.h", "//@if >=4.20\nA\n//@endif\nB\n"u8.ToArray());
        var unreal = NewUnreal();
        var dropping = KeyOf(unreal, "4.19.2-win-x64-shipping", project);
        var keeping = KeyOf(unreal, "4.20.3-win-x64-shipping", project);

        WriteBytes(project, "Source/Fixture.h", "//@if >=4.20\nC\n//@endif\nB\n"u8.ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(KeyOf(unreal, "4.19.2-win-x64-shipping", project), Is.EqualTo(dropping));
            Assert.That(KeyOf(unreal, "4.20.3-win-x64-shipping", project), Is.Not.EqualTo(keeping));
        }
    }

    // 2 players of 1 version never share a key, because each is its own build.
    [Test]
    public void AddKeyInputs_OtherPlayerOfSameVersion_GivesOtherKey()
    {
        var unreal = NewUnreal();

        Assert.That(KeyOf(unreal, "4.19.2-win-x64-shipping"), Is.Not.EqualTo(KeyOf(unreal, "4.19.2-win-x86-shipping")));
    }

    // 1 changed byte in the image folder changes the tag and the key, so a new image builds every player again.
    [Test]
    public void AddKeyInputs_ImageFileChanged_GivesOtherKey()
    {
        string copy = CopyBuildDirectory();
        string script = Path.Combine(copy, "image", "cleanup.ps1");
        var before = KeyOf(NewUnreal(buildDirectory: copy), "4.19.2-win-x64-shipping");

        File.AppendAllText(script, " ");

        Assert.That(KeyOf(NewUnreal(buildDirectory: copy), "4.19.2-win-x64-shipping"), Is.Not.EqualTo(before));
    }

    // An unpinned version throws with the pinned minors before any add, so the key fails before the store opens.
    [Test]
    public void AddKeyInputs_UnpinnedVersion_ThrowsAndAddsNothing()
    {
        CacheKeyBuilder builder = new("player");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => NewUnreal().AddKeyInputs(builder, "9.9-win-x64-shipping", _projectDirectory),
                Throws.InvalidOperationException.With.Message.Contains("'9.9'").And.Message.Contains("4.0, 4.1"));
            Assert.That(builder.Build(), Is.EqualTo(new CacheKeyBuilder("player").Build()));
        }
    }

    // An engine with the pinned sha1 in its marker returns its folder without starting uv.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_EngineWithMarker_SkipsTheToolAndReturnsTheEngineFolder()
    {
        FakeUv uv = new(_folder.NewFolder(), 99);
        string engine = WriteEngine("UE_4.19");

        string installed = await NewUnreal(uv: uv).InstallAsync("4.19.2-win-x64-shipping", _outputPath, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(installed, Is.EqualTo(engine));
            Assert.That(uv.Ran, Is.False);
        }
    }

    // A missing image fails with its docker build command before any step and before the scratch folder exists.
    [Test]
    public void BuildAsync_ImageMissing_ThrowsWithTheBuildCommandBeforeAnyStep()
    {
        FakeBuildHost buildHost = new(_ => 0) { HasImage = false };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"),
                Throws.InvalidOperationException.With.Message.Contains("docker build -t asl-help/unreal4-build:")
                    .And.Message.Contains($" \"{Path.Combine(_buildDirectory, "image")}\""));
            Assert.That(buildHost.Steps, Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(_outputPath, "unreal4-4.19.2-build")), Is.False);
        }
    }

    // The editor target builds first, then the 1st player cooks and every later player reuses the cooked content.
    [Test]
    public async Task BuildAsync_FourPlayers_CooksOnceThenSkipsTheCook()
    {
        var buildHost = NewBuildHost();

        _ = await BuildAsync(
            buildHost,
            "UE_4.19",
            "4.19.2-win-x64-shipping",
            "4.19.2-win-x64-development",
            "4.19.2-win-x86-shipping",
            "4.19.2-win-x86-development");

        Assert.That(
            buildHost.Steps.Select(step => step.Command[^1].Split(" > ")[0]),
            Is.EqualTo(
            [
                @"C:\r\editor.bat",
                @"C:\r\package.bat Win64 Shipping win-x64-shipping -cook",
                @"C:\r\package.bat Win64 Development win-x64-development -skipcook",
                @"C:\r\package.bat Win32 Shipping win-x86-shipping -skipcook",
                @"C:\r\package.bat Win32 Development win-x86-development -skipcook",
            ]));
    }

    // 4.0 copies the runtime DLLs out of the image once before the editor, because its install doesn't have AppLocalDependencies.
    [Test]
    public async Task BuildAsync_ImageRuntimesRow_RunsTheRuntimesStepFirst()
    {
        var buildHost = NewBuildHost();

        _ = await BuildAsync(buildHost, "UE_4.0", "4.0.2-win-x64-development", "4.0.2-win-x86-shipping");

        Assert.That(
            buildHost.Steps.Select(step => step.Command[^1].Split(' ')[0]),
            Is.EqualTo([@"C:\r\runtimes.bat", @"C:\r\editor.bat", @"C:\r\package.bat", @"C:\r\package.bat"]));
    }

    // The build host grants this user the scratch and the engine before the 1st container, so the build can delete what containers write.
    [Test]
    public async Task BuildAsync_BeforeFirstStep_GrantsTheCurrentUserOnScratchAndEngine()
    {
        string engine = WriteEngine("UE_4.19");
        string scratch = Path.Combine(_outputPath, "unreal4-4.19.2-build");
        var buildHost = NewBuildHost();

        _ = await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping");

        Assert.That(buildHost.GrantedFolders, Is.EqualTo([(scratch, 0), (engine, 0)]));
    }

    // A 2nd build deletes the scratch of a failed 1st and renders the project again, so a file left from an earlier run never builds.
    // The fake steps write as this user, so this doesn't prove the delete of files a container wrote. Only a 2nd real build does.
    [Test]
    public async Task BuildAsync_SecondRun_DeletesTheScratchAndRendersAgain()
    {
        string project = Path.Combine(_outputPath, "unreal4-4.19.2-build", "project");
        string stale = Path.Combine(project, "Source", "Fixture", "Stale.h");
        FakeBuildHost failing = new(step => step.Command[^1].Contains("package.bat", StringComparison.Ordinal) ? 1 : RunFakeStep(step));
        _ = Assert.ThrowsAsync<InvalidOperationException>(() => BuildAsync(failing, "UE_4.19", "4.19.2-win-x64-shipping"));
        WriteBytes(Path.GetDirectoryName(stale)!, "Stale.h", [1]);
        bool[]? seen = null;

        var players = await BuildAsync(
            NewBuildHost(_ => seen ??= [File.Exists(stale), File.Exists(Path.Combine(project, "Fixture.uproject"))]),
            "UE_4.19",
            "4.19.2-win-x64-shipping");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen, Is.EqualTo([false, true]));
            Assert.That(players, Has.Count.EqualTo(1));
        }
    }

    // Each player folder has the archive, the manifest with the version and the tag, the step logs and the DLLs its executable imports.
    [Test]
    public async Task BuildAsync_Players_GetArchiveManifestLogsAndRuntimes()
    {
        var players = await BuildAsync(NewBuildHost(), "UE_4.19", "4.19.2-win-x64-shipping", "4.19.2-win-x86-development");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                players,
                Is.EqualTo(
                [
                    Path.Combine(_outputPath, "unreal4-4.19.2-build", "players", "win-x64-shipping"),
                    Path.Combine(_outputPath, "unreal4-4.19.2-build", "players", "win-x86-development"),
                ]));

            string binaries = Path.Combine(players[1], "Fixture", "Binaries", "Win32");
            string manifest = await File.ReadAllTextAsync(Path.Combine(players[1], "fixture.json"));
            Assert.That(File.Exists(Path.Combine(binaries, "Fixture.exe")), Is.True);
            Assert.That(File.Exists(Path.Combine(binaries, "Fixture.pdb")), Is.True);
            Assert.That(File.Exists(Path.Combine(binaries, "runtime.dll")), Is.True);
            Assert.That(manifest, Does.Contain("\"engineVersion\": \"4.19.2\"").And.Contains("\"player\": \"win-x86-development\""));
            Assert.That(File.Exists(Path.Combine(players[1], "logs", "editor.log")), Is.True);
            Assert.That(File.Exists(Path.Combine(players[1], "logs", "package-win-x86-development.log")), Is.True);
        }
    }

    // 4.0 players get the DLLs of their arch from the runtimes the image step copied out.
    [Test]
    public async Task BuildAsync_ImageRuntimesRow_CopiesFromTheRuntimesStep()
    {
        byte[]? x86 = null;

        // Read the DLL right after the step, because the build deletes the archives folder at the end.
        FakeBuildHost buildHost = new(step =>
        {
            int? code = RunFakeStep(step);

            if (step.Command[^1].StartsWith(@"C:\r\runtimes.bat", StringComparison.Ordinal))
            {
                x86 = File.ReadAllBytes(Path.Combine(HostPath(step, @"C:\o"), "runtimes", "x86", "runtime.dll"));
            }

            return code;
        });

        var players = await BuildAsync(buildHost, "UE_4.0", "4.0.2-win-x86-shipping");

        string runtime = Path.Combine(players[0], "Fixture", "Binaries", "Win32", "runtime.dll");
        Assert.That(await File.ReadAllBytesAsync(runtime), Is.EqualTo(x86));
    }

    // A build that makes every player deletes the project and the archives and keeps the players and the logs.
    [Test]
    public async Task BuildAsync_EveryPlayerMade_DeletesTheProjectAndTheArchives()
    {
        string scratch = Path.Combine(_outputPath, "unreal4-4.19.2-build");

        var players = await BuildAsync(NewBuildHost(), "UE_4.19", "4.19.2-win-x64-shipping");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(scratch, "project")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(scratch, "out")), Is.False);
            Assert.That(File.Exists(Path.Combine(players[0], "fixture.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(scratch, "logs", "editor.log")), Is.True);
        }
    }

    // Read-only files in the scratch of an earlier build, in the project and in the archives don't stop the deletes, because the
    // engine build tools mark some of their files read-only.
    [Test]
    public async Task BuildAsync_ReadOnlyFiles_DeletesTheScratchTheProjectAndTheArchives()
    {
        string scratch = Path.Combine(_outputPath, "unreal4-4.19.2-build");
        string stale = WriteReadOnly(Path.Combine(scratch, "stale"));
        var buildHost = NewBuildHost(step =>
        {
            _ = WriteReadOnly(HostPath(step, UnrealPaths.ProjectMount));
            _ = WriteReadOnly(HostPath(step, UnrealPaths.OutMount));
        });

        _ = await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(stale), Is.False);
            Assert.That(Directory.Exists(Path.Combine(scratch, "project")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(scratch, "out")), Is.False);
        }
    }

    // A failed step keeps the project, so a person can look at what the step saw.
    [Test]
    public void BuildAsync_StepFails_KeepsTheProject()
    {
        FakeBuildHost buildHost = new(step => step.Command[^1].Contains("package.bat", StringComparison.Ordinal) ? 1 : RunFakeStep(step));

        Assert.That(async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"), Throws.InvalidOperationException);
        Assert.That(File.Exists(Path.Combine(_outputPath, "unreal4-4.19.2-build", "project", "Fixture.uproject")), Is.True);
    }

    // A package step that archives nothing fails with the out folder of the player and its log.
    [Test]
    public void BuildAsync_ArchiveMissing_ThrowsWithTheOutFolder()
    {
        FakeBuildHost buildHost = new(step => step.Command[^1].Contains("package.bat", StringComparison.Ordinal) ? 0 : RunFakeStep(step));

        Assert.That(
            async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"),
            Throws.InvalidOperationException
                .With.Message.Contains(Path.Combine(_outputPath, "unreal4-4.19.2-build", "out", "win-x64-shipping"))
                .And.Message.Contains("package-win-x64-shipping.log"));
    }

    // A failed step stops the build, so no later player builds on a broken editor target.
    [Test]
    public void BuildAsync_EditorFails_StartsNoPackageStep()
    {
        FakeBuildHost buildHost = new(step => step.Command[^1].Contains("editor.bat", StringComparison.Ordinal) ? 6 : RunFakeStep(step));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await BuildAsync(buildHost, "UE_4.19", "4.19.2-win-x64-shipping"),
                Throws.InvalidOperationException.With.Message.Contains("editor").And.Message.Contains("code 6"));
            Assert.That(buildHost.Steps, Has.Count.EqualTo(1));
        }
    }

    // The check reads the player and the version from fixture.json, so it starts the executable of that platform and
    // configuration whatever the folder name is. ping.exe rejects the arguments.
    [Test]
    [Platform("Win")]
    public void CheckAsync_PlayerFolder_StartsTheExecutableOfItsManifest()
    {
        string player = _folder.NewFolder();
        string executable = Path.Combine(player, "Fixture", "Binaries", "Win32", "Fixture-Win32-Shipping.exe");
        new UnrealFixtureManifest("4.19.2", "win-x86-shipping", UnrealConfig.Shipping, [], []).Write(player);
        WriteBytes(player, Path.GetRelativePath(player, executable), File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "PING.EXE")));

        Assert.That(
            async () => await NewUnreal().CheckAsync(player, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains(executable));
    }

    // An unpinned version fails at the key with the pinned minors, before the store opens and before the install.
    [Test]
    public async Task RunAsync_UnpinnedVersion_FailsBeforeTheStoreAndTheInstall()
    {
        using StringWriter error = new();
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "unreal4", "9.9", "--output", _outputPath],
            _output,
            error,
            [NewUnreal()],
            _ =>
            {
                opened = true;
                return null;
            },
            Path.Combine(AppContext.BaseDirectory, "projects"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("'9.9'").And.Contains("4.0, 4.1"));
            Assert.That(opened, Is.False);
            Assert.That(Directory.Exists(Path.Combine(_outputPath, "engines")), Is.False);
        }
    }

    // The help of the subcommand lists both options with their values.
    [Test]
    public async Task RunAsync_Help_ListsArchAndConfig()
    {
        using StringWriter error = new();

        _ = await CommandLine.RunAsync(["build", "unreal4", "--help"], _output, error, [NewUnreal()], _ => null, _projectDirectory);

        Assert.That(_output.ToString(), Does.Contain("--arch").And.Contains("x64").And.Contains("x86")
            .And.Contains("--config").And.Contains("shipping").And.Contains("development"));
    }

    // A real engine and the build image build every player of the version, each with its manifest and its executable.
    [Test]
    public async Task BuildAsync_RealEngine_BuildsEveryPlayer()
    {
        string? version = Environment.GetEnvironmentVariable(BuildVariable);
        string? output = Environment.GetEnvironmentVariable(OutputVariable);
        Assume.That(version is not null && output is not null, $"Set {BuildVariable} and {OutputVariable} to run this test.");

        using ContainerHost host = new();
        using StringWriter error = new();
        Unreal unreal = new(new UnrealEngine(_output), host, host, _buildDirectory);
        int expected = unreal.ListPlayers(version!, Parse(unreal)).Count;

        int code = await CommandLine.RunAsync(
            ["build", "unreal4", version!, "--output", output!],
            _output,
            error,
            [unreal],
            _ => null,
            Path.Combine(AppContext.BaseDirectory, "projects"));

        string[] folders = [.. _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).TakeLast(expected)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());

            foreach (string folder in folders)
            {
                Assert.That(File.Exists(Path.Combine(folder, "fixture.json")), Is.True, folder);
                string[] executables = Directory.GetFiles(Path.Combine(folder, "Fixture", "Binaries"), "*.exe", SearchOption.AllDirectories);
                Assert.That(executables, Has.Length.EqualTo(1), folder);
            }
        }
    }

    // Write what each step writes for real: its log, the runtimes per arch or the archive of a player with its executable and PDB.
    private static int? RunFakeStep(BuildStep step)
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
            string archive = Path.Combine(HostPath(step, @"C:\o"), words[3], "WindowsNoEditor");
            var player = UnrealPlayer.Parse(words[3]);

            // The scratch folder is unreal4-<version>-build, so its 2nd part is the version of the build.
            var version = Version.Parse(Path.GetFileName(Path.GetDirectoryName(HostPath(step, @"C:\o")))!.Split('-')[1]);
            FakeExecutable.Write(player.ExecutablePath(archive, version), "runtime.dll", "KERNEL32.dll");
            WriteBytes(archive, Path.GetRelativePath(archive, player.PdbPath(archive, version)), [1]);
        }

        return 0;
    }

    private static string HostPath(BuildStep step, string containerPath)
    {
        return step.Mounts.Single(mount => mount.ContainerPath == containerPath).HostPath;
    }

    // Write 1 read-only file into the folder, once, because a step runs the callback per step.
    private static string WriteReadOnly(string directory)
    {
        string path = Path.Combine(directory, "read-only.bin");

        if (!File.Exists(path))
        {
            WriteBytes(directory, "read-only.bin", [1]);
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }

        return path;
    }

    private static CacheKey KeyOf(Unreal unreal, string player, string? projectDirectory = null)
    {
        CacheKeyBuilder builder = new("player");
        _ = unreal.AddKeyInputs(builder, player, projectDirectory ?? _projectDirectory);

        return builder.Build();
    }

    private static string ProjectInputOf(Unreal unreal, string player, string projectDirectory)
    {
        CacheKeyBuilder builder = new("player");
        _ = unreal.AddKeyInputs(builder, player, projectDirectory);
        _ = builder.Build();

        return builder.Inputs.Single(input => input.Name == "project").Value;
    }

    private static ParseResult Parse(Unreal unreal, params string[] args)
    {
        return new RootCommand { Options = { unreal.Options[0], unreal.Options[1] } }.Parse(args);
    }

    private static FakeBuildHost NewBuildHost(Action<BuildStep>? before = null)
    {
        return new FakeBuildHost(step =>
        {
            before?.Invoke(step);

            return RunFakeStep(step);
        });
    }

    private Task<IReadOnlyList<string>> BuildAsync(FakeBuildHost buildHost, string app, params string[] players)
    {
        string engine = Path.Combine(_outputPath, UnrealEngine.EnginesFolder, app);

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

    // Write an engine with the pinned marker and the runtime DLL in AppLocalDependencies of both platforms.
    private string WriteEngine(string app)
    {
        string engine = Path.Combine(_outputPath, UnrealEngine.EnginesFolder, app);
        var release = EmbeddedReleases<UnrealRelease>.Find(app);
        _ = Directory.CreateDirectory(engine);
        EngineMarker.Write(engine, release.Sha1);

        foreach (string platform in new[] { "Win64", "Win32" })
        {
            FakeExecutable.Write(Path.Combine(engine, "Engine", "Binaries", "ThirdParty", "AppLocalDependencies", platform, "Misc", "runtime.dll"));
        }

        return engine;
    }

    private Unreal NewUnreal(FakeBuildHost? buildHost = null, FakeUv? uv = null, string? buildDirectory = null)
    {
        UnrealEngine engine = new(_output)
        {
            UvPath = uv?.FilePath ?? Path.Combine(_folder.Root, "missing.exe"),
            FreeBytes = _ => long.MaxValue,
        };

        return new Unreal(engine, new LocalPlayerHost(), buildHost ?? new FakeBuildHost(RunFakeStep), buildDirectory ?? _buildDirectory);
    }
}
