using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan _cancelDelay = TimeSpan.FromSeconds(1);
    private static readonly string _cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly TimeSpan _exitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _runTimeout = TimeSpan.FromSeconds(10);

    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // A code other than 0 throws with the command line and the code in decimal and 8 hex digits, also for a negative code.
    [TestCase("exit 3", "code 3 (0x00000003)")]
    [TestCase("exit -1", "code -1 (0xFFFFFFFF)")]
    [Platform("Win")]
    [SetCulture("en-US")]
    public void RunAsync_NonZeroExit_ThrowsWithTheCommandLineAndCode(string command, string code)
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", command);

        Assert.That(
            async () => await ProcessRunner.RunAsync(info, CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains($"'{_cmdPath} /c \"{command}\"'")
                .And.Message.Contains(code));
    }

    // A tool that prints no text gets a message that ends after the code.
    [TestCase("exit 3")]
    [TestCase("echo.& exit 3")]
    [Platform("Win")]
    public void RunAsync_ToolPrintsNoText_EndsTheMessageAfterTheCode(string command)
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", command);

        Assert.That(
            async () => await ProcessRunner.RunAsync(info, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.EndsWith("exited with code 3 (0x00000003)."));
    }

    // The lines a failing tool prints to stdout and to stderr follow the code, each on its own line.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_ToolPrintsToBothStreams_PutsEachLineInTheMessage()
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "echo boom& echo bust>&2& exit 3");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        Assert.That(OutputLines(exception!), Is.EquivalentTo(["boom", "bust"]));
    }

    // Only the last lines of a long output reach the message, in the order the tool printed them.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_MoreLinesThanTheTail_KeepsOnlyTheLastLines()
    {
        int count = ProcessRunner.TailLineCount + 5;
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", $"(for /l %i in (1,1,{count}) do @echo line %i)& exit 3");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        Assert.That(OutputLines(exception!), Is.EqualTo(Enumerable.Range(6, ProcessRunner.TailLineCount).Select(i => $"line {i}")));
    }

    // Blank lines are left out, so they never push a line with text out of the message.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_BlankLinesBetweenText_LeavesThemOut()
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "echo a& echo.& echo b& exit 3");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        Assert.That(OutputLines(exception!), Is.EqualTo(["a", "b"]));
    }

    // Each argument shows as Process passes it on Windows, so a path with a space reads as 1 argument.
    [TestCase("a b", @"""a b""")]
    [TestCase("", @"""""")]
    [TestCase("a\tb", "\"a\tb\"")]
    [TestCase(@"a""b", @"""a\""b""")]
    [TestCase(@"a\""b", @"""a\\\""b""")]
    [TestCase(@"a b\", @"""a b\\""")]
    [TestCase(@"a\b", @"a\b")]
    [Platform("Win")]
    public void RunAsync_OddArgument_QuotesItAsProcessDoes(string argument, string shown)
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "exit", "3", argument);

        Assert.That(
            async () => await ProcessRunner.RunAsync(info, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains($"'{_cmdPath} /c exit 3 {shown}'"));
    }

    // Arguments set as 1 string show as they are, because Process passes that string to the tool unchanged.
    [Test]
    [Platform("Win")]
    public void RunAsync_ArgumentsString_ShowsItAsItIs()
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath);
        info.Arguments = "/c \"exit 3\"";

        Assert.That(
            async () => await ProcessRunner.RunAsync(info, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.StartsWith($"'{_cmdPath} /c \"exit 3\"' exited"));
    }

    // A tool path with a space shows in quotes, so the message reads as 1 tool and its arguments.
    [Test]
    [Platform("Win")]
    public void RunAsync_ToolPathWithSpace_QuotesItInTheMessage()
    {
        string folder = Path.Combine(_folder.Root, "a b");
        _ = Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, "cmd.exe");
        File.Copy(_cmdPath, copy);
        var info = ProcessRunner.NewStartInfo(copy, "/c", "exit", "3");

        Assert.That(
            async () => await ProcessRunner.RunAsync(info, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.StartsWith($"'\"{copy}\" /c exit 3' exited"));
    }

    // A cancel while the tool runs ends the tool and its ping.exe child within seconds, so the folder they have open can be deleted.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_CanceledWhileRunning_EndsTheToolAndItsChild()
    {
        string folder = _folder.NewFolder();
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "ping -n 30 127.0.0.1 > file.txt");
        info.WorkingDirectory = folder;
        using CancellationTokenSource cancellation = new(_cancelDelay);

        await Assert.ThatAsync(
            () => ProcessRunner.RunAsync(info, cancellation.Token).WaitAsync(_exitTimeout),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(WaitUntil(() => TryDelete(folder), _exitTimeout), Is.True);
    }

    // A tool that writes 3000 lines to stderr and nothing to stdout runs to its end, because both pipes are read while it runs.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_ManyLinesOnStderrOnly_ReadsThemWhileTheToolRuns()
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "(for /l %i in (1,1,3000) do @(echo line %i)>&2)& exit 3");
        using CancellationTokenSource cancellation = new(_runTimeout);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, cancellation.Token));

        Assert.That(OutputLines(exception!).LastOrDefault(), Is.EqualTo("line 3000"));
    }

    // A canceled token throws before the start, so a file that does not exist gives no Win32Exception and no tool starts.
    [Test]
    public void RunAsync_CanceledToken_ThrowsBeforeTheStart()
    {
        var info = ProcessRunner.NewStartInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe"));

        Assert.That(
            async () => await ProcessRunner.RunAsync(info, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // The start info runs the tool without a shell or a window, pipes its output and ends it with this process, with 1 argument per item.
    [Test]
    public void NewStartInfo_Arguments_SetsNoWindowKillOnParentExitAndPipes()
    {
        var info = ProcessRunner.NewStartInfo("file.exe", "-a", "b c");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.CreateNoWindow, Is.True);
            Assert.That(info.KillOnParentExit, Is.True);
            Assert.That(info.RedirectStandardOutput, Is.True);
            Assert.That(info.RedirectStandardError, Is.True);
            Assert.That(info.ArgumentList, Is.EqualTo(["-a", "b c"]));
        }
    }

    private static string[] OutputLines(InvalidOperationException exception)
    {
        return exception.Message.Split(Environment.NewLine)[1..];
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                return false;
            }

            Thread.Sleep(50);
        }

        return true;
    }
}
