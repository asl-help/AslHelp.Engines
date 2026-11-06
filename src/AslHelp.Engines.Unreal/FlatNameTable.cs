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
    ///     The text of the name or <see langword="null"/> when <paramref name="id"/> is out of range, a read fails or the entry has another id.
    /// </returns>
    public string? Read(int id)
    {
        if (_cache.TryGetValue(id, out string? name))
        {
            return name;
        }

        name = ReadEntry(id);

        if (name is not null)
        {
            _cache.Add(id, name);
        }

        return name;
    }

    private string? ReadEntry(int id)
    {
        int pointerSize = _memory.Layout.PointerSize;

        if (id < 0
            || !_memory.Read<int>(_array + pointerSize).TryUnwrap(out int count)
            || id >= count
            || !_memory.ReadPointer(_array).TryUnwrap(out var data)
            || !_memory.ReadPointer(data + ((long)id * pointerSize)).TryUnwrap(out var entry)
            || entry == Address.Zero
            || !_memory.Read<int>(entry + _layout.Index).TryUnwrap(out int index)
            || (index >> 1) != id)
        {
            return null;
        }

        var type = (index & WideBit) != 0 ? StringType.Utf16 : StringType.Ascii;

        return _memory.ReadString(entry + _layout.Name, type, MaxNameLength).UnwrapOrDefault();
    }
}
