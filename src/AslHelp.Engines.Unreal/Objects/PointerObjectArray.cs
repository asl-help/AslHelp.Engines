using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Provides reads of a flat array of object pointers, which is how Unreal Engine 3 and Unreal Engine 4 through 4.7 store objects.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the engine moves the data when the array grows.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="array">
///     The address of the array, which is a data pointer and then a 4-byte count.
///     On Unreal Engine 4 that's <c>ObjObjects</c> in <c>FUObjectArray</c>.
/// </param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/> is <see langword="null"/>.
/// </exception>
public sealed class PointerObjectArray(
    IMemoryReader memory,
    Address array) : IObjectArray
{
    private readonly Address _array = array;
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    // Create the item of 1 pointer once, because a search reads the array on every tick.
    private readonly ObjectItemLayout _pointerItem = new(0, memory.Layout.PointerSize);

    /// <inheritdoc/>
    public Result<int> ReadCount()
    {
        return ObjectSlots.ReadCount(_memory, _array, _array + _memory.Layout.PointerSize);
    }

    /// <inheritdoc/>
    public IEnumerable<Result<Address>> Read(int count)
    {
        if (!ReadCount().TryUnwrap(out int arrayCount, out var error))
        {
            yield return error;
            yield break;
        }

        int total = Math.Min(count, arrayCount);

        if (total <= 0)
        {
            yield break;
        }

        if (!_memory.ReadPointer(_array).TryUnwrap(out var data, out error))
        {
            yield return ObjectArrayError.DataUnreadable(_array, _array, error);
            yield break;
        }

        foreach (var slot in ObjectSlots.Read(_memory, _array, data, total, _pointerItem))
        {
            yield return slot;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Returns <see langword="null"/> as the serial number, because the array only keeps object pointers.
    /// </remarks>
    public Result<ObjectItem> ReadItem(int index)
    {
        if (!ReadCount().TryUnwrap(out int count, out var error))
        {
            return error;
        }

        if (index < 0 || index >= count)
        {
            return default(ObjectItem);
        }

        return _memory.ReadPointer(_array).TryUnwrap(out var data, out error)
            ? ObjectSlots.ReadItem(_memory, _array, index, data + ((long)index * _memory.Layout.PointerSize), _pointerItem)
            : ObjectArrayError.DataUnreadable(_array, _array, error);
    }
}
