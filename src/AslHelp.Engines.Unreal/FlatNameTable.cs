using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of names from a flat array of entry pointers, which is how Unreal Engine 3 stores names.
/// </summary>
/// <remarks>
///     Caches each name by id, because a name never changes once the engine has made it.
///     A failed read stays out of the cache, because the engine can add the entry later.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="array">The address of the array, which is a data pointer and then a 4-byte count.</param>
/// <param name="layout">The offsets inside a name entry.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/> or <paramref name="layout"/> is <see langword="null"/>.
/// </exception>
public sealed class FlatNameTable(
    IMemoryReader memory,
    Address array,
    NameEntryLayout layout)
{
    private const int MaxNameLength = 256;
    private const int WideBit = 1;

    private readonly Address _array = array;
    private readonly Dictionary<int, string> _cache = [];
    private readonly NameEntryLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    /// <summary>
    ///     Reads the name with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The position of the name in the array.</param>
    /// <returns>
    ///     The text of the name or a failure with the id, the entry and the failed read when <paramref name="id"/> is out of range,
    ///     a read fails or the entry has another id.
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

        int pointerSize = _memory.Layout.PointerSize;
        var countAddress = _array + pointerSize;

        if (!_memory.Read<int>(countAddress).TryUnwrap(out int count, out var error))
        {
            return NameTableError.HeaderUnreadable(_array, countAddress, error);
        }

        if (id >= count)
        {
            return NameTableError.IdOutOfRange(id, count);
        }

        if (!_memory.ReadPointer(_array).TryUnwrap(out var data, out error))
        {
            return NameTableError.HeaderUnreadable(_array, _array, error);
        }

        var slot = data + ((long)id * pointerSize);

        if (!_memory.ReadPointer(slot).TryUnwrap(out var entry, out error))
        {
            return NameTableError.EntryPointerUnreadable(id, slot, error);
        }

        if (entry == Address.Zero)
        {
            return NameTableError.EntryNull(id);
        }

        var indexAddress = entry + _layout.Index;

        if (!_memory.Read<int>(indexAddress).TryUnwrap(out int index, out error))
        {
            return NameTableError.IndexUnreadable(id, entry, indexAddress, _layout.Index, error);
        }

        if ((index >> 1) != id)
        {
            return NameTableError.IndexMismatch(id, entry, index, _layout.Index);
        }

        var type = (index & WideBit) != 0 ? StringType.Utf16 : StringType.Ascii;
        var textAddress = entry + _layout.Name;

        return _memory.ReadString(textAddress, type, MaxNameLength).TryUnwrap(out string? text, out error)
            ? text
            : NameTableError.TextUnreadable(id, entry, textAddress, _layout.Name, error);
    }
}
