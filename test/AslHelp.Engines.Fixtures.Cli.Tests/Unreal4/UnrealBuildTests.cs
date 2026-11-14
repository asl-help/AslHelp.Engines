using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Unreal4;
using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealBuildTests
{
    private const string Image = "asl-help/unreal4-build:0123456789abcdef";

    private static readonly UnrealBuildStep.Editor _editor = new();

    private TestFolder _folder = null!;
    private UnrealPaths _paths = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _paths = new UnrealPaths(_folder.NewFolder(), _folder.NewFolder(), _folder.NewFolder());
        _ = Directory.CreateDirectory(_paths.Logs);
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // The 1st player cooks: package.bat gets the platform, the configuration, the tag, -cook and the log redirect.
    [Test]
    public void PackageStep_Cook_PassesTheCookSwitch()
    {
        var release = EmbeddedReleases<UnrealRelease>.Find("4.19");

        var step = UnrealBuild.PackageStep(release, Image, _paths, UnrealPlayer.Parse("win-x86-shipping"), UnrealCookMode.Cook);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(step.Image, Is.EqualTo(Image));
            Assert.That(
                step.Command,
                Is.EqualTo(["cmd", "/c", @"C:\r\package.bat Win32 Shipping win-x86-shipping -cook > C:\l\package-win-x86-shipping.log 2>&1"]));
        }
    }

    // A later player reuses the cooked content, so it gets -skipcook in place of -cook.
    [Test]
    public void PackageStep_SkipCook_PassesTheSkipCookSwitch()
    {
        var release = EmbeddedReleases<UnrealRelease>.Find("4.19");

        var step = UnrealBuild.PackageStep(release, Image, _paths, UnrealPlayer.Parse("win-x64-development"), UnrealCookMode.SkipCook);

        Assert.That(step.Command[^1], Does.StartWith(@"C:\r\package.bat Win64 Development win-x64-development -skipcook >"));
    }

    // 4.0 steps get the -rocket switches of their row and no PATH folder.
    [Test]
    public void EditorAndPackageStep_RocketRow_GetTheRowSwitchesWithoutPathFolder()
    {
        var release = EmbeddedReleases<UnrealRelease>.Find("4.0");

        var editor = UnrealBuild.EditorStep(release, Image, _paths);
        var package = UnrealBuild.PackageStep(release, Image, _paths, UnrealPlayer.Parse("win-x64-development"), UnrealCookMode.Cook);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Environment["FIXTURE_EDITOR_FLAGS"], Is.EqualTo("-rocket"));
            Assert.That(package.Environment["FIXTURE_PACKAGE_FLAGS"], Is.EqualTo("-rocket -nobootstrapexe"));
            Assert.That(editor.Environment.Keys.Concat(package.Environment.Keys), Does.Not.Contain("FIXTURE_PATH_PREPEND"));
        }
    }

    // 4.19 steps get -waitmutex on the editor and the 14.16 compiler folder first on PATH, which its 32-bit builds need.
    [Test]
    public void EditorAndPackageStep_RowWithPathFolder_PutTheFolderInEveryStep()
    {
        var release = EmbeddedReleases<UnrealRelease>.Find("4.19");

        var editor = UnrealBuild.EditorStep(release, Image, _paths);
        var package = UnrealBuild.PackageStep(release, Image, _paths, UnrealPlayer.Parse("win-x86-shipping"), UnrealCookMode.SkipCook);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.Command[^1], Is.EqualTo(@"C:\r\editor.bat > C:\l\editor.log 2>&1"));
            Assert.That(editor.Environment["FIXTURE_EDITOR_FLAGS"], Is.EqualTo("-waitmutex"));
            Assert.That(package.Environment["FIXTURE_PACKAGE_FLAGS"], Is.Empty);
            Assert.That(editor.Environment["FIXTURE_PATH_PREPEND"], Is.EqualTo(release.PathPrepend));
            Assert.That(package.Environment["FIXTURE_PATH_PREPEND"], Is.EqualTo(release.PathPrepend));
        }
    }

    // Every step mounts the engine, the project, the archives and the logs read-write and only the scripts read-only.
    [Test]
    public void RuntimesStep_Mounts_OnlyScriptsReadOnly()
    {
        var step = UnrealBuild.RuntimesStep(Image, _paths);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(step.Command[^1], Is.EqualTo(@"C:\r\runtimes.bat > C:\l\runtimes.log 2>&1"));
            Assert.That(step.Owner, Is.EqualTo(_paths.Scratch));
            Assert.That(
                step.Mounts,
                Is.EqualTo(new BuildMount[]
                {
                    new(_paths.Engine, @"C:\e", ReadOnly: false),
                    new(_paths.Project, @"C:\p", ReadOnly: false),
                    new(_paths.Out, @"C:\o", ReadOnly: false),
                    new(_paths.Logs, @"C:\l", ReadOnly: false),
                    new(_paths.Steps, @"C:\r", ReadOnly: true),
                }));
        }
    }

    // A failed step throws with its name, the code, the log and the last 10 lines that aren't blank.
    [Test]
    public void RunAsync_StepExitsNonZero_ThrowsWithNameCodeAndLogTail()
    {
        UnrealBuildStep.Package package = new(UnrealPlayer.Parse("win-x64-shipping"));
        string log = _paths.LogPath(package);
        File.WriteAllLines(log, [.. Enumerable.Range(1, 15).Select(line => $"line {line}"), ""]);
        FakeBuildHost host = new(_ => 5);

        Assert.That(
            async () => await RunAsync(host, package, TimeSpan.FromMinutes(1), CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("package-win-x64-shipping")
                .And.Message.Contains("code 5 (0x00000005)")
                .And.Message.Contains(log)
                .And.Message.Contains("line 6")
                .And.Message.Contains("line 15")
                .And.Message.Not.Contains("line 5" + Environment.NewLine));
    }

    // The log tail decodes with the OEM code page, because cmd writes the bytes of the step tools as the console got them.
    [Test]
    public void RunAsync_StepLogInOemCodePage_ThrowsWithTheDecodedTail()
    {
        string log = _paths.LogPath(_editor);
        File.WriteAllBytes(log, ProcessRunner.OemEncoding.GetBytes("Datei für Schritt fehlt\r\n"));
        FakeBuildHost host = new(_ => 1);

        Assert.That(
            async () => await RunAsync(host, _editor, TimeSpan.FromMinutes(1), CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("Datei für Schritt fehlt"));
    }

    // A step without a log still fails with its name and code, because a container can fail before the redirect.
    [Test]
    public void RunAsync_StepExitsWithoutLog_ThrowsWithNameAndCode()
    {
        FakeBuildHost host = new(_ => 1);

        Assert.That(
            async () => await RunAsync(host, _editor, TimeSpan.FromMinutes(1), CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("editor").And.Message.Contains("code 1 (0x00000001)"));
    }

    // A step that runs past the timeout throws with the log and its container is removed.
    [Test]
    public void RunAsync_StepRunsPastTimeout_ThrowsTimeoutAndRemovesTheContainer()
    {
        FakeBuildHost host = new(_ => null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await RunAsync(host, _editor, TimeSpan.FromMilliseconds(100), CancellationToken.None),
                Throws.TypeOf<TimeoutException>().With.Message.Contains(_paths.LogPath(_editor)).And.Message.Contains("within 0.1 s."));
            Assert.That(host.DisposedRuns, Is.EqualTo(1));
        }
    }

    // A cancel during a step throws OperationCanceledException and removes the container.
    [Test]
    public void RunAsync_Canceled_ThrowsAndRemovesTheContainer()
    {
        FakeBuildHost host = new(_ => null);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await RunAsync(host, _editor, TimeSpan.FromMinutes(1), cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(host.DisposedRuns, Is.EqualTo(1));
        }
    }

    private Task RunAsync(FakeBuildHost host, UnrealBuildStep buildStep, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return UnrealBuild.RunAsync(host, UnrealBuild.RuntimesStep(Image, _paths), buildStep, _paths, timeout, cancellationToken);
    }
}
