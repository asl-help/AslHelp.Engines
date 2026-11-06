using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of the name, the class and the outer object of an object.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the game can free an object and reuse its address.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="names">The table that the name ids of the objects point into.</param>
/// <param name="layout">The offsets inside an object.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/>, <paramref name="names"/> or <paramref name="layout"/> is <see langword="null"/>.
/// </exception>
public sealed class ObjectReader(
    IMemoryReader memory,
    FlatNameTable names,
    ObjectLayout layout)
{
    private readonly ObjectLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly FlatNameTable _names = names ?? throw new ArgumentNullException(nameof(names));

    /// <summary>
    ///     Reads the base name of <paramref name="obj"/>, without the number suffix.
    /// </summary>
    /// <remarks>
    ///     Gives the same text for 2 objects whose names differ only in the number.
    /// </remarks>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The base name or <see langword="null"/> when <paramref name="obj"/> is <see cref="Address.Zero"/>,
    ///     a read fails or the name number is negative.
    /// </returns>
    public string? NameOf(Address obj)
    {
        if (obj == Address.Zero
            || !_memory.Read<int>(obj + _layout.Name).TryUnwrap(out int id)
            || !_memory.Read<int>(obj + _layout.Name + sizeof(int)).TryUnwrap(out int number)
            || number < 0)
        {
            return null;
        }

        return _names.Read(id);
    }

    /// <summary>
    ///     Reads the class of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The address of the class or <see cref="Address.Zero"/> when the read fails.
    /// </returns>
    public Address ClassOf(Address obj)
    {
        return _memory.ReadPointer(obj + _layout.Class).UnwrapOr(Address.Zero);
    }

    /// <summary>
    ///     Reads the outer object of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The address of the outer object or <see cref="Address.Zero"/> when the read fails.
    /// </returns>
    public Address OuterOf(Address obj)
    {
        return _memory.ReadPointer(obj + _layout.Outer).UnwrapOr(Address.Zero);
    }
}
