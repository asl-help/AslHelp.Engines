using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of names from the blocks of a name pool, which is how Unreal Engine 4.23 and later store names.
/// </summary>
/// <remarks>
///     Caches each name by id, because a name never changes once the engine has made it.
///     A failed read stays out of the cache, because the pool can fill the entry later.
///     Isn't thread-safe, because the cache is a <see cref="Dictionary{TKey, TValue}"/> without a lock.
///     Fails an entry that ends past the byte cursor of the current block or past the end of its block, because the engine never splits an entry.
///     Passes an id inside another entry when its bytes read as an entry that ends inside the used bytes and has no 0 char, because the header holds no more to check.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="pool">The address of the name pool.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/> is <see langword="null"/>.
/// </exception>
public sealed class ChunkedNameTable(
    IMemoryReader memory,
    Address pool) : INameTable
{
    // Use the block index in the high 16 bits of an id, as FNameEntryHandle in the engine source does.
    private const int BlockBits = 16;

    // Use the 65536 2-byte units of FNameBlockOffsets as the size of a block, which is 128 KB in every 4.23 to 5.8 build.
    private const int BlockSize = (OffsetMask + 1) * Stride;

    // Skip the 2 4-byte cursors of FNameEntryAllocator behind its lock, which every 4.23 to 5.8 build has.
    private const int CursorsSize = 2 * sizeof(int);

    // Use the top 10 bits of the header for the length, as FNameEntryHeader does.
    private const int LengthShift = 6;

    // Use FNameMaxBlocks of the engine source, which is the most a torn id can index.
    private const int MaxBlocks = 8192;

    // Use the offset in the low 16 bits of an id, as FNameEntryHandle does.
    private const int OffsetMask = 0xFFFF;

    // Count the offset in 2-byte units, which is FNameEntryAllocator::Stride in the engine source.
    private const int Stride = 2;

    // Skip the 2-byte header, which AnsiName and WideName sit behind in every 4.23 to 5.8 profile.
    private const int TextOffset = 2;

    // Use the low bit of the header for UTF-16 text, as FNameEntryHeader does.
    private const int WideBit = 1;

    private readonly Dictionary<int, string> _cache = [];
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly Address _pool = pool;

    /// <summary>
    ///     Reads the name with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The id of the name, which is a block in the high 16 bits and an offset in 2-byte units in the low 16 bits.</param>
    /// <returns>
    ///     The text of the name or a failure with the id and the failed read when <paramref name="id"/> is out of range,
    ///     its block pointer is 0, its block or its entry lies past the cursors of the pool, its header has length 0, a read fails
    ///     or the text ends before the length of its header.
    /// </returns>
    public Result<string> Read(int id)
    {
        if (_cache.TryGetValue(id, out string? cached))
        {
            return cached;
        }

        var read = ReadEntry(id);

        if (read.TryUnwrap(out string? name))
        {
            _cache.Add(id, name);
        }

        return read;
    }

    private Result<string> ReadEntry(int id)
    {
        if (id < 0)
        {
            return NameTableError.IdNegative(id);
        }

        int block = id >> BlockBits;

        if (block >= MaxBlocks)
        {
            return NameTableError.BlockOutOfRange(id, block, MaxBlocks);
        }

        int pointerSize = _memory.Layout.PointerSize;
        var blockSlot = _pool + pointerSize + CursorsSize + (block * pointerSize);

        if (!_memory.ReadPointer(blockSlot).TryUnwrap(out var start, out var error))
        {
            return NameTableError.BlockPointerUnreadable(id, block, blockSlot, error);
        }

        if (start == Address.Zero)
        {
            return NameTableError.BlockNull(id, block);
        }

        if (!ReadUsedBytes(id, block).TryUnwrap(out int used, out error))
        {
            return error;
        }

        int offset = (id & OffsetMask) * Stride;
        var entry = start + offset;

        // Fail a header past the used bytes, because the bytes past the cursor of the current block aren't an entry yet.
        if (offset + TextOffset > used)
        {
            return NameTableError.EntryPastUsed(id, entry, block, offset + TextOffset, used);
        }

        if (!_memory.Read<ushort>(entry).TryUnwrap(out ushort header, out error))
        {
            return NameTableError.EntryHeaderUnreadable(id, entry, error);
        }

        int length = header >> LengthShift;

        // Fail a length of 0, because the pool doesn't store an empty name and a torn id can point at the 0 bytes past its cursor.
        if (length == 0)
        {
            return NameTableError.EntryLengthZero(id, entry);
        }

        bool wide = (header & WideBit) != 0;
        int end = offset + TextOffset + (length * (wide ? sizeof(char) : sizeof(byte)));

        // Fail an entry that ends past the used bytes, because the engine never splits an entry across blocks and the text bytes that an id inside another entry reads as its header often give a long length.
        if (end > used)
        {
            return NameTableError.EntryPastUsed(id, entry, block, end, used);
        }

        if (!_memory.ReadString(entry + TextOffset, length, wide ? StringType.Utf16 : StringType.Ascii).TryUnwrap(out string? text, out error))
        {
            return NameTableError.EntryCharactersUnreadable(id, entry, length, error);
        }

        // Fail a text shorter than its length, because a real text doesn't contain a 0 char and a torn id can point at any bytes.
        return text.Length == length
            ? text
            : NameTableError.EntryLengthMismatch(id, entry, length, text.Length);
    }

    private Result<int> ReadUsedBytes(int id, int block)
    {
        var address = _pool + _memory.Layout.PointerSize;

        // Read both cursors in 1 read, because the engine moves them together under the lock of the pool.
        if (!_memory.Read<ulong>(address).TryUnwrap(out ulong cursors, out var error))
        {
            return NameTableError.CursorsUnreadable(id, address, error);
        }

        (int current, int cursor) = unchecked(_memory.Layout.ByteOrder == ByteOrder.LittleEndian
            ? ((int)cursors, (int)(cursors >> 32))
            : ((int)(cursors >> 32), (int)cursors));

        if (block > current)
        {
            return NameTableError.BlockPastCurrent(id, block, current);
        }

        return block == current ? Math.Min(cursor, BlockSize) : BlockSize;
    }
}
