using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Provides the hops of the member chains that start at a root object of a game
///     and the watchers that read them through a <see cref="MemberPath"/>.
/// </summary>
/// <param name="classReader">The reader of the game to read from.</param>
/// <param name="root">The object the chains start at.</param>
internal sealed class MemberResolver(
    ClassReader classReader,
    PathRoot root) : IMemberResolver<MemberHop>
{
    /// <summary>
    ///     Creates the watcher that reads the value at the end of <paramref name="hops"/> as the type of the kind of its property.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Returns a <see cref="Watcher{T}"/> of <see cref="object"/>, because a read finds the class of the object and the kind of its member.
    ///         <see cref="Watcher{T}.Current"/> is then the value in its own type, like an <see cref="int"/> for an <c>IntProperty</c>.
    ///     </para>
    ///     <para>
    ///         Before the 1st read that succeeds, <see cref="Watcher{T}.Current"/> and <see cref="Watcher{T}.Old"/> are <see langword="null"/>
    ///         and <see cref="Watcher.Changed"/> is <see langword="false"/>, unless <see cref="FailAction.Throw"/> throws.
    ///         When the class changes to one whose member has another kind, <see cref="Watcher{T}.Old"/> keeps the value of the old type
    ///         and <see cref="Watcher.Changed"/> is <see langword="true"/>.
    ///         A value that doesn't change keeps its box, so a steady read doesn't allocate.
    ///     </para>
    /// </remarks>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <param name="hops">The hops of the chain, from the root to the watched member.</param>
    /// <returns>
    ///     The watcher.
    /// </returns>
    /// <exception cref="InvalidOperationException">The chain is the root alone.</exception>
    public Watcher CreateWatcher(ITickCounter ticks, IReadOnlyList<MemberHop> hops)
    {
        if (hops.Count < 2)
        {
            throw new InvalidOperationException(SR.FormatPathRootUnwatchable(hops[0]));
        }

        return new ChainWatcher(ticks, new MemberPath(classReader, root, [.. hops]));
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Doesn't check the name, because the watcher finds the class of the object when it reads.
    /// </remarks>
    public MemberHop ResolveMember(MemberHop tail, string name)
    {
        return new MemberHop(name);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Takes an integer for an array index or a key, a <see cref="bool"/> or a <see cref="string"/> for a key.
    ///     Doesn't check the collection, because the watcher finds the class of the object when it reads.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="index"/> isn't an integer, a <see cref="bool"/> or a <see cref="string"/>.
    /// </exception>
    public MemberHop ResolveIndex(MemberHop tail, object index)
    {
        return ElementKey.IsKeyType(index)
            ? MemberHop.Index(index)
            : throw new InvalidOperationException(SR.FormatPathIndexTypeUnsupported(tail.Name, index.GetType().Name));
    }
}

file sealed class ChainWatcher(
    ITickCounter ticks,
    MemberPath memberPath) : Watcher<object?>(ticks)
{
    // Keep the last value apart from Current, because Clear empties Current and a read returns the last box when the value is equal.
    private object? _last;

    protected override Result<object?> Read()
    {
        var result = memberPath.ReadBoxed(_last);

        if (result.IsOk)
        {
            _last = result.Value;
        }

        return result;
    }

    public override string ToString()
    {
        return memberPath.ToString();
    }
}
