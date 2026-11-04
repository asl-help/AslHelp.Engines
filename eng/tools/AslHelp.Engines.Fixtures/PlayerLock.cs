using System;
using System.Threading;

namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Represents the machine-wide lock that lets 1 player run at a time.
/// </summary>
/// <remarks>
///     The lock takes the mutex on a thread of its own, because a mutex belongs to the thread that took it and
///     <see cref="Dispose"/> may run on another thread. The holder thread also makes a 2nd lock in the same process
///     wait instead of re-entering the mutex.
///     <para/>
///     The lock uses a mutex instead of a global semaphore, because a holder that dies leaves a mutex abandoned and the
///     next waiter gets it, while a semaphore count stays down for as long as any other process keeps a handle to the
///     semaphore.
///     Every waiter keeps such a handle.
/// </remarks>
internal sealed class PlayerLock : IDisposable
{
    /// <summary>
    ///     The name of the machine-wide mutex.
    /// </summary>
    internal const string Name = @"Global\AslHelp.Engines.Fixtures.Player";

    private readonly Thread _holder;
    private readonly Mutex _mutex;
    private readonly ManualResetEventSlim _release = new();
    private readonly TimeSpan _timeout;
    private readonly ManualResetEventSlim _waited = new();

    private bool _disposed;
    private bool _taken;

    private PlayerLock(TimeSpan timeout)
    {
        _mutex = new Mutex(initiallyOwned: false, Name);
        _timeout = timeout;
        _holder = new Thread(Hold) { IsBackground = true, Name = "player lock" };
    }

    /// <summary>
    ///     Takes the lock and keeps it until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="timeout">How long to wait for another player to give the lock back.</param>
    /// <returns>
    ///     The taken lock.
    /// </returns>
    /// <exception cref="TimeoutException">Another player kept the lock past <paramref name="timeout"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The lock exists under another user account.</exception>
    public static PlayerLock Acquire(TimeSpan timeout)
    {
        PlayerLock playerLock = new(timeout);
        playerLock._holder.Start();
        playerLock._waited.Wait();

        if (playerLock._taken)
        {
            return playerLock;
        }

        playerLock.Dispose();

        throw new TimeoutException(SR.Format(SR.LockTimedOut, timeout));
    }

    /// <summary>
    ///     Gives the lock back and returns once it is free.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _release.Set();
        _holder.Join();

        _mutex.Dispose();
        _waited.Dispose();
        _release.Dispose();
    }

    private void Hold()
    {
        _taken = Wait();
        _waited.Set();

        if (!_taken)
        {
            return;
        }

        _release.Wait();
        _mutex.ReleaseMutex();
    }

    private bool Wait()
    {
        try
        {
            return _mutex.WaitOne(_timeout);
        }
        catch (AbandonedMutexException)
        {
            // Count an abandoned mutex as taken, because its holder died and the wait gave it to this thread.
            return true;
        }
    }
}
