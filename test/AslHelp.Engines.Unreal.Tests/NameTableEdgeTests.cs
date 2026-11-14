using AslHelp.Memory;
using NUnit.Framework;
using System.Buffers.Binary;
using System.Text;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class NameTableEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    int blocksOffset)
{
    private const int EntriesPerChunk = 16384;
    private const int LastEntryId = 0x1FFF_FFFD;
    private const int MaxEntries = 2_097_152;
    private const string Text = "Core";

    private static readonly NameEntryLayout _entryLayout = new(0, 16);

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    // Uses block offset 0x10 of the profile readme on x64. The lock is 4 bytes on x86, so it's 0xC there.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), 0xC),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), 0x10),
    ];

    private static readonly ObjectLayout _objectLayout = new(0x10, 0x18, 0x28);

    private readonly Address _block = start + 0x10_0000;
    private readonly Address _chunk = start + 0x20_0000;
    private readonly Address _entry = start + 0x30_0000;
    private readonly Address _object = start + 0x40_0000;
    private readonly Address _pool = start;
    private readonly Address _table = start + 0x1000;

    private Address TopOfAddressRange => new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);

    // Block pointer 0 sits at the literal offset of the profile readme, id 0 is the 1st byte of that block and the byte cursor ends right after its entry.
    [Test]
    public void Read_ChunkedBlockPointerAtLiteralOffset_ReadsId0()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_pool, new byte[blocksOffset + memoryLayout.PointerSize]);
        memory.WritePointer(_pool + blocksOffset, _block);
        memory.WriteInt32(_pool + memoryLayout.PointerSize + sizeof(int), sizeof(ushort) + Text.Length);
        memory.AddPoolEntry(_block, wide: false, Text);

        Assert.That(new ChunkedNameTable(memory, _pool).Read(0).Unwrap(), Is.EqualTo(Text));
    }

    // Id 0x1FFFFFFD is the last id of the 8192 blocks with room for a 4-char entry, which ends at the last byte of block 8191.
    [Test]
    public void Read_ChunkedLastEntryId_ReadsLastEntryOfLastBlock()
    {
        var blocks = new Address[8192];
        blocks[8191] = _block;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, blocks);
        memory.AddPoolEntry(_block + 0x1FFFA, wide: false, Text);

        Assert.That(new ChunkedNameTable(memory, _pool).Read(LastEntryId).Unwrap(), Is.EqualTo(Text));
    }

    // Bits 1 to 5 of the header are the lowercase probe hash of the engine source, so they don't change the length or the encoding.
    [TestCase(false)]
    [TestCase(true)]
    public void Read_ChunkedHeaderWithProbeHashBits_ReadsTextOfItsLength(bool wide)
    {
        byte[] text = (wide ? Encoding.Unicode : Encoding.ASCII).GetBytes(Text);
        byte[] bytes = new byte[sizeof(ushort) + text.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)((Text.Length << 6) | 0b11_1110 | (wide ? 1 : 0)));
        text.CopyTo(bytes, sizeof(ushort));

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block);
        memory.Add(_block, bytes);

        Assert.That(new ChunkedNameTable(memory, _pool).Read(0).Unwrap(), Is.EqualTo(Text));
    }

    // A real text doesn't contain a 0 char, so a 0 char inside the length comes from a torn id and the cut text isn't the name.
    [TestCase(false, 0)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 2)]
    public void Read_ChunkedLengthPastZeroChar_FailsWithBothLengths(bool wide, int characters)
    {
        string text = characters == 0 ? "\0\0\0\0" : "Co\0\0";

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block);
        memory.AddPoolEntry(_block, wide, text);

        Assert.That(
            new ChunkedNameTable(memory, _pool).Read(0).UnwrapErr().Message,
            Is.EqualTo($"The header of name 0 at {_block} has length 4, but its text ends after {characters} characters. Check the name id."));
    }

    // A cut text stays out of the cache, so the same id returns the text once the pool has written all of it.
    [Test]
    public void Read_ChunkedCutTextThenWritten_ReadsAgain()
    {
        byte[] bytes = new byte[sizeof(ushort) + Text.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(Text.Length << 6));

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block);
        memory.Add(_block, bytes);
        ChunkedNameTable table = new(memory, _pool);

        var first = table.Read(0);
        Encoding.ASCII.GetBytes(Text).CopyTo(bytes, sizeof(ushort));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsErr, Is.True);
            Assert.That(table.Read(0).Unwrap(), Is.EqualTo(Text));
        }
    }

    // A length of 0 fails with the wide bit or the probe hash bits set, because only the length decides if an entry is filled.
    [TestCase(0b00_0001)]
    [TestCase(0b11_1110)]
    [TestCase(0b11_1111)]
    public void Read_ChunkedLengthZeroWithOtherHeaderBits_FailsWithTheEntry(int header)
    {
        byte[] bytes = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)header);

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block);
        memory.Add(_block, bytes);

        Assert.That(
            new ChunkedNameTable(memory, _pool).Read(0).UnwrapErr().Message,
            Is.EqualTo($"The header of name 0 at {_block} has length 0. Check the name id."));
    }

    // The header counts UTF-16 units, so a char outside the basic plane takes 2 of the 4 units and comes back whole.
    [Test]
    public void Read_ChunkedSurrogatePair_ReturnsWholeText()
    {
        const string Wide = "名前𝒳";

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block);
        memory.AddPoolEntry(_block, wide: true, Wide);

        Assert.That(new ChunkedNameTable(memory, _pool).Read(0).Unwrap(), Is.EqualTo(Wide));
    }

    // A block pointer at the top of the address range moves past the end, so the read fails with the moved entry and doesn't throw.
    [Test]
    public void Read_ChunkedBlockAtTopOfAddressRange_FailsWithTheEntry()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, TopOfAddressRange);

        var entry = TopOfAddressRange + 0x20;

        Assert.That(
            new ChunkedNameTable(memory, _pool).Read(0x10).UnwrapErr().Message,
            Is.EqualTo($"Can't read the header of name 16 from its entry at {entry}. No block contains {entry}."));
    }

    // The largest id is block 32767, far past the block pointers, so it fails without a read.
    [Test]
    public void Read_ChunkedLargestId_FailsWithoutRead()
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                new ChunkedNameTable(memory, _pool).Read(int.MaxValue).UnwrapErr().Message,
                Is.EqualTo("Name 2147483647 is in block 32767, past the 8192 blocks of a name pool."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // The largest id is past the 2M entries, so the chunk math doesn't run and it fails without a read.
    [Test]
    public void Read_IndirectLargestId_FailsWithoutRead()
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                new IndirectNameTable(memory, _pool, _entryLayout).Read(int.MaxValue).UnwrapErr().Message,
                Is.EqualTo($"Name {int.MaxValue} is past the {MaxEntries} names a name pool can have."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // The smallest id fails without a read in every shape.
    [Test]
    public void Read_SmallestId_FailsWithoutReadInEveryShape()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        INameTable[] tables =
        [
            new FlatNameTable(memory, _pool, _entryLayout),
            new IndirectNameTable(memory, _pool, _entryLayout),
            new ChunkedNameTable(memory, _pool),
        ];

        using (Assert.EnterMultipleScope())
        {
            foreach (var table in tables)
            {
                Assert.That(table.Read(int.MinValue).UnwrapErr().Message, Is.EqualTo($"Name {int.MinValue} is below 0."));
            }

            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // Id 49159 is chunk 3 and slot 7, so a chunk or slot from the wrong math reads a pointer the test doesn't fill.
    [Test]
    public void Read_IndirectIdInLaterChunk_ReadsItsChunkAndSlot()
    {
        const int Id = (3 * EntriesPerChunk) + 7;

        var entries = new Address[8];
        entries[7] = _entry;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_pool, _table);
        memory.AddPointers(_table, Address.Zero, Address.Zero, Address.Zero, _chunk);
        memory.AddPointers(_chunk, entries);
        memory.AddNameEntry(_entry, _entryLayout, Id << 1, Text);

        Assert.That(new IndirectNameTable(memory, _pool, _entryLayout).Read(Id).Unwrap(), Is.EqualTo(Text));
    }

    // Index 1 is id 0 with the wide bit, because only the bits above the low bit are the id.
    [Test]
    public void Read_IndirectId0Wide_ReturnsText()
    {
        var memory = MemoryWithIndirectEntry(index: 1, "Grüße");

        Assert.That(new IndirectNameTable(memory, _pool, _entryLayout).Read(0).Unwrap(), Is.EqualTo("Grüße"));
    }

    // An indirect entry ends at its terminator, so a char outside the basic plane comes back whole.
    [Test]
    public void Read_IndirectSurrogatePair_ReturnsWholeText()
    {
        const string Wide = "名前𝒳";

        var memory = MemoryWithIndirectEntry(index: 1, Wide);

        Assert.That(new IndirectNameTable(memory, _pool, _entryLayout).Read(0).Unwrap(), Is.EqualTo(Wide));
    }

    // A negative index stays negative after the shift, so it can't match any id and fails with both values.
    [Test]
    public void Read_IndirectNegativeIndex_FailsWithTheIndex()
    {
        var memory = MemoryWithIndirectEntry(int.MinValue, Text);

        Assert.That(
            new IndirectNameTable(memory, _pool, _entryLayout).Read(0).UnwrapErr().Message,
            Is.EqualTo(
                $"The entry of name 0 at {_entry} has index {int.MinValue}, which is name {int.MinValue >> 1}. "
                + "Check NameEntryLayout.Index (0x0)."));
    }

    // A chunk table at the top of the address range moves past the end for chunk 1, so the read fails with that address and doesn't throw.
    [Test]
    public void Read_IndirectTableAtTopOfAddressRange_FailsWithTheAddress()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_pool, TopOfAddressRange);

        var slot = TopOfAddressRange + memoryLayout.PointerSize;

        Assert.That(
            new IndirectNameTable(memory, _pool, _entryLayout).Read(EntriesPerChunk).UnwrapErr().Message,
            Is.EqualTo($"Can't read the pointer of chunk 1 of name {EntriesPerChunk}: reading {slot} failed. No block contains {slot}."));
    }

    // A number on a chunked id adds the suffix to the text of the pool, the same as a flat id.
    [Test]
    public void NameOf_ChunkedIdWithNumber_ReturnsFullName()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, _block);
        memory.AddPoolEntry(_block + 0x20, wide: false, Text);
        memory.AddObject(_object, _objectLayout, 0x0001_0010, number: 5);

        ObjectReader reader = new(memory, new ChunkedNameTable(memory, _pool), _objectLayout);

        Assert.That(reader.NameOf(_object).Unwrap(), Is.EqualTo(Text + "_4"));
    }

    // A number on an indirect id adds the suffix to the text of the entry, the same as a flat id.
    [Test]
    public void NameOf_IndirectIdWithNumber_ReturnsFullName()
    {
        var memory = MemoryWithIndirectEntry(index: 0, Text);
        memory.AddObject(_object, _objectLayout, 0, number: 5);

        ObjectReader reader = new(memory, new IndirectNameTable(memory, _pool, _entryLayout), _objectLayout);

        Assert.That(reader.NameOf(_object).Unwrap(), Is.EqualTo(Text + "_4"));
    }

    // An id with the top bit set goes through the table as is, so it fails as negative and keeps the object and the layout field.
    [Test]
    public void NameOf_IdWithTopBitSet_FailsAsNegative()
    {
        const int Id = int.MinValue + 0x10;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddObject(_object, _objectLayout, Id);

        ObjectReader reader = new(memory, new ChunkedNameTable(memory, _pool), _objectLayout);

        Assert.That(
            reader.NameOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_object} with the name id {Id}. Name {Id} is below 0. "
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X})."));
    }

    // A block the pool hasn't allocated fails with the id, the block, the object and the layout field.
    [Test]
    public void NameOf_ChunkedUnallocatedBlock_KeepsEveryLevel()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, Address.Zero);
        memory.AddObject(_object, _objectLayout, 0x0001_0010);

        ObjectReader reader = new(memory, new ChunkedNameTable(memory, _pool), _objectLayout);

        Assert.That(
            reader.NameOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_object} with the name id 65552. The pointer of block 1 of name 65552 is 0. "
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X})."));
    }

    // An id into the 0 bytes past the cursor of a block fails with the object, so the object doesn't come back with an empty name.
    [Test]
    public void NameOf_ChunkedIdOfUnfilledEntry_KeepsEveryLevel()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, _block);
        memory.Add(_block, new byte[0x100]);
        memory.AddObject(_object, _objectLayout, 0x0001_0010);

        ObjectReader reader = new(memory, new ChunkedNameTable(memory, _pool), _objectLayout);

        Assert.That(
            reader.NameOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_object} with the name id 65552. "
                + $"The header of name 65552 at {_block + 0x20} has length 0. Check the name id. "
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X})."));
    }

    private FakeMemory MemoryWithIndirectEntry(int index, string text)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_pool, _table);
        memory.AddPointers(_table, _chunk);
        memory.AddPointers(_chunk, _entry);
        memory.AddNameEntry(_entry, _entryLayout, index, text);

        return memory;
    }
}
