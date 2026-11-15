using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of object items from 1 array, which is how Unreal Engine 4.11 to 4.19 store objects.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the engine adds objects while it runs.
/// </remarks>
public sealed class ItemObjectArray : IObjectArray
{
    private readonly Address _array;
    private readonly ObjectItemLayout _item;
    private readonly ItemObjectArrayLayout _layout;
    private readonly IMemoryReader _memory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ItemObjectArray"/> class.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, which is <c>ObjObjects</c> in <c>FUObjectArray</c>.</param>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/>, <paramref name="layout"/> or <paramref name="item"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     A pointer at the object offset of <paramref name="item"/> doesn't fit in the item.
    ///     The item is larger than 1024 bytes.
    /// </exception>
    public ItemObjectArray(IMemoryReader memory, Address array, ItemObjectArrayLayout layout, ObjectItemLayout item)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _item = ObjectSlots.CheckItem(item, memory.Layout.PointerSize);
        _array = array;
    }

    /// <inheritdoc/>
    public Result<int> ReadCount()
    {
        return ObjectSlots.ReadCount(_memory, _array, _array + _layout.NumElements);
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

        var dataAddress = _array + _layout.Objects;

        if (!_memory.ReadPointer(dataAddress).TryUnwrap(out var data, out error))
        {
            yield return ObjectArrayError.DataUnreadable(_array, dataAddress, error);
            yield break;
        }

        foreach (var slot in ObjectSlots.Read(_memory, _array, data, total, _item))
        {
            yield return slot;
        }
    }
}
