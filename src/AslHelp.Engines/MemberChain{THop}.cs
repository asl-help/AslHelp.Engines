using AslHelp.Memory.Watching;
using System;
using System.Dynamic;

namespace AslHelp.Engines;

/// <summary>
///     Represents a path of members from a root object, built by writing each member's name after the last one.
/// </summary>
/// <remarks>
///     <para>
///         A chain is immutable. Getting a member returns a new chain with 1 more hop, so 2 chains can share a start.
///     </para>
///     <para>
///         The type declares no public properties or fields, because one would hide a game member of the same name. The
///         inherited members are methods, so getting a member with one of their names still goes to the resolver.
///     </para>
/// </remarks>
/// <typeparam name="THop">
///     The engine's type for 1 hop of a chain. Its text is how a script writes the hop, with its separator, like
///     <c>.Health</c>.
/// </typeparam>
public sealed class MemberChain<THop> : DynamicObject, IWatcherSource
    where THop : class
{
    private readonly THop[] _hops;
    private readonly IMemberResolver<THop> _resolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemberChain{THop}"/> class that starts at <paramref name="root"/>.
    /// </summary>
    /// <param name="resolver">The resolver that finds each member and creates the watcher.</param>
    /// <param name="root">The hop for the root object.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="resolver"/> or <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    public MemberChain(IMemberResolver<THop> resolver, THop root)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(root);

        _resolver = resolver;
        _hops = [root];
    }

    private MemberChain(IMemberResolver<THop> resolver, THop[] hops)
    {
        _resolver = resolver;
        _hops = hops;
    }

    /// <inheritdoc/>
    Watcher IWatcherSource.CreateWatcher(ITickCounter ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        return _resolver.CreateWatcher(ticks, _hops);
    }

    /// <summary>
    ///     Returns the hops in order, each as its own text.
    /// </summary>
    /// <returns>
    ///     The text of every hop, joined without a separator.
    /// </returns>
    public override string ToString()
    {
        return string.Concat<THop>(_hops);
    }

    /// <summary>
    ///     Creates a chain with the member that <paramref name="binder"/> asks for after the last hop of this chain.
    /// </summary>
    /// <param name="binder">The binder with the name of the member.</param>
    /// <param name="result">The longer chain.</param>
    /// <returns>
    ///     <see langword="true"/>, because an unknown member throws.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="binder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The resolver doesn't find the member.</exception>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        ArgumentNullException.ThrowIfNull(binder);

        result = new MemberChain<THop>(_resolver, [.. _hops, _resolver.ResolveMember(_hops[^1], binder.Name)]);

        return true;
    }
}
