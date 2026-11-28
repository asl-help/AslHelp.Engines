using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealEngineTests
{
    private static readonly TimeSpan _cancelDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _exitTimeout = TimeSpan.FromSeconds(10);

    private static readonly UnrealRelease _release = EmbeddedReleases<UnrealRelease>.Find("4.19");

    private static readonly UnrealRelease _releaseWithoutTags = EmbeddedReleases<UnrealRelease>.Find("4.3");

    private string _enginePath = null!;
    private string _enginesPath = null!;
    private TestFolder _folder = null!;
    private StringWriter _output = null!;
    private string _outputPath = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _output = new StringWriter();
        _outputPath = _folder.NewFolder();
        _enginesPath = Path.Combine(_outputPath, DiskSpace.EnginesFolder);
        _enginePath = Path.Combine(_enginesPath, _release.App);
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _folder.Delete();
    }

    // An engine with the pinned sha1 in its marker doesn't start the tool and returns its folder.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_MarkerWithPinnedSha1_SkipsTheToolAndReturnsTheFolder()
    {
        FakeUv uv = new(_folder.NewFolder(), 99);
        NewFinishedEngine(_release.Sha1);

        string installed = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(installed, Is.EqualTo(_enginePath));
            Assert.That(uv.Ran, Is.False);
        }
    }

    // An installed engine still needs the cook scratch, so a build never starts on a full drive.
    [Test]
    [Platform("Win")]
    public void InstallAsync_MarkerWithPinnedSha1AndNoRoomForTheCook_ThrowsAndSkipsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        NewFinishedEngine(_release.Sha1);

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(DiskSpace.MarginBytes + UnrealEngine.CookBytes - 1), CancellationToken.None),
            Throws.InstanceOf<IOException>());

        Assert.That(uv.Ran, Is.False);
    }

    // The measure gets the row and the manifest path. The install starts once the drive has the measured bytes, the cook and the margin.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_NoFolder_MeasuresAndNeedsTheMeasuredBytes()
    {
        FakeUv uv = new(_folder.NewFolder(), 100, 0, $"if not exist \"{_enginePath}\" mkdir \"{_enginePath}\"");
        long needed = 100 + UnrealEngine.CookBytes + DiskSpace.MarginBytes;

        await Assert.ThatAsync(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, Room(needed - 1), CancellationToken.None),
            Throws.InstanceOf<IOException>());

        bool ranOnShortDrive = uv.Ran;
        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(needed), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ranOnShortDrive, Is.False);
            Assert.That(uv.Ran, Is.True);
            Assert.That(uv.MeasureArguments, Does.EndWith($"--measure {_release.App} {_release.Build} {_release.Sha1} 1 {_enginePath}.manifest"));
        }
    }

    // An interrupted install counts the bytes it already has, so a resume doesn't remove a finished engine for them. An old pin is gone before the measure.
    [TestCase(60, null, 40)]
    [TestCase(150, null, 0)]
    [TestCase(60, "0000000000000000000000000000000000000000", 100)]
    [Platform("Win")]
    public async Task InstallAsync_FolderWithOtherFiles_NeedsOnlyTheMissingBytes(int partBytes, string? sha1, int missingBytes)
    {
        FakeUv uv = new(_folder.NewFolder(), 100, 0, $"if not exist \"{_enginePath}\" mkdir \"{_enginePath}\"");
        _ = Directory.CreateDirectory(_enginePath);
        await File.WriteAllBytesAsync(Path.Combine(_enginePath, "file.bin"), new byte[partBytes]);

        if (sha1 is not null)
        {
            EngineMarker.Write(_enginePath, sha1);
        }

        long needed = missingBytes + UnrealEngine.CookBytes + DiskSpace.MarginBytes;

        await Assert.ThatAsync(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, Room(needed - 1), CancellationToken.None),
            Throws.InstanceOf<IOException>());

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(needed), CancellationToken.None);

        Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(_release.Sha1));
    }

    // A measure that doesn't print the size fails with the app before the install starts.
    [Test]
    [Platform("Win")]
    public void InstallAsync_MeasureWithoutTheSize_ThrowsWithTheAppAndSkipsTheInstall()
    {
        FakeUv uv = new(_folder.NewFolder(), null, 0);

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains(_release.App));

        Assert.That(uv.Ran, Is.False);
    }

    // A marker of another sha1 counts as not installed and the old files go, because files only the old build had would stay in the install.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_MarkerWithOtherSha1_DeletesTheFolderAndRunsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 0, $"mkdir \"{_enginePath}\"");
        NewFinishedEngine(new string('0', 40));
        string oldFile = Path.Combine(_enginePath, "file.bin");
        await File.WriteAllTextAsync(oldFile, "old");

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(uv.Ran, Is.True);
            Assert.That(oldFile, Does.Not.Exist);
            Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(_release.Sha1));
        }
    }

    // A folder without a marker keeps its files, because the tool resumes from them.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_FolderWithoutMarker_KeepsItsFilesForTheResume()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        _ = Directory.CreateDirectory(_enginePath);
        string partFile = Path.Combine(_enginePath, "file.bin");
        await File.WriteAllTextAsync(partFile, "part");

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        Assert.That(partFile, Does.Exist);
    }

    // A clean exit writes the marker and the tool gets the pinned legendary, the row and the paths.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolExitsZero_WritesTheMarkerAndPassesTheArguments()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        _ = Directory.CreateDirectory(_enginePath);

        string installed = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        string[] arguments =
        [
            "run --no-project --python 3.14 --with legendary-gl==0.21.1 python -I",
            uv.ScriptPath,
            _release.App,
            _release.Build,
            _release.Sha1,
            _enginesPath,
            "1",
            $"{_enginePath}.manifest",
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(installed, Is.EqualTo(_enginePath));
            Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(_release.Sha1));
            Assert.That(uv.Arguments, Is.EqualTo(string.Join(' ', arguments)));
        }
    }

    // A row whose manifest doesn't have install tags turns the tag filter off.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_RowWithoutInstallTags_PassesZero()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        string engine = Path.Combine(_enginesPath, _releaseWithoutTags.App);
        _ = Directory.CreateDirectory(engine);

        _ = await NewEngine(uv).InstallAsync(_releaseWithoutTags, _outputPath, Room(), CancellationToken.None);

        Assert.That(uv.Arguments, Does.EndWith($" {_enginesPath} 0 {engine}.manifest"));
    }

    // The script path in the arguments has the embedded script while the tool runs.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolRuns_FindsTheScriptAtItsPath()
    {
        string folder = _folder.NewFolder();
        string copy = Path.Combine(folder, "copy.py");
        string copyLine = $"for %%a in (%*) do @if \"%%~nxa\"==\"install_engine.py\" copy \"%%~a\" \"{copy}\" > nul";
        FakeUv uv = new(folder, 0, copyLine);
        _ = Directory.CreateDirectory(_enginePath);

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        Assert.That(await File.ReadAllTextAsync(copy), Does.Contain("--check-reader"));
    }

    // The tool runs from the temp root, so a child that ends late can't keep the script folder from its delete.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolRuns_RunsOutsideTheScriptFolder()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        _ = Directory.CreateDirectory(_enginePath);

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        Assert.That(Path.GetDirectoryName(uv.ScriptPath), Is.Not.SamePath(uv.WorkingDirectory));
    }

    // The folder of the script goes after a clean exit, so a run leaves nothing in the temp folder.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolExitsZero_DeletesTheScriptFolder()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        _ = Directory.CreateDirectory(_enginePath);

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        Assert.That(Path.GetDirectoryName(uv.ScriptPath), Does.Not.Exist);
    }

    // Every line of a clean run reaches the writer of the steps.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolPrintsLines_ReachTheWriter()
    {
        FakeUv uv = new(_folder.NewFolder(), 0, "echo download: 5 MiB/s", "echo text.bin");
        _ = Directory.CreateDirectory(_enginePath);

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None);

        Assert.That(_output.ToString(), Does.Contain("download: 5 MiB/s").And.Contain("text.bin"));
    }

    // Exit code 10 is a missing login, so the message has the command that fixes it and the reason the script printed.
    [Test]
    [Platform("Win")]
    public void InstallAsync_ToolExits10_ThrowsNotLoggedInWithTheAuthCommand()
    {
        FakeUv uv = new(_folder.NewFolder(), 10, "echo stored login expired");

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("just legendary auth")
                .And.Message.Contains("stored login expired"));
    }

    // Exit code 11 is a manifest that Epic changed, so both sha1 values reach the message and the install doesn't count as finished.
    [Test]
    [Platform("Win")]
    public void InstallAsync_ToolExits11_ThrowsWithBothSha1Values()
    {
        FakeUv uv = new(_folder.NewFolder(), 11, "echo sha1: pinned abc got def");
        _ = Directory.CreateDirectory(_enginePath);

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("abc").And.Message.Contains("def").And.Message.Contains("releases.json"));
        Assert.That(EngineMarker.Read(_enginePath), Is.Null);
    }

    // Exit code 12 is an app in the installed list of legendary, so the message has the command that takes it off that list and keeps its files.
    [Test]
    [Platform("Win")]
    public void InstallAsync_ToolExits12_ThrowsWithTheKeepFilesCommand()
    {
        FakeUv uv = new(_folder.NewFolder(), 12);

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("just legendary uninstall --keep-files UE_4.19"));
    }

    // Exit code 13 is a failed file hash check, so the bad files reach the message and the install doesn't count as finished.
    [Test]
    [Platform("Win")]
    public void InstallAsync_ToolExits13_ThrowsWithTheFilePaths()
    {
        FakeUv uv = new(_folder.NewFolder(), 13, "echo Engine/file.bin", "echo bad files: 1");
        _ = Directory.CreateDirectory(_enginePath);

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("Engine/file.bin").And.Message.Contains("bad files: 1"));
        Assert.That(EngineMarker.Read(_enginePath), Is.Null);
    }

    // Any other code throws with the code and the last lines, so a Python traceback reaches the message.
    [Test]
    [Platform("Win")]
    public void InstallAsync_ToolExitsOtherCode_ThrowsWithCodeAndTail()
    {
        FakeUv uv = new(_folder.NewFolder(), 7, "echo first", "echo last 1>&2");
        _ = Directory.CreateDirectory(_enginePath);

        Assert.That(
            async () => await NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("UE_4.19")
                .And.Message.Contains("code 7")
                .And.Message.Contains("first")
                .And.Message.Contains("last"));
        Assert.That(EngineMarker.Read(_enginePath), Is.Null);
    }

    // The message of a failed tool only has the last lines, so a long output doesn't flood the error.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolFailsAfterManyLines_KeepsOnlyTheLastLines()
    {
        FakeUv uv = new(_folder.NewFolder(), 7, "for /l %%i in (1,1,15) do @echo line%%i");
        _ = Directory.CreateDirectory(_enginePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, Room(), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("line15").And.Contain("line6").And.Not.Contain("line5"));
    }

    // A missing uv throws with the command that installs it and the start failure stays inner.
    [Test]
    [Platform("Win")]
    public void InstallAsync_UvMissing_ThrowsWithTheWingetCommand()
    {
        UnrealEngine engine = new(_output)
        {
            UvPath = Path.Combine(_folder.Root, "missing.exe"),
        };

        Assert.That(
            async () => await engine.InstallAsync(_release, _outputPath, Room(), CancellationToken.None),
            Throws.TypeOf<FileNotFoundException>()
                .With.Message.Contains("winget install astral-sh.uv")
                .And.InnerException.TypeOf<Win32Exception>());
    }

    // A cancel ends the tool and deletes the script folder. The install doesn't count as finished.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_Canceled_DeletesTheScriptFolder()
    {
        FakeUv uv = new(_folder.NewFolder(), 0, "ping -n 30 127.0.0.1 > nul");
        _ = Directory.CreateDirectory(_enginePath);
        using CancellationTokenSource cancellation = new(_cancelDelay);

        await Assert.ThatAsync(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, Room(), cancellation.Token).WaitAsync(_exitTimeout),
            Throws.InstanceOf<OperationCanceledException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Path.GetDirectoryName(uv.ScriptPath), Does.Not.Exist);
            Assert.That(EngineMarker.Read(_enginePath), Is.Null);
        }
    }

    // Make room on a drive with the given free space, which by default always has room.
    private DriveRoom Room(long freeBytes = long.MaxValue)
    {
        return new DriveRoom(_outputPath, _ => freeBytes, _output);
    }

    private UnrealEngine NewEngine(FakeUv uv)
    {
        return new UnrealEngine(_output)
        {
            UvPath = uv.FilePath,
        };
    }

    private void NewFinishedEngine(string sha1)
    {
        _ = Directory.CreateDirectory(_enginePath);
        EngineMarker.Write(_enginePath, sha1);
    }
}
