using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Structs;

/// <summary>
///     Provides reads of the super chain and the fields of a struct.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the game can free a struct and reuse its address.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="objectReader">The reader of the names and the outer objects.</param>
/// <param name="structLayout">The offsets inside a struct.</param>
/// <param name="childLayout">The offsets inside a field on the children chain.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/>, <paramref name="objectReader"/>, <paramref name="structLayout"/> or <paramref name="childLayout"/> is <see langword="null"/>.
/// </exception>
public sealed class StructReader(
    IMemoryReader memory,
    ObjectReader objectReader,
    StructLayout structLayout,
    ChildLayout childLayout)
{
    private const int MaxChildren = 4096;
    private const int MaxDepth = 64;

    private readonly ChildLayout _childLayout = childLayout ?? throw new ArgumentNullException(nameof(childLayout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ObjectReader _objectReader = objectReader ?? throw new ArgumentNullException(nameof(objectReader));
    private readonly StructLayout _structLayout = structLayout ?? throw new ArgumentNullException(nameof(structLayout));

    /// <summary>
    ///     Gets the offset of the super pointer in a struct.
    /// </summary>
    internal int SuperOffset => _structLayout.SuperStruct;

    /// <summary>
    ///     Reads the struct that <paramref name="type"/> derives from.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The address of the super struct or a failure with the struct and the failed read. A pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    public Result<Address> SuperOf(Address type)
    {
        var address = type + _structLayout.SuperStruct;

        return _memory.ReadPointer(address).TryUnwrap(out var super, out var error)
            ? super
            : StructError.SuperUnreadable(type, address, _structLayout.SuperStruct, error);
    }

    /// <summary>
    ///     Determines whether <paramref name="type"/> or a struct up its super chain is called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally. Stops at <see cref="Address.Zero"/> and fails after 64 levels, because a damaged chain can loop.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <param name="name">The name to find, as <see cref="ObjectReader.NameOf"/> returns it.</param>
    /// <returns>
    ///     <see langword="true"/> when a struct on the chain is called <paramref name="name"/>, <see langword="false"/> when none is,
    ///     or a failure with the level of the chain and the failed read when a name or a super pointer on the way does not read.
    ///     A chain that goes on past 64 levels fails too.
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
            if (!_objectReader.NameOf(current).TryUnwrap(out string? currentName, out var error))
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

        if (current != Address.Zero)
        {
            return StructError.ChainPastLimit(type, MaxDepth, _structLayout.SuperStruct);
        }

        return false;
    }

    /// <summary>
    ///     Enumerates the fields of <paramref name="type"/> in chain order.
    /// </summary>
    /// <remarks>
    ///     Applies to every field through Unreal Engine 4.24 and to the functions from 4.25, where a field is an object with an outer object.
    ///     Reads each field when the enumeration moves to it, so a caller can stop early.
    ///     Stops at <see cref="Address.Zero"/> and at a later field whose outer object is not <paramref name="type"/>.
    ///     Fails at a 1st field whose outer object is not <paramref name="type"/>, because the struct points at its own 1st field.
    ///     Fails after 4096 fields, because a damaged chain can loop.
    ///     Ends at the 1st read that fails, with the failure as the last item, so the failure comes where the fields stop.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The addresses of the fields, then a failure if a read failed, the 1st field has another outer object
    ///     or the chain goes on past 4096 fields.
    /// </returns>
    public IEnumerable<Result<Address>> ChildrenOf(Address type)
    {
        var childrenAddress = type + _structLayout.Children;

        if (!_memory.ReadPointer(childrenAddress).TryUnwrap(out var field, out var error))
        {
            yield return StructError.ChildrenUnreadable(type, childrenAddress, _structLayout.Children, error);
            yield break;
        }

        for (int count = 0; count < MaxChildren && field != Address.Zero; count++)
        {
            if (!_objectReader.OuterOf(field).TryUnwrap(out var outer, out error))
            {
                yield return StructError.FieldUnreadable(type, count + 1, error);
                yield break;
            }

            if (outer != type)
            {
                // Fail a 1st field with another outer object, because the struct points at its own 1st field and a wrong offset would end as an empty success.
                if (count == 0)
                {
                    yield return StructError.FirstFieldOuterMismatch(type, field, outer, _objectReader.OuterOffset);
                }

                yield break;
            }

            yield return field;

            var nextAddress = field + _childLayout.Next;

            if (!_memory.ReadPointer(nextAddress).TryUnwrap(out var next, out error))
            {
                yield return StructError.NextUnreadable(field, nextAddress, _childLayout.Next, error);
                yield break;
            }

            field = next;
        }

        if (field != Address.Zero)
        {
            yield return StructError.FieldsPastLimit(type, MaxChildren, _childLayout.Next);
        }
    }
}
