using AslHelp.Engines.Errors;
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
///         When the path doesn't read yet, like on a null object on the way, the engine checks what it can without the object.
///         A step it can't check succeeds with <see cref="IsChecked"/> <see langword="false"/>.
///         The 1st read that reaches such a step fails with a <see cref="MemberPathError"/> whose
///         <see cref="MemberPathError.IsShapeMismatch"/> is <see langword="true"/> when the step doesn't fit.
///         A step can read the game, so build a path once and keep it.
///     </para>
///     <para>
///         <see cref="IWatcherSource{T}.CreateWatcher"/> watches the value at the end in the type the engine reads it as,
///         like an <see cref="int"/>, a <see cref="string"/> or a <see cref="StructValue"/>. <see cref="Read"/> reads it once.
///     </para>
///     <para>
///         Override <see cref="object.ToString"/> to return the path as it's written, like <c>World.Player.Items[2]</c>.
///         Error messages show that text.
///     </para>
/// </remarks>
public interface IMemberPath : IWatcherSource<object?>
{
    /// <summary>
    ///     Gets the path to the number of items of the collection at the end of this path or a failure when the end of this
    ///     path isn't a collection.
    /// </summary>
    Result<IMemberPath> Count { get; }

    /// <summary>
    ///     Gets a value indicating whether the engine checked the last step of this path against the game.
    /// </summary>
    /// <remarks>
    ///     <see langword="false"/> after a step on a path that doesn't read yet, until a read of the path reaches the step.
    ///     A path without steps is always checked.
    /// </remarks>
    bool IsChecked { get; }

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
    ///     Reads the value at the end of this path once, in the type the engine reads it as.
    /// </summary>
    /// <remarks>
    ///     Reads every step again and doesn't use or change what the watchers of a tick keep, so a write in the current tick shows up.
    /// </remarks>
    /// <returns>
    ///     The value or a failure. It's a <see cref="MemberPathError"/> for a path that doesn't read now or doesn't fit the game.
    /// </returns>
    Result<object?> Read();

    /// <summary>
    ///     Reads the value of type <typeparamref name="T"/> at the end of this path once.
    /// </summary>
    /// <remarks>
    ///     Reads like <see cref="Read()"/>.
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>
    ///     The value or a failure, also when the engine doesn't read the value as <typeparamref name="T"/>.
    /// </returns>
    Result<T> Read<T>();

    /// <summary>
    ///     Creates the source of a typed watcher for the value at the end of this path.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>
    ///     The source or a failure when the engine doesn't read the value as <typeparamref name="T"/>.
    ///     When <see cref="IsChecked"/> is <see langword="false"/>, <typeparamref name="T"/> isn't checked either,
    ///     so probe it with <see cref="Read{T}"/> once the path reads.
    /// </returns>
    Result<IWatcherSource<T>> Value<T>();
}
