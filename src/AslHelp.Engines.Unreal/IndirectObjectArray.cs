using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

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

        var chunkCountAddress = _array + _layout.NumChunks;

        if (!_memory.Read<int>(chunkCountAddress).TryUnwrap(out int chunks, out error))
        {
            yield return ObjectArrayError.ChunkCountUnreadable(_array, chunkCountAddress, error);
            yield break;
        }

        if (chunks is < 0 or > MaxChunks)
        {
            yield return ObjectArrayError.ChunkCountOutOfRange(_array, chunks, MaxChunks);
            yield break;
        }

        // Check the count of the array, so a sample fails on a wrong offset the same way a full read does.
        if (arrayCount > chunks * PointersPerChunk)
        {
            yield return ObjectArrayError.CountPastChunks(_array, arrayCount, chunks, PointersPerChunk);
            yield break;
        }

        foreach (var slot in ObjectSlots.ReadChunks(_memory, _array, _array + _layout.Objects, total, PointersPerChunk, _pointerItem))
        {
            yield return slot;
        }
    }
}
