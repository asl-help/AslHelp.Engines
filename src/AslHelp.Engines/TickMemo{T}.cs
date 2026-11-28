using AslHelp.Memory.Watching;
using System;
using System.Diagnostics.CodeAnalysis;

namespace AslHelp.Engines;

/// <summary>
///     Keeps 1 value for 1 tick of 1 tick counter, like the object that the shared steps of many paths reach.
/// </summary>
/// <remarks>
///     <para>
///         An engine keeps 1 memo per step of its paths and lets every watcher of a tick counter go through it,
///         so the watchers of 1 counter read a shared step once per tick.
///     </para>
///     <para>
///         A value only comes back for the counter it was set with, while that counter is at the same tick.
///         A set with another counter replaces the value, so 2 counters never get each other's values.
///         A counter that goes back to the tick of the value gets it again, like a <see cref="Watcher"/> keeps its read.
///         Isn't thread-safe.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
public sealed class TickMemo<T>
{
    private long _tick;
    private ITickCounter? _ticks;
    private T? _value;

    /// <summary>
    ///     Keeps <paramref name="value"/> for the current tick of <paramref name="ticks"/>.
    /// </summary>
    /// <param name="ticks">The tick counter of the read.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> is <see langword="null"/>.
    /// </exception>
    public void Set(ITickCounter ticks, T value)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        _tick = ticks.Value;
        _ticks = ticks;
        _value = value;
    }

    /// <summary>
    ///     Gets the value kept for the current tick of <paramref name="ticks"/>.
    /// </summary>
    /// <param name="ticks">The tick counter of the read.</param>
    /// <param name="value">The value or the default value.</param>
    /// <returns>
    ///     <see langword="true"/> when the value was set with <paramref name="ticks"/> at its current tick.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> is <see langword="null"/>.
    /// </exception>
    public bool TryGet(ITickCounter ticks, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        if (ReferenceEquals(_ticks, ticks) && _tick == ticks.Value)
        {
            value = _value!;

            return true;
        }

        value = default;

        return false;
    }
}
