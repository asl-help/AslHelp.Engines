using AslHelp.Memory;
using AslHelp.Memory.Remote;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Runs 1 player process of the live tests with the memory of its executable module.
/// </summary>
internal sealed class StartedPlayer : IDisposable
{
    private const int MaxStarts = 3;
    private const string ReadyFileName = "FixtureReady.txt";

    private static readonly TimeSpan _readyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _readyTimeout = TimeSpan.FromMinutes(2);

    private readonly string _executablePath;

    private Process _process;
    private RemoteProcessMemory _memory;
    private MemoryModule _module;
    private bool _stopped;

    /// <summary>
    ///     Starts the player and waits for its ready file.
    /// </summary>
    /// <param name="executablePath">The path of the executable of the player.</param>
    public StartedPlayer(string executablePath)
    {
        _executablePath = executablePath;
        (_process, _memory, _module) = Open(executablePath);
    }

    /// <summary>
    ///     Runs a test on the memory and the module of the player, after a new start when an earlier test left the player exited.
    /// </summary>
    /// <remarks>
    ///     Warns about the new start, because the exit can hide a crash in that earlier test.
    /// </remarks>
    /// <param name="test">The test.</param>
    public void Run(Action<IMemoryReader, MemoryModule> test)
    {
        // Start again after a start that failed too, because that start left the player stopped.
        if (_stopped || _process.HasExited)
        {
            Assert.Warn("The player exited after an earlier test, so this test starts it again.");
            Dispose();
            (_process, _memory, _module) = Open(_executablePath);
            _stopped = false;
        }

        test(_memory, _module);
    }

    /// <summary>
    ///     Kills the player.
    /// </summary>
    public void Dispose()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _memory.Dispose();
        Stop(_process);
    }

    // Kill the player when its memory or its module doesn't open, because nothing else has the process then.
    private static (Process Process, RemoteProcessMemory Memory, MemoryModule Module) Open(string executablePath)
    {
        var process = Start(executablePath);
        RemoteProcessMemory? memory = null;

        try
        {
            memory = new RemoteProcessMemory(process);

            return (process, memory, memory.GetModule(Path.GetFileName(executablePath)).Unwrap());
        }
        catch
        {
            memory?.Dispose();
            Stop(process);

            throw;
        }
    }

    // Start again up to 3 times, because another test run can use the same player.
    private static Process Start(string executablePath)
    {
        string readyPath = Path.Combine(Path.GetDirectoryName(executablePath)!, ReadyFileName);
        ProcessStartInfo startInfo = new(executablePath, "-nullrhi -nosound") { UseShellExecute = false };

        for (int attempt = 1; ; attempt++)
        {
            var started = DateTime.UtcNow;
            var process = Process.Start(startInfo)!;

            if (WaitForReadyFile(process, readyPath, started))
            {
                return process;
            }

            string message = process.HasExited
                ? $"The player exited before it wrote {ReadyFileName}, {MaxStarts} starts."
                : $"The player didn't write {ReadyFileName} within {_readyTimeout}, {MaxStarts} starts.";

            Stop(process);
            Assert.That(attempt, Is.LessThan(MaxStarts), message);
        }
    }

    // Skip the kill of a player that already exited, because Kill throws for it on .NET Framework.
    private static void Stop(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
        }

        process.WaitForExit();
        process.Dispose();
    }

    // Wait for a ready file written after the start, because the file of an earlier run stays next to the executable.
    // Return false when the player exits before it or doesn't write it in time.
    private static bool WaitForReadyFile(Process process, string readyPath, DateTime started)
    {
        var clock = Stopwatch.StartNew();

        while (!File.Exists(readyPath) || File.GetLastWriteTimeUtc(readyPath) < started)
        {
            if (process.HasExited || clock.Elapsed > _readyTimeout)
            {
                return false;
            }

            Thread.Sleep(_readyPollInterval);
        }

        return true;
    }
}
