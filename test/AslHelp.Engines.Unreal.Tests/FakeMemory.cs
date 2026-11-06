using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Reads from byte blocks that a test adds and counts the reads.
/// </summary>
/// <remarks>
///     A read succeeds only when all its bytes sit in 1 block, because a real read fails past the end of committed memory.
/// </remarks>
internal sealed class FakeMemory : IMemoryReader
{
    private const int EntriesOffset = 0x1000;
    private const int EntrySpacing = 0x100;
    private const int ObjectSize = 0x100;

    private readonly List<(Address Start, byte[] Bytes)> _blocks = [];
    private readonly Dictionary<Address, int> _readsAt = [];

    /// <inheritdoc/>
    public MemoryLayout Layout { get; init; } = MemoryLayout.X86;

    /// <summary>
    ///     Gets the number of reads so far.
    /// </summary>
    public int Reads { get; private set; }

    /// <summary>
    ///     Adds a block of readable bytes.
    /// </summary>
    /// <param name="start">The address of the 1st byte.</param>
    /// <param name="bytes">The bytes, which later writes change.</param>
    public void Add(Address start, byte[] bytes)
    {
        _blocks.Add((start, bytes));
    }

    /// <summary>
    ///     Adds a name array and its block of entry pointers.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="data">The address of the entry pointers.</param>
    /// <param name="count">The count of the array, which can differ from the number of entry pointers.</param>
    /// <param name="entries">The entry pointers.</param>
    public void AddNameArray(Address array, Address data, int count, params Address[] entries)
    {
        Add(array, new byte[Layout.PointerSize + sizeof(int)]);
        WritePointer(array, data);
        WriteInt32(array + Layout.PointerSize, count);

        Add(data, new byte[entries.Length * Layout.PointerSize]);

        for (int i = 0; i < entries.Length; i++)
        {
            WritePointer(data + (i * Layout.PointerSize), entries[i]);
        }
    }

    /// <summary>
    ///     Adds a name entry whose block ends right after the terminator of the text.
    /// </summary>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="layout">The offsets inside the entry.</param>
    /// <param name="index">The shifted id, with the low bit set to write the text as UTF-16.</param>
    /// <param name="text">The text.</param>
    public void AddNameEntry(Address entry, NameEntryLayout layout, int index, string text)
    {
        var encoding = (index & 1) != 0 ? Encoding.Unicode : Encoding.ASCII;
        byte[] terminated = encoding.GetBytes(text + '\0');
        byte[] bytes = new byte[layout.Name + terminated.Length];

        terminated.CopyTo(bytes, layout.Name);

        Add(entry, bytes);
        WriteInt32(entry + layout.Index, index);
    }

    /// <summary>
    ///     Adds a name array with an ASCII entry for each name, so a name's id is its position in <paramref name="names"/>.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="layout">The offsets inside an entry.</param>
    /// <param name="names">The text of the entries.</param>
    public void AddNames(Address array, NameEntryLayout layout, params string[] names)
    {
        var data = array + EntrySpacing;
        var entries = new Address[names.Length];

        for (int id = 0; id < names.Length; id++)
        {
            entries[id] = array + EntriesOffset + (id * EntrySpacing);
            AddNameEntry(entries[id], layout, id << 1, names[id]);
        }

        AddNameArray(array, data, names.Length, entries);
    }

    /// <summary>
    ///     Adds an object in a block large enough for the struct members of a test.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="layout">The offsets inside the object.</param>
    /// <param name="nameId">The name id.</param>
    /// <param name="number">The name number.</param>
    /// <param name="outer">The outer object.</param>
    /// <param name="klass">The class.</param>
    public void AddObject(Address obj, ObjectLayout layout, int nameId, int number = 0, Address outer = default, Address klass = default)
    {
        Add(obj, new byte[ObjectSize]);
        WriteInt32(obj + layout.Name, nameId);
        WriteInt32(obj + layout.Name + sizeof(int), number);
        WritePointer(obj + layout.Outer, outer);
        WritePointer(obj + layout.Class, klass);
    }

    /// <summary>
    ///     Writes a 4-byte value into the block that contains <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the value.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentOutOfRangeException">No block contains <paramref name="address"/>.</exception>
    public void WriteInt32(Address address, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(BytesAt(address, sizeof(int)), value);
    }

    /// <summary>
    ///     Writes a pointer into the block that contains <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="value">The pointer.</param>
    /// <exception cref="ArgumentOutOfRangeException">No block contains <paramref name="address"/>.</exception>
    public void WritePointer(Address address, Address value)
    {
        var bytes = BytesAt(address, Layout.PointerSize);

        if (Layout.PointerSize == sizeof(ulong))
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value.Value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value.Value);
        }
    }

    /// <inheritdoc/>
    public Result Read(Address address, Span<byte> buffer)
    {
        Reads++;
        _readsAt[address] = ReadsAt(address) + 1;

        foreach ((var start, byte[] bytes) in _blocks)
        {
            long at = address - start;

            if (at >= 0 && at + buffer.Length <= bytes.Length)
            {
                bytes.AsSpan((int)at, buffer.Length).CopyTo(buffer);

                return Result.Ok();
            }
        }

        return new UnreadableError(address);
    }

    /// <summary>
    ///     Gets the number of reads that started at <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the 1st byte of a read.</param>
    /// <returns>
    ///     The number of reads.
    /// </returns>
    public int ReadsAt(Address address)
    {
        return _readsAt.GetValueOrDefault(address);
    }

    private Span<byte> BytesAt(Address address, int length)
    {
        foreach ((var start, byte[] bytes) in _blocks)
        {
            long at = address - start;

            if (at >= 0 && at + length <= bytes.Length)
            {
                return bytes.AsSpan((int)at, length);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(address), address, "No block contains this address.");
    }

    /// <summary>
    ///     Represents a read outside the blocks.
    /// </summary>
    /// <param name="Address">The address of the read.</param>
    private sealed record UnreadableError(
        Address Address) : ResultError
    {
        /// <inheritdoc/>
        public override string Message => $"No block contains {Address}.";
    }
}
