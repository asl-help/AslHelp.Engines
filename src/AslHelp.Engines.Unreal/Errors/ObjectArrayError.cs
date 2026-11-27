using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a part of an <see cref="IObjectArray"/> that did not read or a layout that doesn't fit it.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record ObjectArrayError : ResultError
{
    /// <summary>
    ///     Creates the error for a block of pointers that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="position">The position of the 1st pointer of the block.</param>
    /// <param name="length">The number of pointers in the block.</param>
    /// <param name="address">The address of the block.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError BlockUnreadable(Address array, int position, int length, Address address, ResultError cause)
    {
        return new BlockUnreadableError(array, position, length, address, cause);
    }

    /// <summary>
    ///     Creates the error for a chunk count below 0.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="chunks">The chunk count.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ChunkCountNegative(Address array, int chunks)
    {
        return new ChunkCountNegativeError(array, chunks);
    }

    /// <summary>
    ///     Creates the error for a chunk count outside the range of the shape.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="chunks">The chunk count.</param>
    /// <param name="max">The largest chunk count of the shape.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ChunkCountOutOfRange(Address array, int chunks, int max)
    {
        return new ChunkCountOutOfRangeError(array, chunks, max);
    }

    /// <summary>
    ///     Creates the error for a chunk count that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="address">The address of the chunk count.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ChunkCountUnreadable(Address array, Address address, ResultError cause)
    {
        return new ChunkCountUnreadableError(array, address, cause);
    }

    /// <summary>
    ///     Creates the error for a chunk pointer that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="chunk">The index of the chunk.</param>
    /// <param name="address">The address of the chunk pointer.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ChunkPointerUnreadable(Address array, int chunk, Address address, ResultError cause)
    {
        return new ChunkPointerUnreadableError(array, chunk, address, cause);
    }

    /// <summary>
    ///     Creates the error for a count outside the range of a real array.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="count">The count of the array.</param>
    /// <param name="max">The largest count of a real array.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError CountOutOfRange(Address array, int count, int max)
    {
        return new CountOutOfRangeError(array, count, max);
    }

    /// <summary>
    ///     Creates the error for a count past what the allocated chunks have room for.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="count">The count of the array.</param>
    /// <param name="chunks">The chunk count.</param>
    /// <param name="perChunk">The number of objects in 1 chunk.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError CountPastChunks(Address array, int count, int chunks, int perChunk)
    {
        return new CountPastChunksError(array, count, chunks, perChunk);
    }

    /// <summary>
    ///     Creates the error for a count that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="address">The address of the count.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError CountUnreadable(Address array, Address address, ResultError cause)
    {
        return new CountUnreadableError(array, address, cause);
    }

    /// <summary>
    ///     Creates the error for a data pointer that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="address">The address of the data pointer.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError DataUnreadable(Address array, Address address, ResultError cause)
    {
        return new DataUnreadableError(array, address, cause);
    }

    /// <summary>
    ///     Creates the error for a chunk without room for an item.
    /// </summary>
    /// <param name="elementsPerChunk">The number of items in 1 chunk.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ElementsPerChunkBelowOne(int elementsPerChunk)
    {
        return new ElementsPerChunkBelowOneError(elementsPerChunk);
    }

    /// <summary>
    ///     Creates the error for an item larger than the 1024-byte limit of an item.
    /// </summary>
    /// <param name="size">The size of 1 item in bytes.</param>
    /// <param name="max">The largest size of an item in bytes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ItemTooLarge(int size, int max)
    {
        return new ItemTooLargeError(size, max);
    }

    /// <summary>
    ///     Creates the error for an item without room for a pointer at its object offset.
    /// </summary>
    /// <param name="size">The size of 1 item in bytes.</param>
    /// <param name="pointerSize">The pointer size of the memory.</param>
    /// <param name="offset">The offset of the object pointer in an item.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError ItemTooSmall(int size, int pointerSize, int offset)
    {
        return new ItemTooSmallError(size, pointerSize, offset);
    }

    /// <summary>
    ///     Creates the error for a pointer size that the block reads of the pointers don't decode.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="pointerSize">The pointer size of the memory.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError PointerSizeUnsupported(Address array, int pointerSize)
    {
        return new PointerSizeUnsupportedError(array, pointerSize);
    }
}

/// <summary>
///     Represents a block of pointers that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Position">The position of the 1st pointer of the block.</param>
/// <param name="Length">The number of pointers in the block.</param>
/// <param name="Address">The address of the block.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record BlockUnreadableError(
    Address Array,
    int Position,
    int Length,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayBlockUnreadable(Length, Position, Array, Address, Cause.Message);
}

/// <summary>
///     Represents a chunk count below 0.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Chunks">The chunk count.</param>
file sealed record ChunkCountNegativeError(
    Address Array,
    int Chunks) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayChunkCountNegative(Array, Chunks);
}

/// <summary>
///     Represents a chunk count outside the range of the shape.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Chunks">The chunk count.</param>
/// <param name="Max">The largest chunk count of the shape.</param>
file sealed record ChunkCountOutOfRangeError(
    Address Array,
    int Chunks,
    int Max) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayChunkCountOutOfRange(Array, Chunks, Max);
}

/// <summary>
///     Represents a chunk count that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ChunkCountUnreadableError(
    Address Array,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayChunkCountUnreadable(Array, Address, Cause.Message);
}

/// <summary>
///     Represents a chunk pointer that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Chunk">The index of the chunk.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ChunkPointerUnreadableError(
    Address Array,
    int Chunk,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayChunkPointerUnreadable(Array, Chunk, Address, Cause.Message);
}

/// <summary>
///     Represents a count outside the range of a real array.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Count">The count of the array.</param>
/// <param name="Max">The largest count of a real array.</param>
file sealed record CountOutOfRangeError(
    Address Array,
    int Count,
    int Max) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayCountOutOfRange(Array, Count, Max);
}

/// <summary>
///     Represents a count past what the allocated chunks have room for.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Count">The count of the array.</param>
/// <param name="Chunks">The chunk count.</param>
/// <param name="PerChunk">The number of objects in 1 chunk.</param>
file sealed record CountPastChunksError(
    Address Array,
    int Count,
    int Chunks,
    int PerChunk) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayCountPastChunks(Array, Count, Chunks, PerChunk);
}

/// <summary>
///     Represents a count of an object array that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record CountUnreadableError(
    Address Array,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectArrayCountUnreadable(Array, Address, Cause.Message);
}

/// <summary>
///     Represents a data pointer of an object array that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record DataUnreadableError(
    Address Array,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectArrayDataUnreadable(Array, Address, Cause.Message);
}

file sealed record ElementsPerChunkBelowOneError(
    int ElementsPerChunk) : ObjectArrayError
{
    public override string Message => SR.FormatObjectArrayElementsPerChunkBelowOne(ElementsPerChunk);
}

file sealed record ItemTooLargeError(
    int Size,
    int Max) : ObjectArrayError
{
    public override string Message => SR.FormatObjectItemTooLarge(Size, Max);
}

file sealed record ItemTooSmallError(
    int Size,
    int PointerSize,
    int Offset) : ObjectArrayError
{
    public override string Message => SR.FormatObjectItemTooSmall(Size, PointerSize, Offset);
}

/// <summary>
///     Represents a pointer size that the block reads of the pointers don't decode.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="PointerSize">The pointer size of the memory.</param>
file sealed record PointerSizeUnsupportedError(
    Address Array,
    int PointerSize) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatObjectArrayPointerSizeUnsupported(Array, PointerSize);
}
