using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of chunks of object items behind a pointer to the chunk pointers, which is how Unreal Engine 4.20 and later store objects.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the engine adds objects and chunks while it runs.
///     Keeps <see cref="Address.Zero"/> for each position of a chunk pointer of 0, because the engine allocates a chunk late.
/// </remarks>
public sealed class ChunkedObjectArray : IObjectArray
{
    private readonly Address _array;
    private readonly ObjectItemLayout _item;
    private readonly ChunkedObjectArrayLayout _layout;
    private readonly IMemoryReader _memory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChunkedObjectArray"/> class.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, which is <c>ObjObjects</c> in <c>FUObjectArray</c>.</param>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/>, <paramref name="layout"/> or <paramref name="item"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="layout"/> has less than 1 item per chunk.
    ///     A pointer at the object offset of <paramref name="item"/> doesn't fit in the item.
    ///     The item is larger than 1024 bytes.
    /// </exception>
    public ChunkedObjectArray(IMemoryReader memory, Address array, ChunkedObjectArrayLayout layout, ObjectItemLayout item)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));

        if (ObjectSlots.ValidateChunks(layout).TryUnwrapErr(out var error))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, error.Message);
        }

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

        var tableAddress = _array + _layout.Objects;

        if (!_memory.ReadPointer(tableAddress).TryUnwrap(out var table, out error))
        {
            yield return ObjectArrayError.DataUnreadable(_array, tableAddress, error);
            yield break;
        }

        var chunkCountAddress = _array + _layout.NumChunks;

        if (!_memory.Read<int>(chunkCountAddress).TryUnwrap(out int chunks, out error))
        {
            yield return ObjectArrayError.ChunkCountUnreadable(_array, chunkCountAddress, error);
            yield break;
        }

        if (chunks < 0)
        {
            yield return ObjectArrayError.ChunkCountNegative(_array, chunks);
            yield break;
        }

        // Check the count of the array, so a sample fails on a wrong offset the same way a full read does.
        if (arrayCount > (long)chunks * _layout.ElementsPerChunk)
        {
            yield return ObjectArrayError.CountPastChunks(_array, arrayCount, chunks, _layout.ElementsPerChunk);
            yield break;
        }

        foreach (var slot in ObjectSlots.ReadChunks(_memory, _array, table, total, _layout.ElementsPerChunk, _item))
        {
            yield return slot;
        }
    }
}
