using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Objects;

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

        if (CheckChunks(arrayCount).TryUnwrapErr(out error))
        {
            yield return error;
            yield break;
        }

        foreach (var slot in ObjectSlots.ReadChunks(_memory, _array, table, total, _layout.ElementsPerChunk, _item))
        {
            yield return slot;
        }
    }

    /// <inheritdoc/>
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

        // Check the count against the chunks like Read does, so an index from a damaged count doesn't read past the chunk pointers.
        if (CheckChunks(count).TryUnwrapErr(out error))
        {
            return error;
        }

        var tableAddress = _array + _layout.Objects;

        if (!_memory.ReadPointer(tableAddress).TryUnwrap(out var table, out error))
        {
            return ObjectArrayError.DataUnreadable(_array, tableAddress, error);
        }

        int chunk = index / _layout.ElementsPerChunk;
        var chunkAddress = table + ((long)chunk * _memory.Layout.PointerSize);

        if (!_memory.ReadPointer(chunkAddress).TryUnwrap(out var start, out error))
        {
            return ObjectArrayError.ChunkPointerUnreadable(_array, chunk, chunkAddress, error);
        }

        // Read a position in a chunk that the engine didn't allocate yet as empty, like Read does.
        return start == Address.Zero
            ? default(ObjectItem)
            : ObjectSlots.ReadItem(_memory, _array, index, start + ((long)(index % _layout.ElementsPerChunk) * _item.Size), _item);
    }

    // Read the number of chunks and fail it when it's below 0 or its chunks have less room than the count of the array,
    // so a sample fails on a wrong offset the same way a full read does.
    private Result CheckChunks(int arrayCount)
    {
        var chunkCountAddress = _array + _layout.NumChunks;

        if (!_memory.Read<int>(chunkCountAddress).TryUnwrap(out int chunks, out var error))
        {
            return ObjectArrayError.ChunkCountUnreadable(_array, chunkCountAddress, error);
        }

        if (chunks < 0)
        {
            return ObjectArrayError.ChunkCountNegative(_array, chunks);
        }

        return arrayCount > (long)chunks * _layout.ElementsPerChunk
            ? ObjectArrayError.CountPastChunks(_array, arrayCount, chunks, _layout.ElementsPerChunk)
            : Result.Ok();
    }
}
