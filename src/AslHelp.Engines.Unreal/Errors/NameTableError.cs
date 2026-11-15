using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a name that did not read from an <see cref="INameTable"/>.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record NameTableError : ResultError
{
    /// <summary>
    ///     Creates the error for a block pointer of 0.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="block">The index of the block.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError BlockNull(int id, int block)
    {
        return new BlockNullError(id, block);
    }

    /// <summary>
    ///     Creates the error for an id in a block past the blocks of a name pool.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="block">The index of the block.</param>
    /// <param name="max">The number of blocks of a name pool.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError BlockOutOfRange(int id, int block, int max)
    {
        return new BlockOutOfRangeError(id, block, max);
    }

    /// <summary>
    ///     Creates the error for an id in a block past the current block of a name pool.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="block">The index of the block.</param>
    /// <param name="current">The index of the current block of the pool.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError BlockPastCurrent(int id, int block, int current)
    {
        return new BlockPastCurrentError(id, block, current);
    }

    /// <summary>
    ///     Creates the error for a block pointer that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="block">The index of the block.</param>
    /// <param name="address">The address of the block pointer.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError BlockPointerUnreadable(int id, int block, Address address, ResultError cause)
    {
        return new BlockPointerUnreadableError(id, block, address, cause);
    }

    /// <summary>
    ///     Creates the error for a chunk pointer of 0.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="chunk">The index of the chunk.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError ChunkNull(int id, int chunk)
    {
        return new ChunkNullError(id, chunk);
    }

    /// <summary>
    ///     Creates the error for a chunk pointer that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="chunk">The index of the chunk.</param>
    /// <param name="address">The address of the chunk pointer.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError ChunkPointerUnreadable(int id, int chunk, Address address, ResultError cause)
    {
        return new ChunkPointerUnreadableError(id, chunk, address, cause);
    }

    /// <summary>
    ///     Creates the error for cursors of a name pool that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="address">The address of the cursors.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError CursorsUnreadable(int id, Address address, ResultError cause)
    {
        return new CursorsUnreadableError(id, address, cause);
    }

    /// <summary>
    ///     Creates the error for the characters of a pool entry that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="length">The length from the header of the entry.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryCharactersUnreadable(int id, Address entry, int length, ResultError cause)
    {
        return new EntryCharactersUnreadableError(id, entry, length, cause);
    }

    /// <summary>
    ///     Creates the error for the header of a pool entry that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryHeaderUnreadable(int id, Address entry, ResultError cause)
    {
        return new EntryHeaderUnreadableError(id, entry, cause);
    }

    /// <summary>
    ///     Creates the error for a pool entry whose text ends before the length of its header.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="length">The length from the header of the entry.</param>
    /// <param name="characters">The number of characters in front of the terminator.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryLengthMismatch(int id, Address entry, int length, int characters)
    {
        return new EntryLengthMismatchError(id, entry, length, characters);
    }

    /// <summary>
    ///     Creates the error for a pool entry whose header has a length of 0.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryLengthZero(int id, Address entry)
    {
        return new EntryLengthZeroError(id, entry);
    }

    /// <summary>
    ///     Creates the error for a null entry pointer.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryNull(int id)
    {
        return new EntryNullError(id);
    }

    /// <summary>
    ///     Creates the error for a pool entry that ends past the used bytes of its block.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="block">The index of the block.</param>
    /// <param name="end">The offset in the block where the entry ends.</param>
    /// <param name="used">The number of used bytes of the block.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryPastUsed(int id, Address entry, int block, int end, int used)
    {
        return new EntryPastUsedError(id, entry, block, end, used);
    }

    /// <summary>
    ///     Creates the error for an entry pointer that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="address">The address of the entry pointer.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryPointerUnreadable(int id, Address address, ResultError cause)
    {
        return new EntryPointerUnreadableError(id, address, cause);
    }

    /// <summary>
    ///     Creates the error for a data pointer or a count of the array that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError HeaderUnreadable(Address array, Address address, ResultError cause)
    {
        return new HeaderUnreadableError(array, address, cause);
    }

    /// <summary>
    ///     Creates the error for an id below 0.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IdNegative(int id)
    {
        return new IdNegativeError(id);
    }

    /// <summary>
    ///     Creates the error for an id at or past the count of the array.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="count">The count of the array.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IdOutOfRange(int id, int count)
    {
        return new IdOutOfRangeError(id, count);
    }

    /// <summary>
    ///     Creates the error for an id past the names of a name pool.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="max">The number of names of a name pool.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IdPastPool(int id, int max)
    {
        return new IdPastPoolError(id, max);
    }

    /// <summary>
    ///     Creates the error for an entry whose index is the index of another id.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="index">The index read from the entry.</param>
    /// <param name="offset">The offset of the index in the entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IndexMismatch(int id, Address entry, int index, int offset)
    {
        return new IndexMismatchError(id, entry, index, offset);
    }

    /// <summary>
    ///     Creates the error for an index that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="address">The address of the index.</param>
    /// <param name="offset">The offset of the index in the entry.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IndexUnreadable(int id, Address entry, Address address, int offset, ResultError cause)
    {
        return new EntryIndexUnreadableError(id, entry, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a pointer to the chunk table that is 0.
    /// </summary>
    /// <param name="table">The address of the pointer to the chunk table.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError TableNull(Address table)
    {
        return new TableNullError(table);
    }

    /// <summary>
    ///     Creates the error for a pointer to the chunk table that did not read.
    /// </summary>
    /// <param name="table">The address of the pointer to the chunk table.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError TablePointerUnreadable(Address table, ResultError cause)
    {
        return new TablePointerUnreadableError(table, cause);
    }

    /// <summary>
    ///     Creates the error for a text that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="address">The address of the text.</param>
    /// <param name="offset">The offset of the text in the entry.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError TextUnreadable(int id, Address entry, Address address, int offset, ResultError cause)
    {
        return new EntryTextUnreadableError(id, entry, address, offset, cause);
    }
}

/// <summary>
///     Represents a block pointer of 0.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Block">The index of the block.</param>
file sealed record BlockNullError(
    int Id,
    int Block) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameBlockNull(Id, Block);
}

/// <summary>
///     Represents an id in a block past the blocks of a name pool.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Block">The index of the block.</param>
/// <param name="Max">The number of blocks of a name pool.</param>
file sealed record BlockOutOfRangeError(
    int Id,
    int Block,
    int Max) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameBlockOutOfRange(Id, Block, Max);
}

/// <summary>
///     Represents an id in a block past the current block of a name pool.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Block">The index of the block.</param>
/// <param name="Current">The index of the current block of the pool.</param>
file sealed record BlockPastCurrentError(
    int Id,
    int Block,
    int Current) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameBlockPastCurrent(Id, Block, Current);
}

/// <summary>
///     Represents a block pointer that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Block">The index of the block.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record BlockPointerUnreadableError(
    int Id,
    int Block,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameBlockPointerUnreadable(Id, Block, Address, Cause.Message);
}

/// <summary>
///     Represents a chunk pointer of 0.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Chunk">The index of the chunk.</param>
file sealed record ChunkNullError(
    int Id,
    int Chunk) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameChunkNull(Id, Chunk);
}

/// <summary>
///     Represents a chunk pointer that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Chunk">The index of the chunk.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ChunkPointerUnreadableError(
    int Id,
    int Chunk,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameChunkPointerUnreadable(Id, Chunk, Address, Cause.Message);
}

/// <summary>
///     Represents cursors of a name pool that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Address">The address of the cursors.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record CursorsUnreadableError(
    int Id,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameCursorsUnreadable(Id, Address, Cause.Message);
}

/// <summary>
///     Represents the characters of a pool entry that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Length">The length from the header of the entry.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryCharactersUnreadableError(
    int Id,
    Address Entry,
    int Length,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryCharactersUnreadable(Id, Entry, Length, Cause.Message);
}

/// <summary>
///     Represents the header of a pool entry that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryHeaderUnreadableError(
    int Id,
    Address Entry,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryHeaderUnreadable(Id, Entry, Cause.Message);
}

/// <summary>
///     Represents an index of a name entry that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the entry.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryIndexUnreadableError(
    int Id,
    Address Entry,
    Address Address,
    int Offset,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatNameEntryIndexUnreadable(Id, Entry, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a pool entry whose text ends before the length of its header.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Length">The length from the header of the entry.</param>
/// <param name="Characters">The number of characters in front of the terminator.</param>
file sealed record EntryLengthMismatchError(
    int Id,
    Address Entry,
    int Length,
    int Characters) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryLengthMismatch(Id, Entry, Length, Characters);
}

/// <summary>
///     Represents a pool entry whose header has a length of 0.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
file sealed record EntryLengthZeroError(
    int Id,
    Address Entry) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryLengthZero(Id, Entry);
}

/// <summary>
///     Represents a pool entry that ends past the used bytes of its block.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Block">The index of the block.</param>
/// <param name="End">The offset in the block where the entry ends.</param>
/// <param name="Used">The number of used bytes of the block.</param>
file sealed record EntryPastUsedError(
    int Id,
    Address Entry,
    int Block,
    int End,
    int Used) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryPastUsed(Id, Entry, End, Used, Block);
}

/// <summary>
///     Represents the text of a name entry that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the entry.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryTextUnreadableError(
    int Id,
    Address Entry,
    Address Address,
    int Offset,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatNameEntryTextUnreadable(Id, Entry, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a null entry pointer.
/// </summary>
/// <param name="Id">The id of the name.</param>
file sealed record EntryNullError(
    int Id) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryNull(Id);
}

/// <summary>
///     Represents an entry pointer that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Address">The address of the entry pointer.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryPointerUnreadableError(
    int Id,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryPointerUnreadable(Id, Address, Cause.Message);
}

/// <summary>
///     Represents a data pointer or a count of the array that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record HeaderUnreadableError(
    Address Array,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameArrayHeaderUnreadable(Array, Address, Cause.Message);
}

/// <summary>
///     Represents an id below 0.
/// </summary>
/// <param name="Id">The id of the name.</param>
file sealed record IdNegativeError(
    int Id) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameIdNegative(Id);
}

/// <summary>
///     Represents an id at or past the count of the array.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Count">The count of the array.</param>
file sealed record IdOutOfRangeError(
    int Id,
    int Count) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameIdOutOfRange(Id, Count);
}

/// <summary>
///     Represents an id past the names of a name pool.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Max">The number of names of a name pool.</param>
file sealed record IdPastPoolError(
    int Id,
    int Max) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameIdPastPool(Id, Max);
}

/// <summary>
///     Represents an entry whose index is the index of another id.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Index">The index read from the entry.</param>
/// <param name="Offset">The offset of the index in the entry.</param>
file sealed record IndexMismatchError(
    int Id,
    Address Entry,
    int Index,
    int Offset) : NameTableError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatNameEntryIndexMismatch(Id, Entry, Index, Index >> 1, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a pointer to the chunk table that is 0.
/// </summary>
/// <param name="Table">The address of the pointer to the chunk table.</param>
file sealed record TableNullError(
    Address Table) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameTableNull(Table);
}

/// <summary>
///     Represents a pointer to the chunk table that did not read.
/// </summary>
/// <param name="Table">The address of the pointer to the chunk table.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record TablePointerUnreadableError(
    Address Table,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameTablePointerUnreadable(Table, Cause.Message);
}
