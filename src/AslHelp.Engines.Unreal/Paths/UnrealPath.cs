using AslHelp.Engines.Unreal.Properties;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal.Paths;

/// <summary>
///     Represents a path from a root object of a game, like a global, through members to an object or a struct in an object, like
///     <c>GWorld.AuthorityGameMode</c>.
/// </summary>
/// <remarks>
///     A path is immutable and doesn't read until a watcher of a longer path reads.
///     Each hop looks the member up on the real class of the object it's read from,
///     so a member of a derived class works after a member declared as its base class.
///     A hop through an object property reads the pointer and a hop through a struct property only adds the offset,
///     so both are written the same way.
///     Isn't thread-safe, because a read keeps the property of each hop.
/// </remarks>
public sealed class UnrealPath
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
    ///     Gets the path to the number of elements of the array, set or map at the end of this path.
    /// </summary>
    public UnrealValuePath<int> Count => new(_memberPath.Append(CollectionAccess.CountMember));

    /// <summary>
    ///     Creates the path to the element of <paramref name="key"/> in the array or map at the end of this path.
    /// </summary>
    /// <remarks>
    ///     An integer is the index of an array element or a key of a map, where it matches any integer or enum key it fits in.
    ///     A key that the collection can't take fails when a watcher reads, because a read finds the class of the object.
    /// </remarks>
    /// <param name="key">The index or key.</param>
    /// <returns>
    ///     The longer path, like <c>Items[1]</c>, which goes on into the element.
    /// </returns>
    public UnrealPath this[int key] => new(Append(key));

    /// <inheritdoc cref="this[int]"/>
    public UnrealPath this[long key] => new(Append(key));

    /// <inheritdoc cref="this[int]"/>
    /// <remarks>
    ///     A <see cref="string"/> matches a name or string key of a set or a map.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="key"/> is <see langword="null"/>.
    /// </exception>
    public UnrealPath this[string key] => new(Append(key ?? throw new ArgumentNullException(nameof(key))));

    /// <summary>
    ///     Creates the path to the value of type <typeparamref name="T"/> of the element of <paramref name="key"/> in the collection
    ///     at the end of this path.
    /// </summary>
    /// <remarks>
    ///     On a set, a <see cref="bool"/> read gives whether the set has the key. The other rules are those of the indexer.
    /// </remarks>
    /// <typeparam name="T">The type of the element, like <see cref="int"/> for an <c>IntProperty</c> element.</typeparam>
    /// <param name="key">The index or key.</param>
    /// <returns>
    ///     The path to the value.
    /// </returns>
    public UnrealValuePath<T> Element<T>(int key)
    {
        return new UnrealValuePath<T>(Append(key));
    }

    /// <inheritdoc cref="Element{T}(int)"/>
    public UnrealValuePath<T> Element<T>(long key)
    {
        return new UnrealValuePath<T>(Append(key));
    }

    /// <inheritdoc cref="Element{T}(int)"/>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="key"/> is <see langword="null"/>.
    /// </exception>
    public UnrealValuePath<T> Element<T>(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new UnrealValuePath<T>(Append(key));
    }

    /// <inheritdoc cref="Element{T}(int)"/>
    public UnrealValuePath<T> Element<T>(bool key)
    {
        return new UnrealValuePath<T>(Append(key));
    }

    /// <summary>
    ///     Creates the path to the object or struct in the member <paramref name="name"/> of the end of this path.
    /// </summary>
    /// <param name="name">The name of an object or struct property, like <c>AuthorityGameMode</c>.</param>
    /// <returns>
    ///     The longer path.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public UnrealPath Member(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new UnrealPath(_memberPath.Append(name));
    }

    /// <summary>
    ///     Creates the path to the value of type <typeparamref name="T"/> in the member <paramref name="name"/> of the end of this path.
    /// </summary>
    /// <remarks>
    ///     A <typeparamref name="T"/> that the kind of the member doesn't read fails when a watcher reads,
    ///     because a read finds the class of the object.
    ///     After an array, a set or a map, <see cref="Count"/> reads its number of elements.
    /// </remarks>
    /// <typeparam name="T">
    ///     The type of the value, like <see cref="int"/> for an <c>IntProperty</c>. <see cref="UnrealGame.Read{T}"/> lists the types.
    /// </typeparam>
    /// <param name="name">The name of the property, like <c>Hp</c>.</param>
    /// <returns>
    ///     The path to the value.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public UnrealValuePath<T> Member<T>(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new UnrealValuePath<T>(_memberPath.Append(name));
    }

    /// <summary>
    ///     Returns the root and the members of the path joined by a period.
    /// </summary>
    /// <returns>
    ///     The text of the path, like <c>GWorld.AuthorityGameMode</c>.
    /// </returns>
    public override string ToString()
    {
        return _memberPath.ToString();
    }

    /// <summary>
    ///     Reads the address of the object or struct at the end of the path.
    /// </summary>
    /// <returns>
    ///     The address or a failure. An object pointer of 0 at the end returns <see cref="Address.Zero"/>.
    /// </returns>
    internal Result<Address> Read()
    {
        return _memberPath.ReadContainer();
    }

    private MemberPath Append(object key)
    {
        return _memberPath.Append(MemberHop.Index(key));
    }
}
