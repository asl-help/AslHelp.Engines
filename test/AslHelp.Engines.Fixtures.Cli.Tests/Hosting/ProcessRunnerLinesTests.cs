using AslHelp.Engines.Fixtures.Cli.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Hosting;

public sealed class ProcessRunnerLinesTests
{
    private const int LineCount = 200_000;

    private static readonly string _cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly TimeSpan _runTimeout = TimeSpan.FromSeconds(60);

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

    // Output far past the size of a pipe reaches the callback in full on both streams and each stream keeps its order.
    [Test]
    [Platform("Win")]
    public async Task RunWithLinesAsync_HugeOutputOnBothStreams_GetsEveryLineInOrder()
    {
        string folder = _folder.NewFolder();
        await File.WriteAllLinesAsync(Path.Combine(folder, "out.txt"), NumberedLines("out"));
        await File.WriteAllLinesAsync(Path.Combine(folder, "err.txt"), NumberedLines("err"));
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "type err.txt 1>&2& type out.txt");
        info.WorkingDirectory = folder;
        List<string> lines = [];

        int exitCode = await ProcessRunner.RunWithLinesAsync(info, lines.Add, CancellationToken.None).WaitAsync(_runTimeout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(lines.FindAll(line => line.StartsWith("out", StringComparison.Ordinal)), Is.EqualTo(NumberedLines("out")));
            Assert.That(lines.FindAll(line => line.StartsWith("err", StringComparison.Ordinal)), Is.EqualTo(NumberedLines("err")));
        }
    }

    // 1 line of 4 MB without a line end reaches the callback whole.
    [Test]
    [Platform("Win")]
    public async Task RunWithLinesAsync_HugeLineWithoutLineEnd_GetsItWhole()
    {
        string folder = _folder.NewFolder();
        string text = new('a', 4 << 20);
        await File.WriteAllTextAsync(Path.Combine(folder, "file.txt"), text);
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "type file.txt");
        info.WorkingDirectory = folder;
        List<string> lines = [];

        _ = await ProcessRunner.RunWithLinesAsync(info, lines.Add, CancellationToken.None).WaitAsync(_runTimeout);

        Assert.That(lines.Single(), Is.EqualTo(text));
    }

    // A negative or very large code comes back unchanged, because a crashed process exits with an NTSTATUS such as 0xC0000005.
    [TestCase(-1)]
    [TestCase(-1073741819)]
    [TestCase(int.MaxValue)]
    [Platform("Win")]
    public async Task RunWithLinesAsync_UnusualExitCode_ReturnsItUnchanged(int code)
    {
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", $"exit {code}");

        int exitCode = await ProcessRunner.RunWithLinesAsync(info, _ => { }, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(code));
    }

    private static IEnumerable<string> NumberedLines(string prefix)
    {
        return Enumerable.Range(0, LineCount).Select(i => $"{prefix} {i}");
    }
}
