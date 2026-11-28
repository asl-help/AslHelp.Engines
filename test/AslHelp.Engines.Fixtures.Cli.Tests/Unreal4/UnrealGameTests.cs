using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Unreal4;
using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealGameTests
{
    private static readonly UnrealPlayer _player = UnrealPlayer.Parse("win-x64-shipping");
    private static readonly Version _version = new(4, 19, 2);

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

    // An executable of the other platform doesn't count, so a 64-bit player never starts a 32-bit build.
    [Test]
    public void CheckAsync_OnlyOtherPlatform_ThrowsFileNotFoundException()
    {
        string player = _folder.NewFolder();
        WriteBytes(player, "Fixture/Binaries/Win32/Fixture-Win32-Shipping.exe", [1]);

        Assert.That(
            async () => await CheckAsync(new LocalPlayerHost(), player, TimeSpan.FromMinutes(1)),
            Throws.TypeOf<FileNotFoundException>()
                .With.Message.Contains(Path.Combine(player, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.exe")));
    }

    // A canceled token throws before the delete, so the files of the last run stay and no player starts.
    [Test]
    public void CheckAsync_CanceledToken_KeepsTheFilesOfTheLastRun()
    {
        string player = WritePlayer("PING.EXE");
        WriteBytes(player, UnrealGame.LogPath, [1]);
        FakePlayerHost host = new(exitCode: 0, checksBeforeExit: 0, () => { });
        CancellationToken canceled = new(canceled: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UnrealGame.CheckAsync(host, player, _player, _version, TimeSpan.FromMinutes(1), canceled),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(File.Exists(Path.Combine(player, UnrealGame.LogPath)), Is.True);
            Assert.That(host.Starts, Is.Zero);
        }
    }

    // A player that writes the ready file passes, then the file and Fixture/Saved are gone. winver.exe runs until it is stopped.
    [Test]
    [Platform("Win")]
    public async Task CheckAsync_ReadyFileWritten_PassesAndDeletesReadyFileAndSaved()
    {
        string player = WriteNeverEndingPlayer();
        string binaries = Path.Combine(player, "Fixture", "Binaries", "Win64");

        var check = CheckAsync(new LocalPlayerHost(), player, TimeSpan.FromMinutes(1));
        WriteBytes(player, UnrealGame.LogPath, [1]);
        WriteBytes(binaries, "FixtureReady.txt", [1]);
        await check;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(binaries, "FixtureReady.txt")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(player, "Fixture", "Saved")), Is.False);
        }
    }

    // A player that exits throws with the tag, the code and the command to run by hand. ping.exe rejects the arguments with code 1.
    [Test]
    [Platform("Win")]
    public void CheckAsync_PlayerExits_ThrowsWithTheTagCodeAndCommand()
    {
        string player = WritePlayer("PING.EXE");
        string executable = Path.Combine(player, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.exe");

        Assert.That(
            async () => await CheckAsync(new LocalPlayerHost(), player, TimeSpan.FromMinutes(1)),
            Throws.InvalidOperationException
                .With.Message.Contains("win-x64-shipping exited with code 1 (0x00000001)")
                .And.Message.Contains($"Run '{executable} -nullrhi -nosound'"));
    }

    // A player that exits after it wrote its log points at the log.
    [Test]
    public void CheckAsync_PlayerExitsWithLog_PointsAtTheLog()
    {
        string player = WritePlayer("PING.EXE");
        FakePlayerHost host = new(exitCode: 3, checksBeforeExit: 0, () => WriteBytes(player, UnrealGame.LogPath, [1]));

        Assert.That(
            async () => await CheckAsync(host, player, TimeSpan.FromMinutes(1)),
            Throws.InvalidOperationException.With.Message.Contains($"Read '{Path.Combine(player, UnrealGame.LogPath)}'"));
    }

    // A log of an earlier run doesn't count as the log of a player that exits without one, because the check deletes it
    // before the start.
    [Test]
    public void CheckAsync_PlayerExitsWithOldLog_PointsAtTheExecutable()
    {
        string player = WritePlayer("PING.EXE");
        WriteBytes(player, UnrealGame.LogPath, [1]);
        FakePlayerHost host = new(exitCode: 3, checksBeforeExit: 0, () => { });

        Assert.That(
            async () => await CheckAsync(host, player, TimeSpan.FromMinutes(1)),
            Throws.InvalidOperationException.With.Message.Contains("by hand"));
    }

    // The exit code 0xC0000135 throws with the missing DLL as the reason instead of the code alone.
    [Test]
    public void CheckAsync_PlayerExitsWithDllNotFound_ThrowsWithTheReason()
    {
        string player = WritePlayer("PING.EXE");
        FakePlayerHost host = new(exitCode: unchecked((int)0xC0000135), checksBeforeExit: 0, () => { });

        Assert.That(
            async () => await CheckAsync(host, player, TimeSpan.FromMinutes(1)),
            Throws.InvalidOperationException.With.Message.Contains("a DLL is missing (0xC0000135)"));
    }

    // A player that exits right after it wrote the ready file fails, because the player has to run until it is stopped.
    [Test]
    public void CheckAsync_PlayerExitsAfterReadyFile_ThrowsInvalidOperationException()
    {
        string player = WritePlayer("PING.EXE");
        FakePlayerHost host = new(
            exitCode: 3,
            checksBeforeExit: 1,
            () => WriteBytes(player, "Fixture/Binaries/Win64/FixtureReady.txt", [1]));

        Assert.That(
            async () => await CheckAsync(host, player, TimeSpan.FromMinutes(1)),
            Throws.InvalidOperationException.With.Message.Contains("code 3"));
    }

    // A player without the ready file in time and with its log on disk points at the log and prints the limit in seconds.
    [Test]
    [Platform("Win")]
    public void CheckAsync_NoReadyFileInTimeWithLog_PointsAtTheLog()
    {
        string player = WriteNeverEndingPlayer();

        var check = CheckAsync(new LocalPlayerHost(), player, TimeSpan.FromSeconds(1));
        WriteBytes(player, UnrealGame.LogPath, [1]);

        Assert.That(
            async () => await check,
            Throws.TypeOf<TimeoutException>()
                .With.Message.Contains("within 1 s.")
                .And.Message.Contains($"Read '{Path.Combine(player, UnrealGame.LogPath)}'"));
    }

    // A player without the ready file in time throws with its tag, the ready file and the executable.
    [Test]
    [Platform("Win")]
    public void CheckAsync_NoReadyFileInTime_ThrowsTimeoutWithTheTag()
    {
        string player = WriteNeverEndingPlayer();

        Assert.That(
            async () => await CheckAsync(new LocalPlayerHost(), player, TimeSpan.FromSeconds(1)),
            Throws.TypeOf<TimeoutException>()
                .With.Message.Contains("win-x64-shipping")
                .And.Message.Contains("FixtureReady.txt")
                .And.Message.Contains("Fixture-Win64-Shipping.exe"));
    }

    // A ready file of an earlier run doesn't pass a player that never writes one, because the check deletes it before the start.
    [Test]
    [Platform("Win")]
    public void CheckAsync_OldReadyFile_ThrowsTimeoutException()
    {
        string player = WriteNeverEndingPlayer();
        WriteBytes(player, "Fixture/Binaries/Win64/FixtureReady.txt", [1]);

        Assert.That(
            async () => await CheckAsync(new LocalPlayerHost(), player, TimeSpan.FromSeconds(1)),
            Throws.TypeOf<TimeoutException>());
    }

    private static Task CheckAsync(IPlayerHost host, string playerDirectory, TimeSpan timeout)
    {
        return UnrealGame.CheckAsync(host, playerDirectory, _player, _version, timeout, CancellationToken.None);
    }

    private string WriteNeverEndingPlayer()
    {
        string player = WritePlayer("winver.exe");

        // Copy the language files of winver.exe too, because a copy without them exits at once.
        foreach (string language in Directory.EnumerateDirectories(Environment.SystemDirectory))
        {
            string mui = Path.Combine(language, "winver.exe.mui");

            if (File.Exists(mui))
            {
                WriteBytes(player, $"Fixture/Binaries/Win64/{Path.GetFileName(language)}/Fixture-Win64-Shipping.exe.mui", File.ReadAllBytes(mui));
            }
        }

        return player;
    }

    private string WritePlayer(string systemExecutable)
    {
        string player = _folder.NewFolder();
        byte[] executable = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, systemExecutable));
        WriteBytes(player, "Fixture/Binaries/Win64/Fixture-Win64-Shipping.exe", executable);

        return player;
    }
}

// Runs nothing. The run exits with exitCode once HasExited was read checksBeforeExit times.
file sealed class FakePlayerHost(int exitCode, int checksBeforeExit, Action onStart) : IPlayerHost
{
    public int Starts { get; private set; }

    public Task<IHostedRun> StartAsync(string installDirectory, string executablePath, string arguments, CancellationToken cancellationToken)
    {
        Starts++;
        onStart();

        return Task.FromResult<IHostedRun>(new FakePlayerRun(exitCode, checksBeforeExit));
    }
}

file sealed class FakePlayerRun(int exitCode, int checksBeforeExit) : IHostedRun
{
    private int _checks;

    public int ExitCode => exitCode;

    public bool HasExited => _checks++ >= checksBeforeExit;

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
