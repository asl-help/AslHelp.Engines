using AslHelp.Engines.Unreal.Errors;
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
    ///     The address of the super struct or a failure with the struct and the failed read. A pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    public Result<Address> SuperOf(Address type)
    {
        var address = type + _layout.SuperStruct;

        return _memory.ReadPointer(address).TryUnwrap(out var super, out var error)
            ? super
            : StructError.SuperUnreadable(type, address, _layout.SuperStruct, error);
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
    ///     <see langword="true"/> when a struct on the chain is called <paramref name="name"/>, <see langword="false"/> when none is,
    ///     or a failure with the level of the chain and the failed read when a name or a super pointer on the way does not read.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<bool> DerivesFrom(Address type, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var current = type;

        for (int depth = 0; depth < MaxDepth && current != Address.Zero; depth++)
        {
            if (!_objects.NameOf(current).TryUnwrap(out string? currentName, out var error))
            {
                return StructError.ChainUnreadable(type, depth + 1, error);
            }

            if (string.Equals(currentName, name, StringComparison.Ordinal))
            {
                return true;
            }

            if (!SuperOf(current).TryUnwrap(out current, out error))
            {
                return StructError.ChainUnreadable(type, depth + 1, error);
            }
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
    ///     Ends at the 1st read that fails, with the failure as the last item, so the failure comes where the fields stop.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The addresses of the fields, then a failure if a read failed.
    /// </returns>
    public IEnumerable<Result<Address>> ChildrenOf(Address type)
    {
        var childrenAddress = type + _layout.Children;

        if (!_memory.ReadPointer(childrenAddress).TryUnwrap(out var field, out var error))
        {
            yield return StructError.ChildrenUnreadable(type, childrenAddress, _layout.Children, error);
            yield break;
        }

        for (int count = 0; count < MaxChildren && field != Address.Zero; count++)
        {
            if (!_objects.OuterOf(field).TryUnwrap(out var outer, out error))
            {
                yield return StructError.FieldUnreadable(type, count + 1, error);
                yield break;
            }

            if (outer != type)
            {
                yield break;
            }

            yield return field;

            var nextAddress = field + _fieldLayout.Next;

            if (!_memory.ReadPointer(nextAddress).TryUnwrap(out var next, out error))
            {
                yield return StructError.NextUnreadable(field, nextAddress, _fieldLayout.Next, error);
                yield break;
            }

            field = next;
        }
    }
}
