using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Provides reads of object pointers in chunks behind a table inside the array, which is how Unreal Engine 4.8 to 4.10 store objects.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the engine adds objects and chunks while it runs.
///     Keeps <see cref="Address.Zero"/> for each position of a chunk pointer of 0, because the engine allocates a chunk late.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="array">The address of the array, which is <c>ObjObjects</c> in <c>FUObjectArray</c>.</param>
/// <param name="layout">The offsets inside the array.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/> or <paramref name="layout"/> is <see langword="null"/>.
/// </exception>
public sealed class IndirectObjectArray(
    IMemoryReader memory,
    Address array,
    IndirectObjectArrayLayout layout) : IObjectArray
{
    // Use the 512 chunk pointers of the table, because every 4.8 to 4.10 profile has its count that far behind the table on x86 and x64.
    private const int MaxChunks = 512;

    // Use the chunk size of TStaticIndirectArrayThreadSafeRead in the engine source, which every 4.8 to 4.10 build has.
    private const int PointersPerChunk = 16384;

    private readonly Address _array = array;
    private readonly IndirectObjectArrayLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    // Create the item of 1 pointer once, because a search reads the array on every tick.
    private readonly ObjectItemLayout _pointerItem = new(0, memory.Layout.PointerSize);

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

        if (CheckChunks(arrayCount).TryUnwrapErr(out error))
        {
            yield return error;
            yield break;
        }

        foreach (var slot in ObjectSlots.ReadChunks(_memory, _array, _array + _layout.Objects, total, PointersPerChunk, _pointerItem))
        {
            yield return slot;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Returns <see langword="null"/> as the serial number, because 4.8 to 4.10 keep serial numbers in blocks outside the array.
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

        // Check the count against the chunks like Read does, so an index from a damaged count doesn't read past the table.
        if (CheckChunks(count).TryUnwrapErr(out error))
        {
            return error;
        }

        int chunk = index / PointersPerChunk;
        var chunkAddress = _array + _layout.Objects + ((long)chunk * _memory.Layout.PointerSize);

        if (!_memory.ReadPointer(chunkAddress).TryUnwrap(out var start, out error))
        {
            return ObjectArrayError.ChunkPointerUnreadable(_array, chunk, chunkAddress, error);
        }

        return start == Address.Zero
            ? default(ObjectItem)
            : ObjectSlots.ReadItem(_memory, _array, index, start + ((long)(index % PointersPerChunk) * _memory.Layout.PointerSize), _pointerItem);
    }

    // Read the number of chunks and fail it when it's outside the table or its chunks have less room than the count of the array,
    // so a sample fails on a wrong offset the same way a full read does.
    private Result CheckChunks(int arrayCount)
    {
        var chunkCountAddress = _array + _layout.NumChunks;

        if (!_memory.Read<int>(chunkCountAddress).TryUnwrap(out int chunks, out var error))
        {
            return ObjectArrayError.ChunkCountUnreadable(_array, chunkCountAddress, error);
        }

        if (chunks is < 0 or > MaxChunks)
        {
            return ObjectArrayError.ChunkCountOutOfRange(_array, chunks, MaxChunks);
        }

        return arrayCount > chunks * PointersPerChunk
            ? ObjectArrayError.CountPastChunks(_array, arrayCount, chunks, PointersPerChunk)
            : Result.Ok();
    }
}
