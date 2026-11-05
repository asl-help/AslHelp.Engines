using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkServerTests
{
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

    // A missing UDK.exe throws with its path before anything starts, so the log of the last run stays.
    [Test]
    public void CheckAsync_MissingServer_ThrowsFileNotFoundExceptionAndKeepsTheLog()
    {
        string install = WriteInstall(_folder, DefaultIni);
        WriteBytes(install, "UDKGame/Content/Maps/Examples/ExampleMap.udk", [1]);
        WriteBytes(install, "UDKGame/Logs/Launch.log", [1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkServer.CheckAsync(new LocalPlayerHost(), install, UdkServer.DefaultCheckTimeout, CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>().With.Message.Contains(Path.Combine(install, "Binaries", "Win32", "UDK.exe")));
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Logs", "Launch.log")), Is.True);
        }
    }

    // An install without a map throws with the maps folder, because the server needs a map to start.
    [Test]
    [Platform("Win")]
    public void CheckAsync_NoMap_ThrowsFileNotFoundException()
    {
        string install = WriteFakeInstallWithServer("PING.EXE");

        Assert.That(
            async () => await UdkServer.CheckAsync(new LocalPlayerHost(), install, UdkServer.DefaultCheckTimeout, CancellationToken.None),
            Throws.TypeOf<FileNotFoundException>().With.Message.Contains(Path.Combine(install, "UDKGame", "Content", "Maps")));
    }

    // A ready line in the old log does not count, because the log is deleted before the start. ping.exe rejects the server arguments with code 1.
    [Test]
    [Platform("Win")]
    public void CheckAsync_OldLogWithReadyLine_DeletesItBeforeTheStart()
    {
        string install = WriteFakeInstallWithServer("PING.EXE");
        WriteBytes(install, "UDKGame/Content/Maps/Examples/ExampleMap.udk", [1]);
        WriteBytes(install, "UDKGame/Logs/Launch.log", Encoding.ASCII.GetBytes("[0001.00] ScriptLog: Fixture ready\r\n"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkServer.CheckAsync(new LocalPlayerHost(), install, UdkServer.DefaultCheckTimeout, CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("code 1 (0x00000001)"));
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Logs", "Launch.log")), Is.False);
        }
    }

    // A server that exits without a log leaves Launch.log out of the message, because the server did not get as far as
    // writing it.
    [Test]
    [Platform("Win")]
    public void CheckAsync_ServerExitsBeforeReadyLineWithoutLog_ThrowsWithoutTheLogPath()
    {
        string install = WriteFakeInstallWithServer("PING.EXE");
        WriteBytes(install, "UDKGame/Content/Maps/Examples/ExampleMap.udk", [1]);

        Assert.That(
            async () => await UdkServer.CheckAsync(new LocalPlayerHost(), install, UdkServer.DefaultCheckTimeout, CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("code 1 (0x00000001)")
                .And.Message.Contains("did not start")
                .And.Message.Not.Contains("Launch.log"));
    }

    // A server that writes no log within the timeout throws with the start hint and not the log path. winver.exe ignores
    // the arguments and runs until it is stopped.
    [Test]
    [Platform("Win")]
    public void CheckAsync_ServerWritesNoLog_ThrowsTimeoutExceptionWithStartHint()
    {
        string install = WriteFakeInstallWithNeverEndingServer();

        Assert.That(
            async () => await UdkServer.CheckAsync(new LocalPlayerHost(), install, TimeSpan.FromSeconds(1), CancellationToken.None),
            Throws.TypeOf<TimeoutException>().With.Message.Contains("did not start").And.Message.Not.Contains("Launch.log"));
    }

    // A server whose log lacks the ready line throws with the log path once the timeout is over. CheckAsync starts the
    // server before it returns its task, so the log written after the call is the log of this run.
    [Test]
    [Platform("Win")]
    public async Task CheckAsync_LogWithoutReadyLine_ThrowsTimeoutExceptionWithLogPath()
    {
        string install = WriteFakeInstallWithNeverEndingServer();

        var check = UdkServer.CheckAsync(new LocalPlayerHost(), install, TimeSpan.FromSeconds(1), CancellationToken.None);
        WriteBytes(install, "UDKGame/Logs/Launch.log", "Init: Version: 1"u8.ToArray());

        await Assert.ThatAsync(
            () => check,
            Throws.TypeOf<TimeoutException>().With.Message.Contains(Path.Combine(install, "UDKGame", "Logs", "Launch.log")));
    }

    // A passing check deletes the log once the server is gone, because the install becomes the shared player and the log
    // has the machine name, the user and the paths of the build machine.
    [Test]
    [Platform("Win")]
    public async Task CheckAsync_ReadyLine_PassesAndDeletesTheLog()
    {
        string install = WriteFakeInstallWithNeverEndingServer();
        string log = Path.Combine(install, "UDKGame", "Logs", "Launch.log");

        var check = UdkServer.CheckAsync(new LocalPlayerHost(), install, TimeSpan.FromMinutes(1), CancellationToken.None);
        WriteBytes(install, "UDKGame/Logs/Launch.log", "[0044.79] ScriptLog: Fixture ready"u8.ToArray());
        await check;

        Assert.That(File.Exists(log), Is.False);
    }

    // A canceled token throws before the delete, so the log of the last run stays and no server starts.
    [Test]
    [Platform("Win")]
    public void CheckAsync_CanceledToken_ThrowsOperationCanceledExceptionAndKeepsTheLog()
    {
        string install = WriteFakeInstallWithServer("PING.EXE");
        WriteBytes(install, "UDKGame/Content/Maps/Examples/ExampleMap.udk", [1]);
        WriteBytes(install, "UDKGame/Logs/Launch.log", [1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkServer.CheckAsync(
                    new LocalPlayerHost(),
                    install,
                    UdkServer.DefaultCheckTimeout,
                    new CancellationToken(canceled: true)),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Logs", "Launch.log")), Is.True);
        }
    }

    // A ready line split between 2 reads still matches, because each read starts a little before the end of the last one.
    [Test]
    public void HasReadyLine_LineSplitBetweenReads_FindsTheLine()
    {
        string log = Path.Combine(_folder.NewFolder(), "file.log");
        File.WriteAllText(log, "[0001.00] ScriptLog: Fixt");
        long scanned = 0;

        bool first = UdkServer.HasReadyLine(log, ref scanned);
        File.AppendAllText(log, "ure ready\r\n");
        bool second = UdkServer.HasReadyLine(log, ref scanned);

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
        string log = Path.Combine(_folder.NewFolder(), "file.log");
        File.WriteAllText(log, new string('x', 4090) + "ScriptLog: Fixture ready");
        long scanned = 0;

        Assert.That(UdkServer.HasReadyLine(log, ref scanned), Is.True);
    }

    // A miss moves the scanned length to the end of the log, so the next read skips the bytes it already read.
    [Test]
    public void HasReadyLine_NoLine_SetsScannedToTheLength()
    {
        string log = Path.Combine(_folder.NewFolder(), "file.log");
        File.WriteAllText(log, new string('x', 10_000));
        long scanned = 0;

        bool found = UdkServer.HasReadyLine(log, ref scanned);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(scanned, Is.EqualTo(10_000));
        }
    }

    // A log shorter than the scanned length is read from the start, because the engine starts a new log on each run.
    [Test]
    public void HasReadyLine_ShorterLog_ReadsFromTheStart()
    {
        string log = Path.Combine(_folder.NewFolder(), "file.log");
        File.WriteAllText(log, "ScriptLog: Fixture ready");
        long scanned = 10_000;

        Assert.That(UdkServer.HasReadyLine(log, ref scanned), Is.True);
    }

    // A log that does not exist yet has no ready line.
    [Test]
    public void HasReadyLine_MissingLog_ReturnsFalse()
    {
        string log = Path.Combine(_folder.NewFolder(), "file.log");
        long scanned = 0;

        Assert.That(UdkServer.HasReadyLine(log, ref scanned), Is.False);
    }

    private string WriteFakeInstallWithNeverEndingServer()
    {
        string install = WriteFakeInstallWithServer("winver.exe");

        // Copy the language files of winver.exe too, because a copy without them exits at once.
        foreach (string language in Directory.EnumerateDirectories(Environment.SystemDirectory))
        {
            string mui = Path.Combine(language, "winver.exe.mui");

            if (File.Exists(mui))
            {
                WriteBytes(install, $"Binaries/Win32/{Path.GetFileName(language)}/UDK.exe.mui", File.ReadAllBytes(mui));
            }
        }

        WriteBytes(install, "UDKGame/Content/Maps/Examples/ExampleMap.udk", [1]);

        return install;
    }

    private string WriteFakeInstallWithServer(string systemExecutable)
    {
        string install = WriteInstall(_folder, DefaultIni);
        File.Copy(Path.Combine(Environment.SystemDirectory, systemExecutable), Path.Combine(install, "Binaries", "Win32", "UDK.exe"));

        return install;
    }
}
