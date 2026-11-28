using AslHelp.Engines.Errors;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using System;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents a path from a root object of a game, like a global, through members and items, like
///     <c>GWorld.AuthorityGameMode.FixtureData.Items[1]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Each step reads the path once with the classes the game has now. A member that the class doesn't have, a member after a value
///         or after a collection, an index or <c>Count</c> after a member that isn't a collection and a key of the wrong type fail on the step.
///         When the path doesn't read yet, like on a null object or a root that isn't there, the step checks the same rules against the class
///         that each property declares, starting at the 1st segment that doesn't read. The step fails when neither the declared class
///         nor a loaded class derived from it has the member. It can pass once the game loads a derived class with the member.
///         A step that the declared classes can't confirm succeeds with <see cref="IsChecked"/> <see langword="false"/>
///         and the 1st read that reaches it fails with a shape mismatch when it doesn't fit.
///         Names of members ignore case like the engine, but the exact name wins.
///     </para>
///     <para>
///         Each read looks the member up on the real class of the object it's read from again,
///         so a member of a derived class works after a member declared as its base class and a class that changes later still reads.
///         A hop through an object property reads the pointer and a hop through a struct property only adds the offset,
///         so both are written the same way.
///     </para>
///     <para>
///         The watchers of 1 tick counter read the steps their paths share and the class of the object each step reaches once per tick,
///         also when each path is built again from the same root. A watcher that reads again in the same tick,
///         like after <see cref="Watcher.Enabled"/> turns back on, gets the shared steps of that tick and only reads its last step again.
///         A read without a watcher, like a step, reads the whole path.
///     </para>
///     <para>
///         The root keeps every path a step returned, so build a path once and not on each tick.
///     </para>
///     <para>
///         Isn't thread-safe, because a read keeps the property of each hop.
///     </para>
/// </remarks>
public sealed class UnrealPath : IMemberPath
{
    private readonly MemberPath _memberPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealPath"/> class.
    /// </summary>
    /// <param name="memberPath">The path to read.</param>
    internal UnrealPath(MemberPath memberPath)
    {
        _memberPath = memberPath;
    }

    /// <summary>
    ///     Gets a value indicating whether a step or a read checked the last step of the path against the classes of the game.
    /// </summary>
    /// <remarks>
    ///     A step on a live object checks the real class of the object. When an object on the way is null,
    ///     the step checks the class that each property declares instead, starting at the class of the root.
    ///     It stays unchecked when only a loaded derived class has a member or a declared class doesn't read,
    ///     until a read of the path reaches the step.
    /// </remarks>
    public bool IsChecked => _memberPath.IsChecked;

    /// <summary>
    ///     Creates the path to the number of elements of the array, set or map at the end of this path.
    /// </summary>
    /// <returns>
    ///     The longer path to an <see cref="int"/> or a failure when the end of this path isn't a collection, even when it has a member <c>Count</c>.
    /// </returns>
    public Result<IMemberPath> Count()
    {
        return Step(_memberPath.AppendCount());
    }

    /// <summary>
    ///     Creates a new watcher for the value at the end of the path in the type of the kind of its property.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Watcher{T}.Current"/> is the value in the type of the kind that a read finds,
    ///         like an <see cref="int"/> for an <c>IntProperty</c> or a <see cref="StructValue"/> for a <c>StructProperty</c>.
    ///         The path of a root alone reads as the <see cref="Address"/> of its object.
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
    /// <returns>
    ///     The watcher.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> is <see langword="null"/>.
    /// </exception>
    public Watcher<object?> CreateWatcher(ITickCounter ticks)
    {
        return new BoxedPathWatcher(ticks, _memberPath);
    }

    /// <inheritdoc/>
    Watcher IWatcherSource.CreateWatcher(ITickCounter ticks)
    {
        return CreateWatcher(ticks);
    }

    /// <summary>
    ///     Creates the path to the element of <paramref name="key"/> in the array, set or map at the end of this path.
    /// </summary>
    /// <remarks>
    ///     An integer is the index of an array element or a key of a map, where it matches any integer or enum key it fits in.
    ///     A string matches a name or string key. On a set, a <see cref="bool"/> read gives whether the set has the key.
    ///     A missing key or an index past the count fails the read.
    /// </remarks>
    /// <param name="key">The index or key.</param>
    /// <returns>
    ///     The longer path like <c>Items[1]</c> or a failure when the end of this path isn't a collection or the key can't match its keys.
    /// </returns>
    public Result<IMemberPath> Item(PathKey key)
    {
        return Step(_memberPath.AppendItem(key));
    }

    /// <summary>
    ///     Creates the path to the member <paramref name="name"/> of the object or struct at the end of this path.
    /// </summary>
    /// <param name="name">The name of the property like <c>AuthorityGameMode</c> or of a struct member like <c>X</c>.</param>
    /// <returns>
    ///     The longer path or a failure when the class or struct doesn't have the member or the end of this path doesn't have members.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<IMemberPath> Member(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Step(_memberPath.Append(name));
    }

    /// <summary>
    ///     Returns the root and the steps of the path.
    /// </summary>
    /// <returns>
    ///     The text of the path, like <c>GWorld.AuthorityGameMode.Items[1]</c>.
    /// </returns>
    public override string ToString()
    {
        return _memberPath.ToString();
    }

    /// <summary>
    ///     Creates the source of a watcher for the value of type <typeparamref name="T"/> at the end of this path.
    /// </summary>
    /// <typeparam name="T">
    ///     The type of the value, like <see cref="int"/> for an <c>IntProperty</c>. <see cref="UnrealGame.Read{T}"/> lists the types.
    ///     The path of a root alone reads as an <see cref="Address"/>.
    /// </typeparam>
    /// <returns>
    ///     The source or a failure when the kind at the end doesn't read as <typeparamref name="T"/>.
    ///     When <see cref="IsChecked"/> is <see langword="false"/>, <typeparamref name="T"/> isn't checked either
    ///     and each read of the watcher fails with a shape mismatch when it doesn't fit.
    /// </returns>
    public Result<IWatcherSource<T>> Value<T>()
    {
        return _memberPath.CheckValue<T>().TryUnwrapErr(out var error)
            ? error
            : Result.Ok<IWatcherSource<T>>(new UnrealValuePath<T>(_memberPath));
    }

    /// <summary>
    ///     Reads the value at the end of the path once, in the type of the kind of its property.
    /// </summary>
    /// <remarks>
    ///     Reads the whole path and doesn't use or change the reads that the watchers of a tick share,
    ///     so a write in the current tick shows up. Returns the same types as <see cref="CreateWatcher"/>.
    /// </remarks>
    /// <returns>
    ///     The value or a <see cref="MemberPathError"/>.
    /// </returns>
    public Result<object?> Read()
    {
        return _memberPath.ReadBoxed(null, null);
    }

    /// <summary>
    ///     Reads the value of type <typeparamref name="T"/> at the end of the path once.
    /// </summary>
    /// <remarks>
    ///     Reads like <see cref="Read()"/>.
    /// </remarks>
    /// <typeparam name="T">
    ///     The type of the value, like <see cref="int"/> for an <c>IntProperty</c>. <see cref="UnrealGame.Read{T}"/> lists the types.
    ///     The path of a root alone reads as an <see cref="Address"/>.
    /// </typeparam>
    /// <returns>
    ///     The value or a <see cref="MemberPathError"/>. It's a shape mismatch when the kind at the end doesn't read as <typeparamref name="T"/>.
    /// </returns>
    public Result<T> Read<T>()
    {
        return _memberPath.ReadValue<T>(null, default);
    }

    private static Result<IMemberPath> Step(Result<MemberPath> memberPath)
    {
        return memberPath.TryUnwrap(out var child, out var error) ? Result.Ok<IMemberPath>(new UnrealPath(child)) : error;
    }
}

file sealed class BoxedPathWatcher : Watcher<object?>
{
    private readonly MemberPath _memberPath;

    // Keep the last value apart from Current, because Clear empties Current and a read returns the last box when the value is equal.
    private object? _last;

    public BoxedPathWatcher(ITickCounter ticks, MemberPath memberPath)
        : base(ticks)
    {
        _memberPath = memberPath;
    }

    protected override Result<object?> Read()
    {
        var result = _memberPath.ReadBoxed(Ticks, _last);

        if (result.IsOk)
        {
            _last = result.Value;
        }

        return result;
    }

    public override string ToString()
    {
        return _memberPath.ToString();
    }
}
