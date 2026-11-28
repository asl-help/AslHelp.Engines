using System;

namespace AslHelp.Engines;

/// <summary>
///     Represents a path from a root object of a game through members and items to a value, like <c>World.Player.Items[2]</c>.
/// </summary>
/// <remarks>
///     <para>
///         A path is immutable. Each step returns a new path, so 2 paths can share a start.
///     </para>
///     <para>
///         A step returns a failure at once when the path doesn't fit the game as it is now, like a member the class doesn't have.
///         When the path doesn't read yet, like on a null object on the way, the step succeeds and each read of a watcher fails
///         until the path reads. A step can read the game, so build a path once and keep it.
///     </para>
///     <para>
///         <see cref="IWatcherSource{T}.CreateWatcher"/> watches the value at the end in the type the engine reads it as,
///         like an <see cref="int"/>, a <see cref="string"/> or a <see cref="StructValue"/>.
///     </para>
///     <para>
///         Override <see cref="object.ToString"/> to return the path as it's written, like <c>World.Player.Items[2]</c>.
///         Error messages show that text.
///     </para>
/// </remarks>
public interface IMemberPath : IWatcherSource<object?>
{
    /// <summary>
    ///     Creates the path to the number of items of the collection at the end of this path.
    /// </summary>
    /// <returns>
    ///     The longer path or a failure when the end of this path isn't a collection.
    /// </returns>
    Result<IMemberPath> Count();

    /// <summary>
    ///     Creates the path to the item of <paramref name="key"/> in the collection at the end of this path.
    /// </summary>
    /// <param name="key">The index or key of the item.</param>
    /// <returns>
    ///     The longer path like <c>Items[2]</c> or a failure when the end of this path isn't a collection.
    /// </returns>
    Result<IMemberPath> Item(PathKey key);

    /// <summary>
    ///     Creates the path to the member <paramref name="name"/> of the object or struct at the end of this path.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <returns>
    ///     The longer path or a failure when the end of this path doesn't have the member.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    Result<IMemberPath> Member(string name);

    /// <summary>
    ///     Creates the source of a typed watcher for the value at the end of this path.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>
    ///     The source or a failure when the engine doesn't read the value as <typeparamref name="T"/>.
    /// </returns>
    Result<IWatcherSource<T>> Value<T>();
}
