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
    private const int PoolBlockSize = 0x2_0000;

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
    ///     Adds a property that isn't an object in a block as large as an object.
    /// </summary>
    /// <param name="field">The address of the property.</param>
    /// <param name="layout">The offsets inside the property.</param>
    /// <param name="nameId">The name id.</param>
    /// <param name="klass">The field class.</param>
    /// <param name="next">The next property.</param>
    public void AddField(Address field, FieldPropertyLayout layout, int nameId, Address klass = default, Address next = default)
    {
        Add(field, new byte[ObjectSize]);
        WriteInt32(field + layout.Name, nameId);
        WritePointer(field + layout.Class, klass);
        WritePointer(field + layout.Next, next);
    }

    /// <summary>
    ///     Adds a field class in a block as large as an object.
    /// </summary>
    /// <param name="klass">The address of the field class.</param>
    /// <param name="layout">The offsets inside the field class.</param>
    /// <param name="nameId">The name id.</param>
    public void AddFieldClass(Address klass, FieldClassLayout layout, int nameId)
    {
        Add(klass, new byte[ObjectSize]);
        WriteInt32(klass + layout.Name, nameId);
    }

    /// <summary>
    ///     Adds a block of pointers, such as a chunk table or a chunk of entry pointers.
    /// </summary>
    /// <param name="start">The address of the 1st pointer.</param>
    /// <param name="pointers">The pointers.</param>
    public void AddPointers(Address start, params Address[] pointers)
    {
        Add(start, new byte[pointers.Length * Layout.PointerSize]);

        for (int i = 0; i < pointers.Length; i++)
        {
            WritePointer(start + (i * Layout.PointerSize), pointers[i]);
        }
    }

    /// <summary>
    ///     Adds a name pool with its block pointers behind a lock of 1 pointer and 2 4-byte cursors.
    /// </summary>
    /// <remarks>
    ///     Sets the current block to the last block and its byte cursor to the end of a 128 KB block, so every byte of every block counts as used.
    /// </remarks>
    /// <param name="pool">The address of the pool.</param>
    /// <param name="blocks">The block pointers.</param>
    public void AddNamePool(Address pool, params Address[] blocks)
    {
        int cursors = Layout.PointerSize;
        int slots = cursors + (2 * sizeof(int));

        Add(pool, new byte[slots + (blocks.Length * Layout.PointerSize)]);
        WriteInt32(pool + cursors, blocks.Length - 1);
        WriteInt32(pool + cursors + sizeof(int), PoolBlockSize);

        for (int i = 0; i < blocks.Length; i++)
        {
            WritePointer(pool + slots + (i * Layout.PointerSize), blocks[i]);
        }
    }

    /// <summary>
    ///     Adds an entry of a name pool whose block ends right after the text, without a terminator.
    /// </summary>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="wide"><see langword="true"/> to write the text as UTF-16 and set the low bit of the header.</param>
    /// <param name="text">The text.</param>
    public void AddPoolEntry(Address entry, bool wide, string text)
    {
        byte[] characters = (wide ? Encoding.Unicode : Encoding.ASCII).GetBytes(text);
        byte[] bytes = new byte[sizeof(ushort) + characters.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)((text.Length << 6) | (wide ? 1 : 0)));
        characters.CopyTo(bytes, sizeof(ushort));

        Add(entry, bytes);
    }

    /// <summary>
    ///     Adds an object array with its count, its chunk count and its chunk pointers in 1 block.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="count">The count of objects.</param>
    /// <param name="chunks">The chunk pointers, which also set the chunk count.</param>
    public void AddIndirectObjectArray(Address array, IndirectObjectArrayLayout layout, int count, params Address[] chunks)
    {
        Add(array, new byte[layout.NumChunks + sizeof(int)]);
        WriteInt32(array + layout.NumElements, count);
        WriteInt32(array + layout.NumChunks, chunks.Length);

        for (int i = 0; i < chunks.Length; i++)
        {
            WritePointer(array + layout.Objects + (i * Layout.PointerSize), chunks[i]);
        }
    }

    /// <summary>
    ///     Adds an object array with a pointer to its items and its count.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="data">The address of the items.</param>
    /// <param name="count">The count of items.</param>
    public void AddItemObjectArray(Address array, ItemObjectArrayLayout layout, Address data, int count)
    {
        Add(array, new byte[layout.NumElements + sizeof(int)]);
        WritePointer(array + layout.Objects, data);
        WriteInt32(array + layout.NumElements, count);
    }

    /// <summary>
    ///     Adds an object array and its block of chunk pointers.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="layout">The offsets inside the array.</param>
    /// <param name="table">The address of the chunk pointers.</param>
    /// <param name="count">The count of items.</param>
    /// <param name="chunks">The chunk pointers, which also set the chunk count.</param>
    public void AddChunkedObjectArray(Address array, ChunkedObjectArrayLayout layout, Address table, int count, params Address[] chunks)
    {
        Add(array, new byte[layout.NumChunks + sizeof(int)]);
        WritePointer(array + layout.Objects, table);
        WriteInt32(array + layout.NumElements, count);
        WriteInt32(array + layout.NumChunks, chunks.Length);
        AddPointers(table, chunks);
    }

    /// <summary>
    ///     Adds a block of items, with every byte beside the object pointers set, so a read at the wrong offset returns another address.
    /// </summary>
    /// <param name="start">The address of the 1st item.</param>
    /// <param name="item">The offsets inside an item.</param>
    /// <param name="objects">The object pointer of each item.</param>
    public void AddObjectItems(Address start, ObjectItemLayout item, params Address[] objects)
    {
        byte[] bytes = new byte[objects.Length * item.Size];

        bytes.AsSpan().Fill(0xAB);
        Add(start, bytes);

        for (int i = 0; i < objects.Length; i++)
        {
            WritePointer(start + ((long)i * item.Size) + item.ObjectPointer, objects[i]);
        }
    }

    /// <summary>
    ///     Writes an instruction that references a global, as a <c>read global</c> step of a locator reads it.
    /// </summary>
    /// <remarks>
    ///     Writes the operand relative to the end of the instruction for 8-byte pointers and as the absolute address for 4-byte pointers.
    /// </remarks>
    /// <param name="instruction">The address of the 1st byte of the instruction.</param>
    /// <param name="opcode">The bytes in front of the 4-byte operand.</param>
    /// <param name="global">The address of the global.</param>
    /// <exception cref="ArgumentOutOfRangeException">No block contains the instruction.</exception>
    public void WriteGlobalReference(Address instruction, byte[] opcode, Address global)
    {
        opcode.CopyTo(BytesAt(instruction, opcode.Length));

        var operand = instruction + opcode.Length;

        WriteInt32(
            operand,
            Layout.PointerSize == sizeof(ulong) ? checked((int)(global - (operand + sizeof(int)))) : unchecked((int)global.Value));
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
        // Write the low bytes of 8, so a pointer size other than 4 or 8 gets a pointer too.
        byte[] full = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(full, value.Value);
        full.AsSpan(0, Layout.PointerSize).CopyTo(BytesAt(address, Layout.PointerSize));
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
        return _readsAt.TryGetValue(address, out int reads) ? reads : 0;
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
}

/// <summary>
///     Represents a read outside the blocks.
/// </summary>
/// <param name="Address">The address of the read.</param>
file sealed record UnreadableError(
    Address Address) : ResultError
{
    /// <inheritdoc/>
    public override string Message => $"No block contains {Address}.";
}
