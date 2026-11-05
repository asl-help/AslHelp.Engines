using NUnit.Framework;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class ProcessRunnerEdgeCaseTests
{
    private static readonly TimeSpan _cancelDelay = TimeSpan.FromSeconds(1);
    private static readonly string _cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly TimeSpan _exitTimeout = TimeSpan.FromSeconds(5);

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

    // A tool that prints text gets the same 1st line as one that prints nothing, with the command line and the code in decimal and hex.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_ToolPrintsText_StartsTheMessageWithTheCommandLineAndCode()
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "echo boom& exit 3");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        Assert.That(
            exception!.Message.Split(Environment.NewLine)[0],
            Does.StartWith($"'{_cmdPath} /c \"echo boom& exit 3\"' exited with code 3 (0x00000003)."));
    }

    // A line that ends in CRLF, CR, LF or nothing is 1 line of the message, so the last line shows even without a line end.
    [TestCase("first\r\nlast")]
    [TestCase("first\rlast\r")]
    [TestCase("first\nlast\n")]
    [Platform("Win")]
    public async Task RunAsync_OddLineEnds_PutsEachLineInTheMessage(string output)
    {
        var info = NewPrintingStartInfo(output);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        Assert.That(OutputLines(exception!), Is.EqualTo(["first", "last"]));
    }

    // A char outside ASCII shows unchanged while this process has a UTF-8 console, because the tool writes in the code page of its own console.
    [TestCase("echo ü& exit 3")]
    [TestCase("echo ü>&2& exit 3")]
    [Platform("Win")]
    [NonParallelizable]
    public async Task RunAsync_ConsoleInUtf8_ShowsCharOutsideAsciiUnchanged(string command)
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", command);
        var consoleEncoding = Console.OutputEncoding;

        // Switch the console of this process to UTF-8, so its code page differs from the OEM code page that the tool writes in.
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

            Assert.That(OutputLines(exception!), Is.EqualTo(["ü"]));
        }
        finally
        {
            Console.OutputEncoding = consoleEncoding;
        }
    }

    // Empty lines and lines of only spaces or tabs after a full tail push no line with text out of the message.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_BlankLinesAfterAFullTail_KeepsEveryTextLine()
    {
        string[] text = [.. Enumerable.Range(1, ProcessRunner.TailLineCount).Select(i => $"line {i}")];
        var info = NewPrintingStartInfo(string.Join("\r\n", text) + "\r\n\r\n   \r\n\t\r\n \t \r\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        Assert.That(OutputLines(exception!), Is.EqualTo(text));
    }

    // Both streams share 1 tail in the order the lines came, so 5 later stderr lines push the 5 oldest stdout lines out.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_StderrLinesAfterStdoutLines_KeepsTheLastLinesOfBoth()
    {
        int count = ProcessRunner.TailLineCount;

        // Pause for about 1 second between the 2 batches, so the stdout lines come first.
        var info = ProcessRunner.NewStartInfo(
            _cmdPath,
            "/c",
            $"(for /l %i in (1,1,{count}) do @echo out %i)& ping -n 2 127.0.0.1 >nul& (for /l %i in (1,1,5) do @(echo err %i)>&2)& exit 3");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        string[] expected = [.. Enumerable.Range(6, count - 5).Select(i => $"out {i}"), .. Enumerable.Range(1, 5).Select(i => $"err {i}")];
        Assert.That(OutputLines(exception!), Is.EqualTo(expected));
    }

    // Only the code decides a failure, so a tool that prints to stderr and exits with 0 passes.
    [Test]
    [Platform("Win")]
    public void RunAsync_ExitCodeZeroWithStderrLines_Completes()
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "echo boom& echo bust>&2");

        Assert.That(async () => await ProcessRunner.RunAsync(info, CancellationToken.None), Throws.Nothing);
    }

    // The message shows each argument as cmd.exe receives it, also for backslashes before a quote and for white space and letters outside ASCII.
    [TestCase(@"a\\""b")]
    [TestCase(@"""")]
    [TestCase(@"\\")]
    [TestCase(@"a b\\")]
    [TestCase("a\u00A0b")]
    [TestCase("\u3000")]
    [TestCase("ü")]
    [Platform("Win")]
    public async Task RunAsync_OddArgument_ShowsTheCommandLineTheToolReceives(string argument)
    {
        // Echo the received command line in UTF-16 with delayed expansion, so cmd.exe prints its quotes and every char unchanged.
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/u", "/v:on", "/c", "echo", "!CMDCMDLINE!&", "exit", "3", argument);
        info.StandardOutputEncoding = Encoding.Unicode;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(info, CancellationToken.None));

        string[] lines = exception!.Message.Split(Environment.NewLine);
        string shown = lines[0][1..lines[0].IndexOf("' exited", StringComparison.Ordinal)];

        // Quote the tool path as Process does, because it puts the file name in quotes on every start.
        Assert.That(lines[1..], Is.EqualTo([$"\"{_cmdPath}\"{shown[_cmdPath.Length..]}"]));
    }

    // RunAsync throws after the tool has exited, not as soon as its exit code is set, so the copy of the tool it ran can be deleted right away.
    [Test]
    [Platform("Win")]
    public void RunAsync_ToolCopyExitsNonZero_ThrowsOnceTheCopyCanBeDeleted()
    {
        string copy = Path.Combine(_folder.NewFolder(), "cmd.exe");

        // Run a fresh copy 10 times, because a return before the exit leaves the copy in use on most runs and not on every one.
        for (int i = 0; i < 10; i++)
        {
            File.Copy(_cmdPath, copy);
            var info = ProcessRunner.NewStartInfo(copy, "/c", "exit 3");

            Assert.That(async () => await ProcessRunner.RunAsync(info, CancellationToken.None), Throws.InvalidOperationException);
            Assert.That(() => File.Delete(copy), Throws.Nothing);
        }
    }

    // A cancel while the tool prints lines without a pause still ends it within seconds, so the file it has open can be deleted.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_CanceledWhileTheToolPrints_EndsTheTool()
    {
        string folder = _folder.NewFolder();

        // Count from 1 in steps of 0, so cmd.exe prints the same line until the cancel ends it.
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "(for /l %i in (1,0,2) do @echo line %i) 2>file.txt");
        info.WorkingDirectory = folder;
        using CancellationTokenSource cancellation = new(_cancelDelay);

        await Assert.ThatAsync(
            () => ProcessRunner.RunAsync(info, cancellation.Token).WaitAsync(_exitTimeout),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(WaitUntil(() => TryDelete(folder), _exitTimeout), Is.True);
    }

    // A cancel after the tool exited ends the ping.exe it started, which keeps the pipes open, so the read stops and the folder can be deleted.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_CanceledAfterTheToolExited_EndsTheChildThatKeepsThePipesOpen()
    {
        string folder = _folder.NewFolder();

        // Start ping.exe in the background and exit, so only ping.exe keeps the pipes open when the cancel comes.
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "start /b ping -n 30 127.0.0.1 >file.txt& exit 3");
        info.WorkingDirectory = folder;
        using CancellationTokenSource cancellation = new(_cancelDelay);

        await Assert.ThatAsync(
            () => ProcessRunner.RunAsync(info, cancellation.Token).WaitAsync(_exitTimeout),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(WaitUntil(() => TryDelete(folder), _exitTimeout), Is.True);
    }

    // A read that fails while the tool runs ends the tool and the ping.exe it started, so the folder they have open can be deleted.
    [Test]
    [Platform("Win")]
    public async Task RunAsync_ReadFails_EndsTheToolAndItsChild()
    {
        string folder = _folder.NewFolder();
        WriteBytes(folder, "file.bin", [(byte)'a', 0xFF, (byte)'\r', (byte)'\n']);

        // Decode stdout as UTF-8 that throws on the 0xFF byte, so the read fails while ping.exe still runs.
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "type file.bin& ping -n 30 127.0.0.1 >file.txt");
        info.WorkingDirectory = folder;
        info.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        await Assert.ThatAsync(
            () => ProcessRunner.RunAsync(info, CancellationToken.None).WaitAsync(_exitTimeout),
            Throws.InstanceOf<DecoderFallbackException>());

        Assert.That(WaitUntil(() => TryDelete(folder), _exitTimeout), Is.True);
    }

    // A missing tool fails the start with Win32Exception, not with the message of a tool that exited with a code other than 0.
    [Test]
    public void RunAsync_ToolDoesNotExist_ThrowsWin32Exception()
    {
        var info = ProcessRunner.NewStartInfo(Path.Combine(_folder.Root, "file.exe"));

        Assert.That(async () => await ProcessRunner.RunAsync(info, CancellationToken.None), Throws.TypeOf<Win32Exception>());
    }

    // A start info that does not redirect stdout or stderr throws before the start, so a file that does not exist gives no Win32Exception.
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void RunAsync_OutputNotRedirected_ThrowsBeforeTheStart(bool redirectStandardOutput, bool redirectStandardError)
    {
        var info = ProcessRunner.NewStartInfo(Path.Combine(_folder.Root, "file.exe"));
        info.RedirectStandardOutput = redirectStandardOutput;
        info.RedirectStandardError = redirectStandardError;

        Assert.That(async () => await ProcessRunner.RunAsync(info, CancellationToken.None), Throws.ArgumentException);
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

    private ProcessStartInfo NewPrintingStartInfo(string output)
    {
        string folder = _folder.NewFolder();
        WriteBytes(folder, "file.txt", Encoding.ASCII.GetBytes(output));

        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "type file.txt& exit 3");
        info.WorkingDirectory = folder;

        return info;
    }
}
