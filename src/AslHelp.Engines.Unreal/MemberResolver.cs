using AslHelp.Memory.Locating;
using AslHelp.Memory.Watching;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides the hops of the member chains that start at a global of a game and the watchers that read them through a <see cref="MemberPath"/>.
/// </summary>
/// <param name="game">The game to read from.</param>
/// <param name="globalSlot">The slot of the global the chains start at.</param>
internal sealed class MemberResolver(
    UnrealGame game,
    GlobalSlot globalSlot) : IMemberResolver<MemberHop>
{
    /// <inheritdoc/>
    public Watcher CreateWatcher(ITickCounter ticks, IReadOnlyList<MemberHop> hops)
    {
        if (hops.Count < 2)
        {
            throw new InvalidOperationException(SR.FormatPathGlobalUnwatchable(hops[0]));
        }

        return new UnrealInt32Path(new MemberPath(game, globalSlot, [.. hops])).CreateWatcher(ticks);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Doesn't check the name, because the class of the object is only there when the watcher reads.
    /// </remarks>
    public MemberHop ResolveMember(MemberHop tail, string name)
    {
        return new MemberHop(name);
    }
}
