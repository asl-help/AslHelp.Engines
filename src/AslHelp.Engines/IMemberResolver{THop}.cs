using AslHelp.Memory.Watching;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines;

/// <summary>
///     Provides the hops of a <see cref="MemberChain{THop}"/> and the watcher that reads the value at its end.
/// </summary>
/// <typeparam name="THop">
///     The engine's type for 1 hop of a chain. Its text is how a script writes the hop, with its separator, like
///     <c>.Health</c>.
/// </typeparam>
public interface IMemberResolver<THop>
    where THop : class
{
    /// <summary>
    ///     Creates the watcher that reads the value at the end of <paramref name="hops"/>.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <param name="hops">
    ///     The hops of the chain, from the root to the watched member. The list belongs to the chain and stays as it is, so
    ///     the watcher can keep it.
    /// </param>
    /// <returns>
    ///     The watcher, typed after the member at the end.
    /// </returns>
    /// <exception cref="InvalidOperationException">The member at the end isn't a value a watcher can read.</exception>
    Watcher CreateWatcher(ITickCounter ticks, IReadOnlyList<THop> hops);

    /// <summary>
    ///     Finds the member <paramref name="name"/> of the type that <paramref name="tail"/> ends on.
    /// </summary>
    /// <param name="tail">The last hop of the chain so far.</param>
    /// <param name="name">The name of the member, as the script wrote it.</param>
    /// <returns>
    ///     The hop for the member.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The type can't have the member or a chain can't continue past <paramref name="tail"/>.
    /// </exception>
    THop ResolveMember(THop tail, string name);
}
