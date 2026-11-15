using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class InstallEngineScriptEdgeCaseTests
{
    private const string HarnessFile = "install_engine_harness.py";
    private const string ManifestSha1 = "bbce7a0495dff535cf75fc09bf7f58e1d22b3d44";
    private const string UvTestsVariable = "FIXTURES_UV_TESTS";

    private string _base = null!;
    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        Assume.That(Environment.GetEnvironmentVariable(UvTestsVariable), Is.EqualTo("1"), $"Set {UvTestsVariable} to 1 to run this test.");

        _folder = new TestFolder();

        // Run from a folder with a space, chars cmd treats as special and chars outside ASCII, because the script gets its paths on the command line.
        _base = Path.Combine(_folder.Root, "a b & 100% ünï 日本語");
        _ = Directory.CreateDirectory(_base);
    }

    [TearDown]
    public void TearDown()
    {
        _folder?.Delete();
    }

    // A login that comes back false exits 10 before any request.
    [Test]
    [Platform("Win")]
    public async Task Install_LoginFails_Exits10()
    {
        var (exitCode, lines) = await RunCaseAsync("login-false");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(10));
            Assert.That(lines, Does.Contain("manifest requests: 0"));
        }
    }

    // A missing stored login exits 10 and prints legendary's reason for the message.
    [Test]
    [Platform("Win")]
    public async Task Install_StoredLoginMissing_Exits10WithTheReason()
    {
        var (exitCode, lines) = await RunCaseAsync("login-missing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(10));
            Assert.That(lines, Does.Contain("No saved credentials"));
        }
    }

    // An app in legendary's installed list exits 12 before any request, because prepare_download would reuse its folder.
    [Test]
    [Platform("Win")]
    public async Task Install_AppListedAsInstalled_Exits12()
    {
        var (exitCode, lines) = await RunCaseAsync("listed");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(12));
            Assert.That(lines, Does.Contain("manifest requests: 0"));
        }
    }

    // An account without the app exits 1 with the app in the line, because the message of the tool only has that line to show.
    [Test]
    [Platform("Win")]
    public async Task Install_AppOutsideTheLibrary_Exits1WithTheApp()
    {
        var (exitCode, lines) = await RunCaseAsync("no-game");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(lines, Does.Contain("app is not in the library of this Epic account"));
            Assert.That(lines, Does.Contain("manifest requests: 0"));
        }
    }

    // A manifest of another sha1 exits 11 with both values and doesn't write the manifest or start the download.
    [Test]
    [Platform("Win")]
    public async Task Install_ManifestOfOtherSha1_Exits11WithBothValues()
    {
        var (exitCode, lines) = await RunCaseAsync("other-sha1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(11));
            Assert.That(lines, Has.Some.StartsWith($"sha1: pinned {ManifestSha1} got "));
            Assert.That(lines, Does.Contain("manifest file: False"));
            Assert.That(lines, Does.Not.Contain("prepare_download"));
        }
    }

    // A manifest with the pinned sha1 and another build exits 11 with both values and doesn't write the manifest or start the download.
    [Test]
    [Platform("Win")]
    public async Task Install_ManifestOfOtherBuild_Exits11WithBothValues()
    {
        var (exitCode, lines) = await RunCaseAsync("other-build");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(11));
            Assert.That(lines, Does.Contain("build: pinned 1.2.3-4 got 1.2.3-5"));
            Assert.That(lines, Does.Contain("manifest file: False"));
            Assert.That(lines, Does.Not.Contain("prepare_download"));
        }
    }

    // The script passes the row, the checked manifest file and the base urls of the same response, because a local manifest doesn't come with any.
    [Test]
    [Platform("Win")]
    public async Task Install_PinnedManifest_PassesTheRowToPrepareDownload()
    {
        var (exitCode, lines) = await RunCaseAsync("resume-done");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Does.Contain("skip_ue: False"));
            Assert.That(lines, Does.Contain($"base_path: {_base}"));
            Assert.That(lines, Does.Contain("game_folder: app"));
            Assert.That(lines, Does.Contain("file_install_tag: ['']"));
            Assert.That(lines, Does.Contain($"override_manifest: {Path.Combine(_base, "app.manifest")}"));
            Assert.That(lines, Does.Contain("base_urls: ['https://example.invalid/base']"));
            Assert.That(lines, Does.Contain($"manifest sha1: {ManifestSha1}"));
        }
    }

    // A game legendary has stored skips the list refresh, because that refresh sends requests for the whole library on every engine.
    [Test]
    [Platform("Win")]
    public async Task Install_StoredGame_SkipsTheListRefresh()
    {
        var (exitCode, lines) = await RunCaseAsync("stored-game");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Has.None.StartsWith("skip_ue: "));
            Assert.That(lines, Does.Contain("manifest requests: 1"));
        }
    }

    // A resume that finds every chunk done skips the manager, creates the empty files in their folders and deletes the resume file.
    [Test]
    [Platform("Win")]
    public async Task Install_ResumeFindsEveryChunkDone_CreatesEmptyFilesAndDeletesTheResumeFile()
    {
        var (exitCode, lines) = await RunCaseAsync("resume-done");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Does.Contain("manager started: False"));
            Assert.That(Path.Combine(_base, "app", "folder", "sub", "empty.bin"), Does.Exist);
            Assert.That(lines, Does.Contain("resume file: False"));
        }
    }

    // Bad and missing files exit 13 and the resume file lists only the good file, so the next run downloads the rest.
    [Test]
    [Platform("Win")]
    public async Task Install_BadAndMissingFiles_Exits13AndRewritesTheResumeFile()
    {
        var (exitCode, lines) = await RunCaseAsync("bad-files");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(13));
            Assert.That(lines, Does.Contain("bad.bin"));
            Assert.That(lines, Does.Contain("missing.bin"));
            Assert.That(lines, Does.Contain("bad files: 2"));
            Assert.That(lines, Does.Contain("resume: aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d:good.bin"));
            Assert.That(lines, Has.Exactly(1).StartsWith("resume: "));
        }
    }

    // More bad files than the message can show print 9 paths and the count, so the last 10 lines the tool keeps have the count.
    [Test]
    [Platform("Win")]
    public async Task Install_ManyBadFiles_PrintsAtMost9PathsThenTheCount()
    {
        var (exitCode, lines) = await RunCaseAsync("many-bad-files");

        int count = lines.IndexOf("bad files: 12");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(13));
            Assert.That(lines[count - 9], Is.EqualTo("file0.bin"));
            Assert.That(lines[count - 1], Is.EqualTo("file8.bin"));
            Assert.That(lines, Does.Not.Contain("file9.bin"));
        }
    }

    // The tag filter leaves tagged files out of the hash check and keeps files with no tag or the empty tag, as legendary's own filter does.
    [Test]
    [Platform("Win")]
    public async Task Install_TagFilterOn_ChecksOnlyTheUntaggedFiles()
    {
        var (exitCode, lines) = await RunCaseAsync("tags-on");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Does.Contain("checking files: 2"));
        }
    }

    // A row without tags checks every file, so a tagged file in that manifest still has to be on disk.
    [Test]
    [Platform("Win")]
    public async Task Install_TagFilterOff_ChecksTaggedFilesToo()
    {
        var (exitCode, lines) = await RunCaseAsync("tags-off");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(13));
            Assert.That(lines, Does.Contain("file_install_tag: None"));
            Assert.That(lines, Does.Contain("checking files: 3"));
            Assert.That(lines, Does.Contain("tagged.bin"));
        }
    }

    // A failed download exits 1 with the code of the manager and keeps the resume file for the next run.
    [Test]
    [Platform("Win")]
    public async Task Install_ManagerFails_Exits1AndKeepsTheResumeFile()
    {
        var (exitCode, lines) = await RunCaseAsync("manager-fails");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(lines, Does.Contain("download manager exit code: 3"));
            Assert.That(lines, Does.Contain("resume file: True"));
            Assert.That(lines, Has.None.StartsWith("checking files"));
        }
    }

    // The percent and the speed print every 5 percent, so a repeat or a small move doesn't add a line to the build log.
    [Test]
    [Platform("Win")]
    public async Task Install_Download_PrintsEvery5PercentWithTheSpeed()
    {
        var (exitCode, lines) = await RunCaseAsync("download");

        string[] expected = ["download: 0%, 1.0 MiB/s", "download: 5%, 1.0 MiB/s", "download: 50%, 1.0 MiB/s", "download: 100%, 1.0 MiB/s"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines.FindAll(line => line.StartsWith("download: ", StringComparison.Ordinal)), Is.EqualTo(expected));
        }
    }

    // A download without a finished chunk for too long kills the manager and its tree and exits 1, so a retry loop never hangs the build.
    [Test]
    [Platform("Win")]
    public async Task Install_DownloadStalls_KillsTheManagerAndExits1()
    {
        var (exitCode, lines) = await RunCaseAsync("stall");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(lines, Does.Contain("download stalled: no finished chunk for 0 seconds"));
            Assert.That(lines, Does.Contain("manager alive: False"));
        }
    }

    // Paths outside ASCII print as UTF-8 for the tool and go to the resume file as UTF-8 for legendary's reader.
    [Test]
    [Platform("Win")]
    public async Task Install_PathsOutsideAscii_PrintUtf8AndKeepThemInTheResumeFile()
    {
        var (exitCode, lines) = await RunCaseAsync("non-ascii");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(13));
            Assert.That(lines, Does.Contain("Café/日本語.bin"));
            Assert.That(lines, Has.Some.EndsWith(":Ünï/ключ.bin").And.StartsWith("resume: "));
        }
    }

    // The real manager downloads a binary manifest from a local server through its worker processes and doesn't request the chunk of a tagged file.
    [Test]
    [Platform("Win")]
    public async Task Install_RealManagerWithBinaryManifest_InstallsTheUntaggedFiles()
    {
        var (exitCode, lines) = await RunCaseAsync("server-binary");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(_base, "app", "folder", "a.bin")), Is.EqualTo(new string('a', 1000)));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(_base, "app", "b.bin")), Is.EqualTo(new string('b', 2000)));
            Assert.That(new FileInfo(Path.Combine(_base, "app", "folder", "empty.bin")).Length, Is.Zero);
            Assert.That(Path.Combine(_base, "app", "tagged.bin"), Does.Not.Exist);
            Assert.That(lines, Does.Contain("chunk requests of tagged.bin: 0"));
            Assert.That(lines, Does.Contain("resume file: False"));
            Assert.That(lines, Has.Some.StartsWith("download: "));
        }
    }

    // The real manager downloads a JSON manifest without the size and sha lists, as Epic's 4.0 manifests have it, through the reader patch.
    [Test]
    [Platform("Win")]
    public async Task Install_RealManagerWithJsonManifest_InstallsThroughTheReaderPatch()
    {
        var (exitCode, lines) = await RunCaseAsync("server-json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(await File.ReadAllTextAsync(Path.Combine(_base, "app", "folder", "a.bin")), Is.EqualTo(new string('a', 1000)));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(_base, "app", "b.bin")), Is.EqualTo(new string('b', 2000)));
            Assert.That(new FileInfo(Path.Combine(_base, "app", "folder", "empty.bin")).Length, Is.Zero);
            Assert.That(lines, Has.Some.StartsWith("download: "));
        }
    }

    // A resume that finds every chunk done passes legendary's real analysis without a request and creates the empty files without the manager.
    [Test]
    [Platform("Win")]
    public async Task Install_RealAnalysisWithEveryChunkDone_InstallsWithoutARequest()
    {
        var (exitCode, lines) = await RunCaseAsync("server-binary-resume-done");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Does.Contain("chunk requests of folder/a.bin: 0"));
            Assert.That(lines, Does.Contain("chunk requests of b.bin: 0"));
            Assert.That(new FileInfo(Path.Combine(_base, "app", "folder", "empty.bin")).Length, Is.Zero);
            Assert.That(lines, Does.Contain("resume file: False"));
        }
    }

    // A chunk with the wrong bytes fails the file hash check and the next run gets only the chunk of that file from the server.
    [Test]
    [Platform("Win")]
    public async Task Install_RealManagerWithBadChunk_Exits13ThenDownloadsOnlyThatFile()
    {
        var (exitCode, lines) = await RunCaseAsync("server-binary-bad-chunk");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Does.Contain("first exit: 13"));
            Assert.That(lines, Does.Contain("b.bin"));
            Assert.That(lines, Does.Contain("bad files: 1"));
            Assert.That(lines, Does.Contain("chunk requests of folder/a.bin: 1"));
            Assert.That(lines, Does.Contain("chunk requests of b.bin: 2"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(_base, "app", "b.bin")), Is.EqualTo(new string('b', 2000)));
        }
    }

    // A chunk with the wrong bytes also fails the file hash check with the reader patch, which leaves the chunk sha list out of the manifest.
    [Test]
    [Platform("Win")]
    public async Task Install_RealManagerWithBadChunkInJsonManifest_Exits13ThenDownloadsOnlyThatFile()
    {
        var (exitCode, lines) = await RunCaseAsync("server-json-bad-chunk");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines, Does.Contain("first exit: 13"));
            Assert.That(lines, Does.Contain("b.bin"));
            Assert.That(lines, Does.Contain("chunk requests of folder/a.bin: 1"));
            Assert.That(lines, Does.Contain("chunk requests of b.bin: 2"));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(_base, "app", "b.bin")), Is.EqualTo(new string('b', 2000)));
        }
    }

    private async Task<(int ExitCode, List<string> Lines)> RunCaseAsync(string name)
    {
        string script = Path.Combine(_base, "install_engine.py");
        string harness = Path.Combine(_base, "harness.py");
        UnrealEngine.WriteScript(script);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Unreal4", HarnessFile), harness);

        var info = ProcessRunner.NewStartInfo("uv", UnrealEngine.UvArguments(harness, script, name, _base));
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.WorkingDirectory = Path.GetTempPath();

        List<string> lines = [];
        int exitCode = await ProcessRunner.RunWithLinesAsync(info, lines.Add, CancellationToken.None);
        await TestContext.Out.WriteLineAsync(string.Join(Environment.NewLine, lines));

        return (exitCode, lines);
    }
}
