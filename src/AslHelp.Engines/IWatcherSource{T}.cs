using AslHelp.Memory.Watching;

namespace AslHelp.Engines;

/// <summary>
///     Provides a watcher for a value of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public interface IWatcherSource<T> : IWatcherSource
{
    /// <summary>
    ///     Creates a new watcher for the value.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <returns>
    ///     The watcher.
    /// </returns>
    new Watcher<T> CreateWatcher(ITickCounter ticks);
}
