using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of the super chain and the fields of a struct.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the game can free a struct and reuse its address.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="objects">The reader of the names and the outer objects.</param>
/// <param name="layout">The offsets inside a struct.</param>
/// <param name="fieldLayout">The offsets inside a field.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/>, <paramref name="objects"/>, <paramref name="layout"/> or <paramref name="fieldLayout"/> is <see langword="null"/>.
/// </exception>
public sealed class StructReader(
    IMemoryReader memory,
    ObjectReader objects,
    StructLayout layout,
    FieldLayout fieldLayout)
{
    private const int MaxChildren = 4096;
    private const int MaxDepth = 64;

    private readonly FieldLayout _fieldLayout = fieldLayout ?? throw new ArgumentNullException(nameof(fieldLayout));
    private readonly StructLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ObjectReader _objects = objects ?? throw new ArgumentNullException(nameof(objects));

    /// <summary>
    ///     Reads the struct that <paramref name="type"/> derives from.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The address of the super struct or <see cref="Address.Zero"/> when the read fails.
    /// </returns>
    public Address SuperOf(Address type)
    {
        return _memory.ReadPointer(type + _layout.SuperStruct).UnwrapOr(Address.Zero);
    }

    /// <summary>
    ///     Determines whether <paramref name="type"/> or a struct up its super chain is called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally. Stops at <see cref="Address.Zero"/> and after 64 levels, because a damaged chain can loop.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <param name="name">The base name to find.</param>
    /// <returns>
    ///     <see langword="true"/> when a struct on the chain is called <paramref name="name"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public bool DerivesFrom(Address type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var current = type;

        for (int depth = 0; depth < MaxDepth && current != Address.Zero; depth++)
        {
            if (string.Equals(_objects.NameOf(current), name, StringComparison.Ordinal))
            {
                return true;
            }

            current = SuperOf(current);
        }

        return false;
    }

    /// <summary>
    ///     Enumerates the fields of <paramref name="type"/> in chain order.
    /// </summary>
    /// <remarks>
    ///     Applies through Unreal Engine 4.24, where a field is an object with an outer object.
    ///     Reads each field when the enumeration moves to it, so a caller can stop early.
    ///     Stops at <see cref="Address.Zero"/>, at a field whose outer object is not <paramref name="type"/> and after 4096 fields,
    ///     because a damaged chain can loop.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The addresses of the fields.
    /// </returns>
    public IEnumerable<Address> ChildrenOf(Address type)
    {
        var field = _memory.ReadPointer(type + _layout.Children).UnwrapOr(Address.Zero);

        for (int count = 0; count < MaxChildren && field != Address.Zero && _objects.OuterOf(field) == type; count++)
        {
            yield return field;

            field = _memory.ReadPointer(field + _fieldLayout.Next).UnwrapOr(Address.Zero);
        }
    }
}
