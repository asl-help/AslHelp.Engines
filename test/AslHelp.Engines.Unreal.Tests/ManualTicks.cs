using AslHelp.Memory.Watching;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Provides a tick counter that a test moves on by hand.
/// </summary>
internal sealed class ManualTicks : ITickCounter
{
    /// <summary>
    ///     Gets or sets the current tick.
    /// </summary>
    public long Value { get; set; }
}
