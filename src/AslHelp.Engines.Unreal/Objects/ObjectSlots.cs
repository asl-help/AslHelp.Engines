using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Provides the reads of object pointers and the checks of an item layout that the <see cref="IObjectArray"/> shapes share.
/// </summary>
internal static class ObjectSlots
{
    private const int BlockLength = 1024;

    // Use 1 KB as the largest item, far above the 24 bytes of a real item, because a block of 1024 items has to fit in 1 buffer.
    private const int MaxItemSize = 1024;

    // Use 64M objects as the largest count, far above any real array, because a torn read or a wrong offset can give any number.
    private const int MaxObjects = 64 * 1024 * 1024;

    /// <summary>
    ///     Checks that an item has room for a pointer at its object offset.
    /// </summary>
    /// <param name="item">The offsets inside an item.</param>
    /// <param name="pointerSize">The size of a pointer in bytes.</param>
    /// <returns>
    ///     <paramref name="item"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="item"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     A pointer at the object offset of <paramref name="item"/> doesn't fit in the item.
    ///     The item is larger than 1024 bytes.
    /// </exception>
    public static ObjectItemLayout CheckItem(ObjectItemLayout item, int pointerSize)
    {
        ArgumentNullException.ThrowIfNull(item);

        return ValidateItem(item, pointerSize).TryUnwrapErr(out var error)
            ? throw new ArgumentOutOfRangeException(nameof(item), item, error.Message)
            : item;
    }

    /// <summary>
    ///     Reads the count of an array and checks it against the largest count of a real array.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, for the failure.</param>
    /// <param name="address">The address of the 4-byte count.</param>
    /// <returns>
    ///     The count or a failure with the array and either the failed read or the count outside 0 to 64M.
    /// </returns>
    public static Result<int> ReadCount(IMemoryReader memory, Address array, Address address)
    {
        if (!memory.Read<int>(address).TryUnwrap(out int count, out var error))
        {
            return ObjectArrayError.CountUnreadable(array, address, error);
        }

        return count is < 0 or > MaxObjects
            ? ObjectArrayError.CountOutOfRange(array, count, MaxObjects)
            : count;
    }

    /// <summary>
    ///     Reads the object pointer and the serial number of the item at <paramref name="address"/>.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, for the failure.</param>
    /// <param name="index">The position of the item, for the failure.</param>
    /// <param name="address">The address of the item.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <returns>
    ///     The item or a failure when a read fails. The serial number is <see langword="null"/> when <paramref name="item"/> doesn't have one.
    /// </returns>
    public static Result<ObjectItem> ReadItem(IMemoryReader memory, Address array, int index, Address address, ObjectItemLayout item)
    {
        var objectAddress = address + item.ObjectPointer;

        if (!memory.ReadPointer(objectAddress).TryUnwrap(out var obj, out var error))
        {
            return ObjectArrayError.BlockUnreadable(array, index, 1, objectAddress, error);
        }

        if (item.SerialNumber is not { } serialOffset)
        {
            return new ObjectItem(obj, null);
        }

        var serialAddress = address + serialOffset;

        return memory.Read<int>(serialAddress).TryUnwrap(out int serial, out error)
            ? new ObjectItem(obj, serial)
            : ObjectArrayError.BlockUnreadable(array, index, 1, serialAddress, error);
    }

    /// <summary>
    ///     Enumerates the object pointers of the items behind a table of chunk pointers, in position order.
    /// </summary>
    /// <remarks>
    ///     Keeps <see cref="Address.Zero"/> for each position of a chunk pointer of 0, because the engine allocates a chunk late.
    ///     Gives a failure for each position of a chunk whose pointer doesn't read and from the 1st block of a chunk where no item reads to the end of the chunk, then goes on with the next chunk.
    ///     Ends with 1 failure when the 1st chunk pointer doesn't read.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, for the failure.</param>
    /// <param name="table">The address of the 1st chunk pointer.</param>
    /// <param name="count">The number of pointers to read, above 0.</param>
    /// <param name="perChunk">The number of items in 1 chunk.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <returns>
    ///     The pointers, with a failure in place of each pointer that doesn't read.
    /// </returns>
    public static IEnumerable<Result<Address>> ReadChunks(
        IMemoryReader memory,
        Address array,
        Address table,
        int count,
        int perChunk,
        ObjectItemLayout item)
    {
        return Enumerate(memory, array, table, Address.Zero, count, perChunk, item);
    }

    /// <summary>
    ///     Enumerates the object pointers of consecutive items, in position order.
    /// </summary>
    /// <remarks>
    ///     Ends at the 1st block of 1024 items where no item reads, with the failure as the last item.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, for the failure.</param>
    /// <param name="start">The address of the 1st item.</param>
    /// <param name="count">The number of items to read, above 0.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <returns>
    ///     The pointers, with a failure in place of each pointer that doesn't read, then a failure if a block didn't read at all.
    /// </returns>
    public static IEnumerable<Result<Address>> Read(
        IMemoryReader memory,
        Address array,
        Address start,
        int count,
        ObjectItemLayout item)
    {
        return Enumerate(memory, array, null, start, count, count, item);
    }

    /// <summary>
    ///     Checks that a chunk of the array has room for at least 1 item.
    /// </summary>
    /// <param name="layout">The offsets inside the array.</param>
    /// <returns>
    ///     A success or a failure with the number of items per chunk when it's below 1.
    /// </returns>
    public static Result ValidateChunks(ChunkedObjectArrayLayout layout)
    {
        return layout.ElementsPerChunk < 1
            ? ObjectArrayError.ElementsPerChunkBelowOne(layout.ElementsPerChunk)
            : Result.Ok();
    }

    /// <summary>
    ///     Checks that an item has room for a pointer at its object offset and fits in the 1024-byte limit of an item.
    /// </summary>
    /// <param name="item">The offsets inside an item.</param>
    /// <param name="pointerSize">The size of a pointer in bytes.</param>
    /// <returns>
    ///     A success or a failure with the size of the item when a pointer at its object offset doesn't fit in it or it's larger than 1024 bytes.
    /// </returns>
    public static Result ValidateItem(ObjectItemLayout item, int pointerSize)
    {
        if (item.ObjectPointer < 0 || item.Size < (long)item.ObjectPointer + pointerSize)
        {
            return ObjectArrayError.ItemTooSmall(item.Size, pointerSize, item.ObjectPointer);
        }

        return item.Size > MaxItemSize
            ? ObjectArrayError.ItemTooLarge(item.Size, MaxItemSize)
            : Result.Ok();
    }

    /// <summary>
    ///     Decodes the pointer in <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">The 4 or 8 bytes of the pointer.</param>
    /// <param name="order">The byte order of the game.</param>
    /// <returns>
    ///     The pointer.
    /// </returns>
    public static Address PointerAt(ReadOnlySpan<byte> bytes, ByteOrder order)
    {
        bool littleEndian = order == ByteOrder.LittleEndian;

        return bytes.Length == sizeof(ulong)
            ? new Address(littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes))
            : new Address(littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }

    /// <summary>
    ///     Enumerates the object pointers of runs of consecutive items, in position order.
    /// </summary>
    /// <remarks>
    ///     Reads 1024 items at a time when the enumeration moves to them, so a caller can stop early.
    ///     Reads the items of a block that doesn't read 1 at a time, so an unreadable item only hides itself.
    ///     Reads items 1 at a time only until the 1st block where no item reads, so a wrong profile costs at most 1024 extra reads per enumeration.
    ///     Gives a failure for the rest of a run once a whole block doesn't read or its chunk pointer doesn't read, then goes on with the next run.
    ///     Ends with 1 failure when the 1st chunk pointer doesn't read.
    ///     Ends at a run without a table, because the rest of 1 allocation won't read either and reading it 1 item at a time costs 1 read per item.
    ///     Fails before the 1st pointer for a pointer size other than 4 or 8, because only those decode.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="array">The address of the array, for the failure.</param>
    /// <param name="table">The address of the 1st chunk pointer or <see langword="null"/> for 1 run at <paramref name="start"/>.</param>
    /// <param name="start">The address of the 1st item when <paramref name="table"/> is <see langword="null"/>.</param>
    /// <param name="count">The number of pointers to read, above 0.</param>
    /// <param name="perRun">The number of items in 1 run.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <returns>
    ///     The pointers, with a failure in place of each pointer that doesn't read.
    /// </returns>
    private static IEnumerable<Result<Address>> Enumerate(
        IMemoryReader memory,
        Address array,
        Address? table,
        Address start,
        int count,
        int perRun,
        ObjectItemLayout item)
    {
        var layout = memory.Layout;

        if (layout.PointerSize is not (sizeof(uint) or sizeof(ulong)))
        {
            yield return ObjectArrayError.PointerSizeUnsupported(array, layout.PointerSize);
            yield break;
        }

        byte[]? buffer = null;
        ResultError?[]? failures = null;
        bool deadBlockSeen = false;
        int position = 0;

        try
        {
            for (int run = 0; position < count; run++)
            {
                int length = Math.Min(perRun, count - position);

                if (table is { } chunks)
                {
                    var address = chunks + ((long)run * layout.PointerSize);

                    if (!memory.ReadPointer(address).TryUnwrap(out start, out var chunkError))
                    {
                        Result<Address> chunkFailure = ObjectArrayError.ChunkPointerUnreadable(array, run, address, chunkError);

                        // End at the 1st chunk pointer, because a table that doesn't read from its start doesn't have any chunk.
                        if (run == 0)
                        {
                            yield return chunkFailure;
                            yield break;
                        }

                        for (int slot = 0; slot < length; slot++)
                        {
                            yield return chunkFailure;
                        }

                        position += length;

                        continue;
                    }
                }

                if (table is not null && start == Address.Zero)
                {
                    for (int slot = 0; slot < length; slot++)
                    {
                        yield return Address.Zero;
                    }

                    position += length;

                    continue;
                }

                // Rent the buffer and return it when the enumeration ends, because a search reads the arrays on every tick.
                buffer ??= ArrayPool<byte>.Shared.Rent(Math.Min(BlockLength, Math.Min(perRun, count)) * item.Size);

                for (int done = 0; done < length; done += BlockLength)
                {
                    int blockLength = Math.Min(BlockLength, length - done);
                    var block = start + ((long)done * item.Size);

                    if (!memory.Read(block, buffer.AsSpan(0, blockLength * item.Size)).TryUnwrapErr(out var error))
                    {
                        for (int slot = 0; slot < blockLength; slot++)
                        {
                            yield return PointerAt(buffer.AsSpan((slot * item.Size) + item.ObjectPointer, layout.PointerSize), layout.ByteOrder);
                        }

                        continue;
                    }

                    failures ??= ArrayPool<ResultError?>.Shared.Rent(BlockLength);

                    if (!deadBlockSeen && ReadOneByOne(memory, array, position + done, block, blockLength, item, buffer, failures))
                    {
                        for (int slot = 0; slot < blockLength; slot++)
                        {
                            yield return failures[slot] is { } failure
                                ? failure
                                : PointerAt(buffer.AsSpan((slot * item.Size) + item.ObjectPointer, layout.PointerSize), layout.ByteOrder);
                        }

                        continue;
                    }

                    deadBlockSeen = true;

                    Result<Address> blockFailure = ObjectArrayError.BlockUnreadable(array, position + done, length - done, block, error);

                    if (table is null)
                    {
                        yield return blockFailure;
                        yield break;
                    }

                    for (int slot = done; slot < length; slot++)
                    {
                        yield return blockFailure;
                    }

                    break;
                }

                position += length;
            }
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (failures is not null)
            {
                ArrayPool<ResultError?>.Shared.Return(failures, clearArray: true);
            }
        }
    }

    // Read the pointer of each item of a block into its place in the buffer and the failure of each item that doesn't read into failures.
    // Return false when no item reads, so the caller can treat the block as gone.
    private static bool ReadOneByOne(
        IMemoryReader memory,
        Address array,
        int position,
        Address block,
        int length,
        ObjectItemLayout item,
        byte[] buffer,
        ResultError?[] failures)
    {
        int pointerSize = memory.Layout.PointerSize;
        bool anyRead = false;

        for (int slot = 0; slot < length; slot++)
        {
            var address = block + ((long)slot * item.Size) + item.ObjectPointer;

            if (memory.Read(address, buffer.AsSpan((slot * item.Size) + item.ObjectPointer, pointerSize)).TryUnwrapErr(out var error))
            {
                failures[slot] = ObjectArrayError.BlockUnreadable(array, position + slot, 1, address, error);
            }
            else
            {
                failures[slot] = null;
                anyRead = true;
            }
        }

        return anyRead;
    }
}
