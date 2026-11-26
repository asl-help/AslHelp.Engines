using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a path from a global of a game through object members to an object, like <c>GWorld.AuthorityGameMode</c>.
/// </summary>
/// <remarks>
///     A path is immutable and doesn't read until a watcher of a longer path reads.
///     Each hop looks the member up on the real class of the object it's read from,
///     so a member of a derived class works after a member declared as its base class.
/// </remarks>
public sealed class UnrealObjectPath
{
    private readonly MemberPath _memberPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealObjectPath"/> class.
    /// </summary>
    /// <param name="memberPath">The path to read.</param>
    internal UnrealObjectPath(MemberPath memberPath)
    {
        _memberPath = memberPath;
    }

    /// <summary>
    ///     Creates the path to the object that the member <paramref name="name"/> of the object at the end of this path points at.
    /// </summary>
    /// <param name="name">The name of an object property, like <c>AuthorityGameMode</c>.</param>
    /// <returns>
    ///     The longer path.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public UnrealObjectPath ObjectMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new UnrealObjectPath(_memberPath.Append(name));
    }

    /// <summary>
    ///     Creates the path to the 4-byte signed integer in the member <paramref name="name"/> of the object at the end of this path.
    /// </summary>
    /// <param name="name">The name of an <c>IntProperty</c>, like <c>Hp</c>.</param>
    /// <returns>
    ///     The path to the value.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public UnrealInt32Path Int32Member(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new UnrealInt32Path(_memberPath.Append(name));
    }

    /// <summary>
    ///     Returns the global and the members of the path joined by a period.
    /// </summary>
    /// <returns>
    ///     The text of the path, like <c>GWorld.AuthorityGameMode</c>.
    /// </returns>
    public override string ToString()
    {
        return _memberPath.ToString();
    }

    /// <summary>
    ///     Reads the object at the end of the path.
    /// </summary>
    /// <returns>
    ///     The address of the object or a failure. A pointer of 0 at the end returns <see cref="Address.Zero"/>.
    /// </returns>
    internal Result<Address> Read()
    {
        return _memberPath.ReadObject();
    }
}
