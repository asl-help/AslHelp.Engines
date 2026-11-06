using AslHelp.Engines.Fixtures.Unreal3;
using NUnit.Framework;
using System.IO;

namespace AslHelp.Engines.Fixtures.Tests.Unreal3;

public sealed class UdkLogScanTests
{
    private DirectoryInfo _folder = null!;
    private string _log = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Directory.CreateTempSubdirectory("AslHelp.Engines.Fixtures.Tests-");
        _log = Path.Combine(_folder.FullName, "file.log");
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete(recursive: true);
    }

    // A ready line split between 2 reads still matches, because each read starts a little before the end of the last one.
    [Test]
    public void HasReadyLine_LineSplitBetweenReads_FindsTheLine()
    {
        File.WriteAllText(_log, "[0001.00] ScriptLog: Fixt");
        UdkLogScan scan = new(_log);

        bool first = scan.HasReadyLine();
        File.AppendAllText(_log, "ure ready\r\n");
        bool second = scan.HasReadyLine();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.True);
        }
    }

    // A ready line across the 1st and 2nd buffer of 4096 bytes still matches, because the end of each buffer is kept for the next.
    [Test]
    public void HasReadyLine_LineAfterTheFirstBuffer_FindsTheLine()
    {
        File.WriteAllText(_log, new string('x', 4090) + UdkLogScan.ReadyLine);

        Assert.That(new UdkLogScan(_log).HasReadyLine(), Is.True);
    }

    // A miss moves the scanned length to the end of the log, so the next read skips the bytes it already read.
    [Test]
    public void HasReadyLine_NoLine_SetsScannedToTheLength()
    {
        File.WriteAllText(_log, new string('x', 10_000));
        UdkLogScan scan = new(_log);

        bool found = scan.HasReadyLine();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(scan.Scanned, Is.EqualTo(10_000));
        }
    }

    // A log shorter than the scanned length is read from the start, because the engine starts a new log on each run.
    [Test]
    public void HasReadyLine_ShorterLog_ReadsFromTheStart()
    {
        File.WriteAllText(_log, new string('x', 10_000));
        UdkLogScan scan = new(_log);
        _ = scan.HasReadyLine();

        File.WriteAllText(_log, UdkLogScan.ReadyLine);

        Assert.That(scan.HasReadyLine(), Is.True);
    }

    // A log that does not exist yet doesn't have a ready line.
    [Test]
    public void HasReadyLine_MissingLog_ReturnsFalse()
    {
        Assert.That(new UdkLogScan(_log).HasReadyLine(), Is.False);
    }
}
