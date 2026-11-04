using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;

namespace AslHelp.Engines.Fixtures.Tests;

[Platform("Win")]
[NonParallelizable]
public sealed class PlayerEdgeCaseTests
{
    private const int KillOnJobCloseExitCode = 0;

    private static readonly TimeSpan _exitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _shortTimeout = TimeSpan.FromMilliseconds(200);

    private string _folder = null!;
    private Player? _player;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "AslHelp.Engines.Fixtures.Tests", $"{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        _player?.Dispose();

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // An executable path and a working folder with spaces and non-ASCII letters reach CreateProcessW intact.
    [Test]
    public void Start_UnicodePathsWithSpaces_RunsInWorkingDirectory()
    {
        string playerFolder = Path.Combine(_folder, "pläyer ü 文件");
        string workingDirectory = Path.Combine(_folder, "wörk ñ 目录");
        _ = Directory.CreateDirectory(playerFolder);
        _ = Directory.CreateDirectory(workingDirectory);

        string executable = Path.Combine(playerFolder, "plåyer.exe");
        File.Copy(System32("cmd.exe"), executable);

        _player = Player.Start(new PlayerStartInfo(executable, "/c mkdir \"子 ü\"", workingDirectory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitUntil(() => _player.HasExited, _exitTimeout), Is.True);
            Assert.That(Directory.Exists(Path.Combine(workingDirectory, "子 ü")), Is.True);
        }
    }

    // Relative paths resolve against the current folder of the caller, not against the working folder.
    [Test]
    public void Start_RelativePaths_ResolveAgainstCurrentDirectory()
    {
        string sub = Path.Combine(_folder, "sub");
        _ = Directory.CreateDirectory(sub);
        File.Copy(System32("cmd.exe"), Path.Combine(sub, "player.exe"));

        string previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = _folder;

        try
        {
            _player = Player.Start(new PlayerStartInfo(@"sub\player.exe", "/c mkdir created", "sub"));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitUntil(() => _player.HasExited, _exitTimeout), Is.True);
            Assert.That(Directory.Exists(Path.Combine(sub, "created")), Is.True);
        }
    }

    // A command line above the 32767-char limit of CreateProcessW fails and gives the machine lock back.
    [Test]
    public void Start_ArgumentsAboveCommandLineLimit_ThrowsWin32ExceptionAndReleasesLock()
    {
        string executable = System32("ping.exe");
        string arguments = new('a', 40_000);

        Assert.That(
            () => Player.Start(new PlayerStartInfo(executable, arguments, _folder) { LockTimeout = _shortTimeout }),
            Throws.TypeOf<Win32Exception>().With.Message.Contains(executable));

        _player = Player.Start(PingInfo() with { LockTimeout = _shortTimeout });
    }

    // A folder passed as the executable throws FileNotFoundException with the folder path.
    [Test]
    public void Start_ExecutableIsFolder_ThrowsFileNotFoundException()
    {
        Assert.That(
            () => Player.Start(new PlayerStartInfo(_folder, "", _folder)),
            Throws.TypeOf<FileNotFoundException>().With.Property(nameof(FileNotFoundException.FileName)).EqualTo(_folder));
    }

    // A file passed as the working folder throws DirectoryNotFoundException with the file path.
    [Test]
    public void Start_WorkingDirectoryIsFile_ThrowsDirectoryNotFoundException()
    {
        string path = Path.Combine(_folder, "file.bin");
        File.WriteAllBytes(path, [1]);

        Assert.That(
            () => Player.Start(new PlayerStartInfo(System32("ping.exe"), "", path)),
            Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(path));
    }

    // A zero timeout takes a free lock without waiting.
    [Test]
    public void Start_ZeroLockTimeoutWhileFree_Succeeds()
    {
        _player = Player.Start(PingInfo() with { LockTimeout = TimeSpan.Zero });

        Assert.That(_player.HasExited, Is.False);
    }

    // A lock taken by another thread refuses the start before the child process exists.
    [Test]
    public void Start_LockTakenElsewhere_ThrowsTimeoutExceptionWithoutRunningChild()
    {
        string marker = Path.Combine(_folder, "started");

        using Mutex mutex = new(initiallyOwned: false, PlayerLock.Name);
        using ManualResetEventSlim taken = new();
        using ManualResetEventSlim release = new();

        Thread holder = new(() =>
        {
            _ = mutex.WaitOne();
            taken.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });

        holder.Start();

        try
        {
            taken.Wait();

            Assert.That(
                () => Player.Start(new PlayerStartInfo(System32("cmd.exe"), "/c mkdir started", _folder) { LockTimeout = _shortTimeout }),
                Throws.TypeOf<TimeoutException>());

            Thread.Sleep(500);

            Assert.That(Directory.Exists(marker), Is.False);
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }

    // A timed out start leaves no lock behind, so the next start after the first Dispose succeeds.
    [Test]
    public void Start_AfterTimeoutAndDispose_Succeeds()
    {
        var first = Player.Start(PingInfo());

        try
        {
            Assert.That(() => Player.Start(PingInfo() with { LockTimeout = _shortTimeout }), Throws.TypeOf<TimeoutException>());
        }
        finally
        {
            first.Dispose();
        }

        _player = Player.Start(PingInfo() with { LockTimeout = _shortTimeout });

        Assert.That(_player.HasExited, Is.False);
    }

    // The lock thread owns the mutex, so Dispose on a thread other than the Start thread gives the lock back.
    [Test]
    public async Task Dispose_OnAnotherThread_ReleasesLock()
    {
        var first = Player.Start(PingInfo());

        await Task.Run(first.Dispose);

        _player = await Task.Run(() => Player.Start(PingInfo() with { LockTimeout = _shortTimeout }));

        Assert.That(_player.HasExited, Is.False);
    }

    // A start that waits gets the lock as soon as the running player is disposed.
    [Test]
    public void Start_WaitingWhenFirstDisposed_AcquiresLock()
    {
        var first = Player.Start(PingInfo());

        var waiter = Task.Run(() => Player.Start(PingInfo() with { LockTimeout = TimeSpan.FromSeconds(10) }));

        Thread.Sleep(300);
        bool waitedForLock = !waiter.IsCompleted;
        first.Dispose();

        _player = waiter.GetAwaiter().GetResult();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(waitedForLock, Is.True);
            Assert.That(_player.HasExited, Is.False);
        }
    }

    // 2 starts that race on 2 threads end with 1 player and 1 TimeoutException.
    [Test]
    public void Start_TwoThreadsRace_OnlyOneSucceeds()
    {
        using Barrier barrier = new(2);
        List<Player> players = [];
        List<Exception> errors = [];
        object gate = new();

        void race()
        {
            _ = barrier.SignalAndWait(_exitTimeout);

            try
            {
                var player = Player.Start(PingInfo() with { LockTimeout = TimeSpan.FromMilliseconds(500) });
                lock (gate)
                {
                    players.Add(player);
                }
            }
            catch (TimeoutException exception)
            {
                lock (gate)
                {
                    errors.Add(exception);
                }
            }
        }

        Thread a = new(race);
        Thread b = new(race);
        a.Start();
        b.Start();
        a.Join();
        b.Join();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(players, Has.Count.EqualTo(1));
                Assert.That(errors, Has.Count.EqualTo(1));
                Assert.That(errors, Has.All.TypeOf<TimeoutException>());
            }
        }
        finally
        {
            foreach (var player in players)
            {
                player.Dispose();
            }
        }
    }

    // A hidden player runs on a desktop of its own that Dispose closes.
    [Test]
    public void Start_HiddenDesktop_CreatesDesktopThatDisposeRemoves()
    {
        var before = PlayerDesktops();

        _player = Player.Start(PingInfo() with { HiddenDesktop = true });

        var during = PlayerDesktops();
        during.ExceptWith(before);

        _player.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(during, Has.Count.EqualTo(1));
            Assert.That(WaitUntil(() => !PlayerDesktops().Overlaps(during), _exitTimeout), Is.True);
        }
    }

    // A player on the default desktop creates no desktop.
    [Test]
    public void Start_VisibleDesktop_CreatesNoDesktop()
    {
        var before = PlayerDesktops();

        _player = Player.Start(PingInfo());

        var during = PlayerDesktops();
        during.ExceptWith(before);

        Assert.That(during, Is.Empty);
    }

    // Dispose only closes the job, so the kill-on-close limit that a crashed caller relies on ends the child.
    [Test]
    public void Dispose_RunningProcess_EndsItThroughJobClose()
    {
        _player = Player.Start(PingInfo());
        using var process = Process.GetProcessById(_player.Id);
        _ = process.Handle;

        _player.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitUntil(() => process.HasExited, _exitTimeout), Is.True);
            Assert.That(process.ExitCode, Is.EqualTo(KillOnJobCloseExitCode));
        }
    }

    // Concurrent Dispose calls end the player once and none of them throws.
    [Test]
    public void Dispose_ConcurrentCalls_DoNotThrow()
    {
        var player = Player.Start(PingInfo());
        _player = player;

        using Barrier barrier = new(4);
        List<Exception> errors = [];
        var threads = new Thread[4];

        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                _ = barrier.SignalAndWait(_exitTimeout);

                try
                {
                    player.Dispose();
                }
                catch (ObjectDisposedException exception)
                {
                    lock (errors)
                    {
                        errors.Add(exception);
                    }
                }
            });

            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.That(errors, Is.Empty);
    }

    // Dispose waits on a handle of its own, so a Process that the caller disposed does not break it.
    [Test]
    public void Dispose_ProcessDisposedByCaller_EndsPlayer()
    {
        _player = Player.Start(PingInfo());
        using var process = Process.GetProcessById(_player.Id);

        _player.Process.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_player.HasExited, Is.False);
            Assert.That(_player.Dispose, Throws.Nothing);
            Assert.That(process.HasExited, Is.True);
        }
    }

    // Process keeps the handle from before the child ran, so the exit code of a child that ended on its own is readable.
    [Test]
    public void Process_ChildEndedOnItsOwn_ReportsExitCode()
    {
        _player = Player.Start(new PlayerStartInfo(System32("cmd.exe"), "/c exit 7", _folder));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitUntil(() => _player.HasExited, _exitTimeout), Is.True);
            Assert.That(_player.Process.ExitCode, Is.EqualTo(7));
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

    private static HashSet<string> PlayerDesktops()
    {
        HashSet<string> desktops = [];

        _ = PInvoke.EnumDesktops(
            PInvoke.GetProcessWindowStation(),
            (name, parameter) =>
            {
                string desktop = name.ToString();
                if (desktop.StartsWith(Player.DesktopPrefix, StringComparison.Ordinal))
                {
                    _ = desktops.Add(desktop);
                }

                return true;
            },
            default);

        return desktops;
    }
}
