using NUnit.Framework;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.Debug;

namespace AslHelp.Engines.Fixtures.Tests;

[Platform("Win")]
[NonParallelizable]
public sealed class PlayerTests
{
    private const uint StatusDllNotFound = 0xC0000135;

    private static readonly TimeSpan _exitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _shortTimeout = TimeSpan.FromMilliseconds(200);

    private uint _callerErrorMode;
    private string _folder = null!;
    private Player? _player;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "AslHelp.Engines.Fixtures.Tests", $"{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_folder);

        _callerErrorMode = PInvoke.GetErrorMode();
    }

    [TearDown]
    public void TearDown()
    {
        _player?.Dispose();
        _ = PInvoke.SetErrorMode((THREAD_ERROR_MODE)_callerErrorMode);

        // Retry the delete, because Dispose waits for the player only and the rest of its tree may still have the
        // folder open for a moment.
        Assert.That(WaitUntil(TryDeleteFolder, _exitTimeout), Is.True);
    }

    // Id is the pid of the started child, which is a live ping.exe.
    [Test]
    public void Start_Executable_RunsProcessWithItsId()
    {
        _player = Player.Start(PingInfo());

        using var process = Process.GetProcessById(_player.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_player.Process.Id, Is.EqualTo(_player.Id));
            Assert.That(_player.HasExited, Is.False);
            Assert.That(process.ProcessName, Is.EqualTo("ping").IgnoreCase);
        }
    }

    // The quoted executable, the argument string and the working folder all reach the child unchanged.
    [Test]
    public void Start_ArgumentsAndWorkingDirectory_ReachTheChild()
    {
        string output = Path.Combine(_folder, "file.txt");

        _player = Player.Start(new PlayerStartInfo(System32("cmd.exe"), $"/c cd > \"{output}\"", _folder));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitUntil(() => _player.HasExited, _exitTimeout), Is.True);
            Assert.That(File.ReadAllText(output).Trim(), Is.EqualTo(_folder));
        }
    }

    // Dispose ends the child before it returns.
    [Test]
    public void Dispose_RunningProcess_EndsItBeforeReturning()
    {
        _player = Player.Start(PingInfo());
        using var process = Process.GetProcessById(_player.Id);

        _player.Dispose();

        Assert.That(process.HasExited, Is.True);
    }

    // A grandchild inherits the job, so Dispose ends it too.
    [Test]
    public void Dispose_RunningGrandchild_EndsItToo()
    {
        string pidFile = Path.Combine(_folder, "pid.txt");
        string powerShell = System32(@"WindowsPowerShell\v1.0\powershell.exe");
        string command = $"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 60";

        _player = Player.Start(new PlayerStartInfo(
            System32("cmd.exe"),
            $"/s /c \"\"{powerShell}\" -NoProfile -NonInteractive -Command \"{command}\"\"",
            _folder));

        int pid = 0;
        Assert.That(WaitUntil(() => TryReadPid(pidFile, out pid), TimeSpan.FromSeconds(30)), Is.True);

        using var grandchild = Process.GetProcessById(pid);
        Assert.That(grandchild.HasExited, Is.False);

        _player.Dispose();

        Assert.That(WaitUntil(() => grandchild.HasExited, _exitTimeout), Is.True);
    }

    // The machine lock is owned by its own thread, so a 2nd Start on the same thread waits and times out.
    [Test]
    public void Start_WhileAnotherPlayerRuns_ThrowsTimeoutException()
    {
        _player = Player.Start(PingInfo());

        Assert.That(
            () => Player.Start(PingInfo() with { LockTimeout = _shortTimeout }),
            Throws.TypeOf<TimeoutException>().With.Message.Contains("Another player"));
    }

    // Dispose gives the machine lock back before it returns.
    [Test]
    public void Start_AfterFirstPlayerDisposed_Succeeds()
    {
        var first = Player.Start(PingInfo());
        first.Dispose();

        _player = Player.Start(PingInfo() with { LockTimeout = _shortTimeout });

        Assert.That(_player.HasExited, Is.False);
    }

    // A lock left behind by a dead holder counts as free.
    [Test]
    public void Start_LockAbandonedByDeadThread_Succeeds()
    {
        using Mutex mutex = new(initiallyOwned: false, PlayerLock.Name);
        Thread holder = new(() => mutex.WaitOne());
        holder.Start();
        holder.Join();

        _player = Player.Start(PingInfo());

        Assert.That(_player.HasExited, Is.False);
    }

    // A file that is not a program fails in CreateProcessW and the failure gives the machine lock back.
    [Test]
    public void Start_ExecutableNotAProgram_ThrowsWin32ExceptionAndReleasesLock()
    {
        string path = Path.Combine(_folder, "file.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.That(
            () => Player.Start(new PlayerStartInfo(path, "", _folder)),
            Throws.TypeOf<Win32Exception>()
                .With.Property(nameof(Win32Exception.NativeErrorCode)).EqualTo(193)
                .And.Message.Contains(path));

        _player = Player.Start(PingInfo() with { LockTimeout = _shortTimeout });
    }

    // The error code of a failed start survives the message lookups of a culture that no lookup used yet.
    [Test]
    [SetUICulture("fr-CA")]
    public void Start_ExecutableNotAProgramUnderNewCulture_ReportsBadExeFormat()
    {
        string path = Path.Combine(_folder, "file.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.That(
            () => Player.Start(new PlayerStartInfo(path, "", _folder)),
            Throws.TypeOf<Win32Exception>().With.Property(nameof(Win32Exception.NativeErrorCode)).EqualTo(193));
    }

    // A missing executable throws FileNotFoundException with its path.
    [Test]
    public void Start_MissingExecutable_ThrowsFileNotFoundException()
    {
        string path = Path.Combine(_folder, "missing.exe");

        Assert.That(
            () => Player.Start(new PlayerStartInfo(path, "", _folder)),
            Throws.TypeOf<FileNotFoundException>()
                .With.Property(nameof(FileNotFoundException.FileName)).EqualTo(path)
                .And.Message.Contains(path));
    }

    // A missing working folder throws with its path instead of a bare Win32 error later.
    [Test]
    public void Start_MissingWorkingDirectory_ThrowsDirectoryNotFoundException()
    {
        string path = Path.Combine(_folder, "missing");

        Assert.That(
            () => Player.Start(new PlayerStartInfo(System32("ping.exe"), "", path)),
            Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(path));
    }

    // A path with characters Windows does not allow throws FileNotFoundException, the same as a missing file.
    [Test]
    public void Start_ExecutablePathWithInvalidCharacters_ThrowsFileNotFoundException()
    {
        string path = _folder + @"\a|b.exe";

        Assert.That(
            () => Player.Start(new PlayerStartInfo(path, "", _folder)),
            Throws.TypeOf<FileNotFoundException>().With.Message.Contains("a|b.exe"));
    }

    // A working folder with characters Windows does not allow throws DirectoryNotFoundException, the same as a missing
    // folder.
    [Test]
    public void Start_WorkingDirectoryWithInvalidCharacters_ThrowsDirectoryNotFoundException()
    {
        string path = _folder + @"\a|b";

        Assert.That(
            () => Player.Start(new PlayerStartInfo(System32("ping.exe"), "", path)),
            Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains("a|b"));
    }

    // A player on a hidden desktop runs and Dispose ends it.
    [Test]
    public void Start_HiddenDesktop_RunsAndDisposeEndsIt()
    {
        _player = Player.Start(PingInfo() with { HiddenDesktop = true });
        using var process = Process.GetProcessById(_player.Id);

        Assert.That(process.HasExited, Is.False);

        _player.Dispose();

        Assert.That(WaitUntil(() => process.HasExited, _exitTimeout), Is.True);
    }

    // A child that ends on its own turns HasExited true and Dispose on the empty job still returns.
    [Test]
    public void HasExited_ChildEndedOnItsOwn_IsTrueAndDisposeCompletes()
    {
        _player = Player.Start(new PlayerStartInfo(System32("cmd.exe"), "/c exit 0", _folder));

        Assert.That(WaitUntil(() => _player.HasExited, _exitTimeout), Is.True);
        Assert.That(_player.Dispose, Throws.Nothing);
    }

    // A 2nd Dispose does nothing.
    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _player = Player.Start(PingInfo());
        _player.Dispose();

        Assert.That(_player.Dispose, Throws.Nothing);
    }

    // Player disposes its Process, so a read after Dispose throws while Id and HasExited still return their values.
    [Test]
    public void Process_AfterDispose_ThrowsObjectDisposedException()
    {
        _player = Player.Start(PingInfo());
        int id = _player.Id;
        _player.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => _player.Process, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(_player.Id, Is.EqualTo(id));
            Assert.That(_player.HasExited, Is.True);
        }
    }

    // A player with a missing import ends with STATUS_DLL_NOT_FOUND within seconds instead of waiting on a dialog, even
    // when the caller runs with the dialogs on.
    [Test]
    public void Start_ExecutableWithMissingImport_ExitsWithDllNotFound()
    {
        string executable = MissingImportExecutable.Create(_folder);
        TurnErrorDialogsOn();

        _player = Player.Start(new PlayerStartInfo(executable, "", _folder));

        AssertExitsWithDllNotFound(_player);
    }

    // A player on a hidden desktop, where nobody can click a dialog, ends with STATUS_DLL_NOT_FOUND the same way.
    [Test]
    public void Start_HiddenDesktopWithMissingImport_ExitsWithDllNotFound()
    {
        string executable = MissingImportExecutable.Create(_folder);
        TurnErrorDialogsOn();

        _player = Player.Start(new PlayerStartInfo(executable, "", _folder) { HiddenDesktop = true });

        AssertExitsWithDllNotFound(_player);
    }

    // A process the player starts inherits the error mode, so a missing import 2 levels down ends the tree the same
    // way.
    [Test]
    public void Start_GrandchildWithMissingImport_ExitsWithDllNotFound()
    {
        string executable = MissingImportExecutable.Create(_folder);
        TurnErrorDialogsOn();

        _player = Player.Start(new PlayerStartInfo(System32("cmd.exe"), $"/c \"{executable}\"", _folder));

        AssertExitsWithDllNotFound(_player);
    }

    // Start puts the error mode of the caller back as it was, with no flag added or dropped.
    [Test]
    public void Start_CallerWithOneFlagSet_KeepsOnlyThatFlag()
    {
        _ = PInvoke.SetErrorMode(THREAD_ERROR_MODE.SEM_NOOPENFILEERRORBOX);

        _player = Player.Start(PingInfo());

        Assert.That(PInvoke.GetErrorMode(), Is.EqualTo((uint)THREAD_ERROR_MODE.SEM_NOOPENFILEERRORBOX));
    }

    // A CreateProcessW failure puts the caller's error mode back and still reports the error of CreateProcessW.
    [Test]
    public void Start_ExecutableNotAProgram_KeepsCallerErrorModeAndReportsBadExeFormat()
    {
        string path = Path.Combine(_folder, "file.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);
        TurnErrorDialogsOn();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => Player.Start(new PlayerStartInfo(path, "", _folder)),
                Throws.TypeOf<Win32Exception>().With.Property(nameof(Win32Exception.NativeErrorCode)).EqualTo(193));
            Assert.That(PInvoke.GetErrorMode(), Is.Zero);
        }
    }

    private static string System32(string file)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), file);
    }

    private PlayerStartInfo PingInfo()
    {
        return new PlayerStartInfo(System32("ping.exe"), "-n 60 127.0.0.1", _folder) { LockTimeout = TimeSpan.FromSeconds(2) };
    }

    private static void TurnErrorDialogsOn()
    {
        // Clear every flag, because a test host that already runs with the dialogs off would pass these tests without
        // the error mode Start gives the player. TearDown puts the old mode back.
        _ = PInvoke.SetErrorMode(THREAD_ERROR_MODE.SEM_ALL_ERRORS);
    }

    private static void AssertExitsWithDllNotFound(Player player)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitUntil(() => player.HasExited, _exitTimeout), Is.True);
            Assert.That(unchecked((uint)player.Process.ExitCode), Is.EqualTo(StatusDllNotFound));
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

    private bool TryDeleteFolder()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryReadPid(string path, out int pid)
    {
        pid = 0;

        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out pid);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
