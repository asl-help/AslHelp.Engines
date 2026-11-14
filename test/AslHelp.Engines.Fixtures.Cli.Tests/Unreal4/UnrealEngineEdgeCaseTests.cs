using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealEngineEdgeCaseTests
{
    private const string OtherSha1 = "0000000000000000000000000000000000000001";

    private static readonly UnrealRelease _release = EmbeddedReleases<UnrealRelease>.Find("4.19");

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
        _enginesPath = Path.Combine(_outputPath, UnrealEngine.EnginesFolder);
        _enginePath = Path.Combine(_enginesPath, _release.App);
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _folder.Delete();
    }

    // A canceled call doesn't delete anything or start the tool, because a delete can't be undone and costs a whole download.
    [Test]
    [Platform("Win")]
    public void InstallAsync_AlreadyCanceled_DeletesNothingAndSkipsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        _ = NewFinishedEngine(_release.App, OtherSha1);
        string sibling = NewFinishedEngine("sibling", OtherSha1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.That(
            async () => await NewEngine(uv, _ => 0).InstallAsync(_release, _outputPath, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(OtherSha1));
            Assert.That(EngineMarker.Read(sibling), Is.EqualTo(OtherSha1));
            Assert.That(uv.Ran, Is.False);
        }
    }

    // A second run after a clean run doesn't start the tool, because the clean run wrote the marker.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_SecondRunAfterACleanOne_SkipsTheTool()
    {
        FakeUv first = new(_folder.NewFolder(), 0, $"mkdir \"{_enginePath}\"");
        FakeUv second = new(_folder.NewFolder(), 99);

        _ = await NewEngine(first).InstallAsync(_release, _outputPath, CancellationToken.None);
        _ = await NewEngine(second).InstallAsync(_release, _outputPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Ran, Is.True);
            Assert.That(second.Ran, Is.False);
        }
    }

    // A failed run keeps its files and doesn't write a marker, so the next run starts the tool again in the same folder and only that one finishes.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_FailedRunThenCleanRun_ResumesAndWritesTheMarkerOnce()
    {
        string file = Path.Combine(_enginePath, "file.bin");
        FakeUv failing = new(_folder.NewFolder(), 7, $"mkdir \"{_enginePath}\"", $"echo part> \"{file}\"");
        FakeUv clean = new(_folder.NewFolder(), 0);

        await Assert.ThatAsync(
            () => NewEngine(failing).InstallAsync(_release, _outputPath, CancellationToken.None),
            Throws.InvalidOperationException);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(file, Does.Exist);
            Assert.That(EngineMarker.Read(_enginePath), Is.Null);
        }

        _ = await NewEngine(clean).InstallAsync(_release, _outputPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clean.Ran, Is.True);
            Assert.That(file, Does.Exist);
            Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(_release.Sha1));
        }
    }

    // An installed engine on a short drive removes an older engine and returns without starting the tool. The step line has the removed path.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_MarkerWithPinnedSha1AndShortDrive_RemovesAnotherEngineAndSkipsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 99);
        _ = NewFinishedEngine(_release.App, _release.Sha1);
        File.SetLastWriteTimeUtc(Path.Combine(_enginePath, EngineMarker.FileName), DateTime.UtcNow.AddDays(-9));
        string sibling = NewFinishedEngine("sibling", OtherSha1);
        var engine = NewEngine(uv, _ => Directory.Exists(sibling) ? EngineSpace.ScratchBytes - 1 : EngineSpace.ScratchBytes);

        string installed = await engine.InstallAsync(_release, _outputPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(installed, Is.EqualTo(_enginePath));
            Assert.That(sibling, Does.Not.Exist);
            Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(_release.Sha1));
            Assert.That(_output.ToString(), Does.Contain($"Removed {sibling}"));
            Assert.That(uv.Ran, Is.False);
        }
    }

    // The scratch of a failed build of another version goes before an engine. The scratch of this version stays for its build.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_StaleScratchOfOtherVersionAndShortDrive_RemovesItBeforeAnyEngine()
    {
        FakeUv uv = new(_folder.NewFolder(), 99);
        _ = NewFinishedEngine(_release.App, _release.Sha1);
        string sibling = NewFinishedEngine("sibling", OtherSha1);
        string stale = Path.Combine(_outputPath, "unreal4-0.0.0-build");
        string own = Path.Combine(_outputPath, $"unreal4-{_release.Version}-build");
        TestFolder.WriteBytes(stale, "out.bin", [1]);
        TestFolder.WriteBytes(own, "out.bin", [1]);
        var engine = NewEngine(uv, _ => Directory.Exists(stale) ? EngineSpace.ScratchBytes - 1 : EngineSpace.ScratchBytes);

        _ = await engine.InstallAsync(_release, _outputPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stale, Does.Not.Exist);
            Assert.That(own, Does.Exist);
            Assert.That(sibling, Does.Exist);
            Assert.That(_output.ToString(), Does.Contain($"Removed {stale}"));
        }
    }

    // The space of an old pin counts before any other engine goes, so its delete comes before the check.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_MarkerWithOtherSha1_ChecksTheSpaceAfterItsDelete()
    {
        FakeUv uv = new(_folder.NewFolder(), 0, $"mkdir \"{_enginePath}\"");
        _ = NewFinishedEngine(_release.App, OtherSha1);
        string sibling = NewFinishedEngine("sibling", OtherSha1);
        var engine = NewEngine(uv, _ => Directory.Exists(_enginePath) ? 0 : long.MaxValue);

        _ = await engine.InstallAsync(_release, _outputPath, CancellationToken.None);

        Assert.That(sibling, Does.Exist);
    }

    // An empty marker from a crash during its write counts as another pin, so the folder goes and the tool runs.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_EmptyMarker_DeletesTheFolderAndRunsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 0, $"mkdir \"{_enginePath}\"");
        _ = NewFinishedEngine(_release.App, string.Empty);
        string file = Path.Combine(_enginePath, "file.bin");
        await File.WriteAllTextAsync(file, "text");

        _ = await NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(uv.Ran, Is.True);
            Assert.That(file, Does.Not.Exist);
            Assert.That(EngineMarker.Read(_enginePath), Is.EqualTo(_release.Sha1));
        }
    }

    // An old pin with a file in use stops the install before the tool starts, because that file of the old build would stay in the new install.
    [Test]
    [Platform("Win")]
    public void InstallAsync_MarkerWithOtherSha1AndFileInUse_ThrowsAndSkipsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        _ = NewFinishedEngine(_release.App, OtherSha1);

        using (File.Open(Path.Combine(_enginePath, "file.bin"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.That(
                async () => await NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None),
                Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        }

        Assert.That(uv.Ran, Is.False);
    }

    // An output path that is a file fails before the tool starts.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_OutputIsAFile_ThrowsAndSkipsTheTool()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        string file = Path.Combine(_outputPath, "file.bin");
        await File.WriteAllTextAsync(file, "text");

        await Assert.ThatAsync(
            () => NewEngine(uv).InstallAsync(_release, file, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        Assert.That(uv.Ran, Is.False);
    }

    // Any spelling of the output folder ends up with the same engine folder and each path reaches the tool as 1 argument.
    [TestCase("my output")]
    [TestCase("my output\\")]
    [TestCase("my output/")]
    [TestCase("folder\\..\\my output")]
    [Platform("Win")]
    public async Task InstallAsync_OutputSpelling_PassesTheCleanPaths(string spelling)
    {
        string root = _folder.NewFolder();
        string engines = Path.Combine(root, "my output", UnrealEngine.EnginesFolder);
        string engine = Path.Combine(engines, _release.App);
        FakeUv uv = new(_folder.NewFolder(), 0, $"mkdir \"{engine}\"");

        string installed = await NewEngine(uv).InstallAsync(_release, Path.Combine(root, spelling), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(installed, Is.EqualTo(engine));
            Assert.That(EngineMarker.Read(engine), Is.EqualTo(_release.Sha1));
            Assert.That(uv.Arguments, Does.EndWith($" \"{engines}\" 1 \"{engine}.manifest\""));
        }
    }

    // An output folder with chars that cmd or a format string treat as special and chars outside ASCII works like any other.
    [TestCase("100% done")]
    [TestCase("a^b(c)")]
    [TestCase("[x]{0}")]
    [TestCase("ünï-日本語")]
    [Platform("Win")]
    public async Task InstallAsync_OddOutputFolder_WritesTheMarkerThere(string name)
    {
        string output = Path.Combine(_folder.Root, name);
        string engine = Path.Combine(output, UnrealEngine.EnginesFolder, _release.App);
        _ = Directory.CreateDirectory(engine);
        FakeUv uv = new(_folder.NewFolder(), 0);

        string installed = await NewEngine(uv).InstallAsync(_release, output, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(uv.Ran, Is.True);
            Assert.That(installed, Is.EqualTo(engine));
            Assert.That(EngineMarker.Read(engine), Is.EqualTo(_release.Sha1));
        }
    }

    // The script prints UTF-8 on both streams, so a file path outside ASCII shows unchanged in the step lines and in the message.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolPrintsUtf8_DecodesBothStreams()
    {
        string folder = _folder.NewFolder();
        string outPath = Path.Combine(folder, "out.txt");
        string errPath = Path.Combine(folder, "err.txt");
        await File.WriteAllTextAsync(outPath, "Café/日本語.bin\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await File.WriteAllTextAsync(errPath, "Ünï/ключ.bin\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        FakeUv uv = new(folder, 7, $"type \"{outPath}\"", $"type \"{errPath}\" 1>&2");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("Café/日本語.bin").And.Contain("Ünï/ключ.bin"));
            Assert.That(_output.ToString(), Does.Contain("Café/日本語.bin").And.Contain("Ünï/ключ.bin"));
        }
    }

    // Braces in the output of the script stay as they are in every message, because a Python traceback can have them.
    [TestCase(7)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(13)]
    [Platform("Win")]
    public async Task InstallAsync_ToolPrintsBraces_KeepsThemInTheMessage(int code)
    {
        FakeUv uv = new(_folder.NewFolder(), code, "echo {0} {bad} {");

        var exception = await Assert.ThrowsAsync(
            Is.InstanceOf<Exception>(),
            () => NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("{0} {bad} {"));
    }

    // A login failure without output ends the message after the fix, so the error doesn't end in a blank line.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolExits10WithoutOutput_EndsTheMessageAfterTheFix()
    {
        FakeUv uv = new(_folder.NewFolder(), 10);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("Legendary isn't logged in. Run 'just legendary auth'."));
    }

    // A crashed tool shows its code as a signed number, as NTSTATUS codes come back from the process.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolExitsNegativeCode_ThrowsWithTheCode()
    {
        FakeUv uv = new(_folder.NewFolder(), -1073741819);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("UE_4.19").And.Contain("code -1073741819"));
    }

    // The folder of the script goes after a failed exit too, so a failed run leaves nothing in the temp folder.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_ToolExitsNonZero_DeletesTheScriptFolder()
    {
        FakeUv uv = new(_folder.NewFolder(), 7);

        await Assert.ThatAsync(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None),
            Throws.InvalidOperationException);

        Assert.That(Path.GetDirectoryName(uv.ScriptPath), Does.Not.Exist);
    }

    // A bare file name that isn't on PATH throws with the install command, as the default uv does on a machine without it.
    [Test]
    [Platform("Win")]
    public void InstallAsync_UvNotOnPath_ThrowsWithTheWingetCommand()
    {
        UnrealEngine engine = new(_output)
        {
            UvPath = "file-not-on-path",
            FreeBytes = _ => long.MaxValue,
        };

        Assert.That(
            async () => await engine.InstallAsync(_release, _outputPath, CancellationToken.None),
            Throws.TypeOf<FileNotFoundException>()
                .With.Message.Contains("'file-not-on-path'")
                .And.Message.Contains("winget install astral-sh.uv"));
    }

    // An engine that goes with its old pin takes its manifest along, because the next script run writes a new one only after the manifest check.
    [Test]
    [Platform("Win")]
    public async Task InstallAsync_MarkerWithOtherSha1_DeletesTheOldManifest()
    {
        FakeUv uv = new(_folder.NewFolder(), 7);
        _ = NewFinishedEngine(_release.App, OtherSha1);
        string manifest = EngineSpace.ManifestPath(_enginePath);
        await File.WriteAllTextAsync(manifest, "text");

        await Assert.ThatAsync(
            () => NewEngine(uv).InstallAsync(_release, _outputPath, CancellationToken.None),
            Throws.InvalidOperationException);

        Assert.That(manifest, Does.Not.Exist);
    }

    // The biggest pinned engine on a full drive fails with its own numbers, the install rounded up plus the scratch.
    [Test]
    [Platform("Win")]
    public void InstallAsync_BiggestRowOnAFullDrive_ThrowsWithItsNumbers()
    {
        FakeUv uv = new(_folder.NewFolder(), 0);
        string drive = Path.GetPathRoot(_outputPath)!;

        Assert.That(
            async () => await NewEngine(uv, _ => 0).InstallAsync(EmbeddedReleases<UnrealRelease>.Find("5.3"), _outputPath, CancellationToken.None),
            Throws.InstanceOf<IOException>().With.Message.EqualTo(
                $"Drive {drive} has 0 GiB free and the install needs 76 GiB. Free space on it or pass another --output folder."));
        Assert.That(uv.Ran, Is.False);
    }

    // The just recipe of the login and the tool use the same pins, because a login from another legendary might not read in the tool's legendary.
    [Test]
    public void UvArguments_Pins_MatchTheLegendaryRecipe()
    {
        string[] arguments = UnrealEngine.UvArguments("file.py");
        int python = Array.IndexOf(arguments, "python");

        string recipe = $"uv {string.Join(' ', arguments[..python])} legendary @args";

        Assert.That(File.ReadAllText(FindJustfile()), Does.Contain(recipe));
    }

    private static string FindJustfile()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "justfile");

            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("justfile", AppContext.BaseDirectory);
    }

    private UnrealEngine NewEngine(FakeUv uv)
    {
        return NewEngine(uv, _ => long.MaxValue);
    }

    private UnrealEngine NewEngine(FakeUv uv, Func<string, long> freeBytes)
    {
        return new UnrealEngine(_output)
        {
            UvPath = uv.FilePath,
            FreeBytes = freeBytes,
        };
    }

    private string NewFinishedEngine(string name, string sha1)
    {
        string engine = Path.Combine(_enginesPath, name);
        _ = Directory.CreateDirectory(engine);
        EngineMarker.Write(engine, sha1);

        return engine;
    }
}
