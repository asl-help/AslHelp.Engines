using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Gives timestamps and timers that move only on <see cref="Advance"/>.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private static readonly TimeSpan _hangLimit = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];

    private long _timestamp;

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override long GetTimestamp()
    {
        return _timestamp;
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        _ = timer.Change(dueTime, period);

        return timer;
    }

    /// <summary>
    ///     Moves the clock forward by <paramref name="time"/> and fires each timer that is due by then on the calling thread.
    /// </summary>
    /// <param name="time">The time to add.</param>
    public void Advance(TimeSpan time)
    {
        ManualTimer[] timers;

        lock (_gate)
        {
            _timestamp += time.Ticks;
            timers = [.. _timers];
        }

        // Fire from a copy, because a callback can dispose a timer, which removes it from the list.
        foreach (var timer in timers)
        {
            timer.FireIfDue();
        }
    }

    /// <summary>
    ///     Moves the clock forward by <paramref name="time"/> and then waits until <paramref name="cancellationToken"/> is canceled,
    ///     like a network wait that moves no bytes.
    /// </summary>
    /// <remarks>
    ///     The clock moves before the wait, because <see cref="Advance"/> fires a due time limit on the calling thread
    ///     and the limit cancels the token.
    /// </remarks>
    /// <param name="time">The time the wait takes on this clock.</param>
    /// <param name="cancellationToken">The token that ends the wait.</param>
    /// <returns>
    ///     A task that never completes successfully.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="cancellationToken"/> is not canceled within 10 seconds of real time, so a limit that never fires
    ///     fails the test instead of hanging it.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task StallAsync(TimeSpan time, CancellationToken cancellationToken)
    {
        Advance(time);
        await Task.Delay(_hangLimit, cancellationToken);

        throw new InvalidOperationException("The stall was not canceled within 10 seconds.");
    }

    /// <summary>
    ///     Calls its callback once when the clock reaches its due time.
    /// </summary>
    /// <param name="clock">The clock that fires the timer.</param>
    /// <param name="callback">The callback to call.</param>
    /// <param name="state">The value to pass to <paramref name="callback"/>.</param>
    private sealed class ManualTimer(
        ManualTimeProvider clock,
        TimerCallback callback,
        object? state) : ITimer
    {
        private long? _dueTimestamp;

        /// <inheritdoc/>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                if (!clock._timers.Contains(this))
                {
                    return false;
                }

                // Ignore the period, because the timer of a CancellationTokenSource fires once.
                _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan ? null : clock._timestamp + dueTime.Ticks;

                return true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (clock._gate)
            {
                _ = clock._timers.Remove(this);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }

        /// <summary>
        ///     Calls the callback if the clock has reached the due time and clears the due time first, so the timer fires once.
        /// </summary>
        public void FireIfDue()
        {
            lock (clock._gate)
            {
                if (_dueTimestamp is not { } due || due > clock._timestamp)
                {
                    return;
                }

                _dueTimestamp = null;
            }

            callback(state);
        }
    }
}
