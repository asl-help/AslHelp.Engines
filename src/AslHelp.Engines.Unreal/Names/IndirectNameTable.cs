using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Names;

/// <summary>
///     Provides reads of names through the chunk table of a name pool, which is how Unreal Engine 4.0 to 4.22 store names.
/// </summary>
/// <remarks>
///     Caches each name by id, because a name never changes once the engine has made it.
///     A failed read stays out of the cache, because the engine can add the entry later.
///     Isn't thread-safe, because the cache is a <see cref="Dictionary{TKey, TValue}"/> without a lock.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="table">The address of the static that points at the table of chunk pointers.</param>
/// <param name="layout">The offsets inside a name entry.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/> or <paramref name="layout"/> is <see langword="null"/>.
/// </exception>
public sealed class IndirectNameTable(
    IMemoryReader memory,
    Address table,
    NameEntryLayout layout) : INameTable
{
    // Use the chunk size of TStaticIndirectArrayThreadSafeRead in the engine source, which every 4.0 to 4.22 build has.
    private const int EntriesPerChunk = 16384;

    // Use the 128 chunks of that array, which are 2M names and the most a torn id can index.
    private const int MaxEntries = 2_097_152;

    // Use NAME_SIZE of the engine source, which is the longest name.
    private const int MaxNameLength = 1024;

    private readonly Dictionary<int, string> _cache = [];
    private readonly NameEntryLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly Address _table = table;

    /// <summary>
    ///     Reads the name with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The position of the name, which is chunk <c>id / 16384</c> and slot <c>id % 16384</c>.</param>
    /// <returns>
    ///     The text of the name or a failure with the id and the failed read when <paramref name="id"/> is out of range,
    ///     a pointer is 0, a read fails or the entry has another id.
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

        if (id >= MaxEntries)
        {
            return NameTableError.IdPastPool(id, MaxEntries);
        }

        if (!_memory.ReadPointer(_table).TryUnwrap(out var chunks, out var error))
        {
            return NameTableError.TablePointerUnreadable(_table, error);
        }

        if (chunks == Address.Zero)
        {
            return NameTableError.TableNull(_table);
        }

        int pointerSize = _memory.Layout.PointerSize;
        int chunk = id / EntriesPerChunk;
        var chunkSlot = chunks + (chunk * pointerSize);

        if (!_memory.ReadPointer(chunkSlot).TryUnwrap(out var entries, out error))
        {
            return NameTableError.ChunkPointerUnreadable(id, chunk, chunkSlot, error);
        }

        if (entries == Address.Zero)
        {
            return NameTableError.ChunkNull(id, chunk);
        }

        var entrySlot = entries + (id % EntriesPerChunk * pointerSize);

        return _memory.ReadPointer(entrySlot).TryUnwrap(out var entry, out error)
            ? NameEntry.Read(_memory, id, entry, _layout, MaxNameLength)
            : NameTableError.EntryPointerUnreadable(id, entrySlot, error);
    }
}
