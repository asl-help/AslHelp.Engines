using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Reads the slots of the sparse array of a set or a map and the bits that mark which slots are used.
/// </summary>
/// <remarks>
///     Keeps the allocation bits in a buffer, so a read doesn't allocate. Isn't thread-safe.
/// </remarks>
internal sealed class SparseArray
{
    private const int BitsPerWord = 32;

    private readonly SparseArrayLayout _layout;
    private readonly int _maxSlots;
    private readonly string _name;
    private readonly int _start;

    private uint[] _words = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="SparseArray"/> class.
    /// </summary>
    /// <param name="name">The name of the set or the map, for the messages.</param>
    /// <param name="layout">The offsets inside the sparse array.</param>
    /// <param name="start">The offset of the sparse array in the object or struct that has the set or the map.</param>
    /// <param name="maxSlots">The most slots a read takes, because a torn or wrong count can be huge.</param>
    public SparseArray(string name, SparseArrayLayout layout, int start, int maxSlots)
    {
        _name = name;
        _layout = layout;
        _start = start;
        _maxSlots = maxSlots;
    }

    /// <summary>
    ///     Creates the read of the number of used slots of <paramref name="property"/>.
    /// </summary>
    /// <param name="property">The set or map property of the sparse array.</param>
    /// <returns>
    ///     The read.
    /// </returns>
    public ValueAccess<int> CreateCount(UnrealProperty property)
    {
        return new SparseCount(property, this);
    }

    /// <summary>
    ///     Finds the used slot at the position that <paramref name="key"/> gives, counting only the used slots in slot order.
    /// </summary>
    /// <remarks>
    ///     Gives a set of objects and a map keyed by objects an index, because a path key can't be an address.
    ///     The position of an element moves when the game frees a slot in front of it.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the property.</param>
    /// <param name="slotSize">The size of 1 slot in bytes.</param>
    /// <param name="key">The position as written in the path.</param>
    /// <returns>
    ///     The address of the slot or a failure when the key isn't an integer, the position isn't below the number of used slots or a read fails.
    /// </returns>
    public Result<Address> FindUsed(IMemoryReader memory, Address container, int slotSize, ElementKey key)
    {
        if (!key.As<int>(this, _name).TryUnwrap(out int position, out var error)
            || !ReadSlots(memory, container).TryUnwrap(out var slots, out error))
        {
            return error;
        }

        int used = 0;

        for (int slot = 0; slot < slots.SlotCount; slot++)
        {
            if (!IsUsed(slot))
            {
                continue;
            }

            if (used == position)
            {
                return slots.Data + ((long)slot * slotSize);
            }

            used++;
        }

        return key.OutOfRange(_name, position, used);
    }

    /// <summary>
    ///     Checks whether the slot <paramref name="slot"/> of the last <see cref="ReadSlots"/> is used.
    /// </summary>
    /// <param name="slot">The slot, below the slot count of the last read.</param>
    /// <returns>
    ///     <see langword="true"/> when its allocation bit is set.
    /// </returns>
    public bool IsUsed(int slot)
    {
        return (_words[slot / BitsPerWord] & (1u << (slot % BitsPerWord))) != 0;
    }

    /// <summary>
    ///     Reads the number of used slots, which is the number of slots minus the number of free slots.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the property.</param>
    /// <returns>
    ///     The number or a failure when a read fails, a number is below 0 or there are more free slots than slots.
    /// </returns>
    public Result<int> ReadCount(IMemoryReader memory, Address container)
    {
        var start = container + _start;

        if (!ReadInt32(memory, container, start + _layout.Count).TryUnwrap(out int slots, out var error)
            || !ReadInt32(memory, container, start + _layout.NumFreeIndices).TryUnwrap(out int free, out error))
        {
            return error;
        }

        if (slots < 0)
        {
            return CollectionError.CountNegative(_name, slots);
        }

        if (free < 0 || free > slots)
        {
            return CollectionError.FreeSlotsOutOfRange(_name, free, slots);
        }

        return slots - free;
    }

    /// <summary>
    ///     Reads the pointer to the slots, the number of slots and the allocation bits, which <see cref="IsUsed"/> then tests.
    /// </summary>
    /// <remarks>
    ///     Reads the bits behind the secondary pointer when it isn't 0, otherwise the bits inside the sparse array.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the property.</param>
    /// <returns>
    ///     The pointer and the number of slots or a failure when a read fails or the number of slots is below 0 or past the bound.
    /// </returns>
    public Result<(Address Data, int SlotCount)> ReadSlots(IMemoryReader memory, Address container)
    {
        var start = container + _start;

        if (!ReadInt32(memory, container, start + _layout.Count).TryUnwrap(out int slots, out var error))
        {
            return error;
        }

        if (slots < 0)
        {
            return CollectionError.CountNegative(_name, slots);
        }

        if (slots > _maxSlots)
        {
            return CollectionError.TooLong(_name, slots, _maxSlots);
        }

        if (slots == 0)
        {
            return (Address.Zero, 0);
        }

        if (!ReadPointer(memory, container, start + _layout.Data).TryUnwrap(out var data, out error)
            || !ReadPointer(memory, container, start + _layout.NumBits - memory.Layout.PointerSize).TryUnwrap(out var secondary, out error))
        {
            return error;
        }

        if (data == Address.Zero)
        {
            return CollectionError.DataNull(_name, slots);
        }

        int wordCount = (slots + BitsPerWord - 1) / BitsPerWord;

        if (_words.Length < wordCount)
        {
            _words = new uint[Math.Max(wordCount, (int)Math.Min(_words.Length * 2L, int.MaxValue))];
        }

        var words = _words.AsSpan(0, wordCount);
        var bits = secondary == Address.Zero ? start + _layout.InlineBits : secondary;

        if (memory.Read(bits, MemoryMarshal.AsBytes(words)).TryUnwrapErr(out error))
        {
            return ValueError.Unreadable(container, _name, bits, error);
        }

        // Swap the bytes of each word when the game's byte order isn't the host's, because the read copies the bytes as they are.
        if (BitConverter.IsLittleEndian != (memory.Layout.ByteOrder == ByteOrder.LittleEndian))
        {
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = BinaryPrimitives.ReverseEndianness(words[i]);
            }
        }

        return (data, slots);
    }

    private Result<int> ReadInt32(IMemoryReader memory, Address container, Address address)
    {
        return memory.Read<int>(address).TryUnwrap(out int value, out var error)
            ? value
            : ValueError.Unreadable(container, _name, address, error);
    }

    private Result<Address> ReadPointer(IMemoryReader memory, Address container, Address address)
    {
        return memory.ReadPointer(address).TryUnwrap(out var value, out var error)
            ? value
            : ValueError.Unreadable(container, _name, address, error);
    }
}

file sealed class SparseCount(
    UnrealProperty property,
    SparseArray sparseArray) : ValueAccess<int>(property)
{
    public override Result<int> Read(IMemoryReader memory, Address container, int last)
    {
        return sparseArray.ReadCount(memory, container);
    }
}
