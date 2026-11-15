using AslHelp.Memory.Watching;

namespace AslHelp.Engines;

/// <summary>
///     Provides a watcher for the value an object describes, so a host can register the object itself as a watcher.
/// </summary>
public interface IWatcherSource
{
    /// <summary>
    ///     Creates a new watcher for the described value.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <returns>
    ///     The watcher.
    /// </returns>
    Watcher CreateWatcher(ITickCounter ticks);
}
