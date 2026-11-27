using AslHelp.Engines.Unreal.Names;
using AslHelp.Memory;
using NUnit.Framework;
using System.Buffers.Binary;
using System.Text;

namespace AslHelp.Engines.Unreal.Tests.Names;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ChunkedNameTableTests(
    MemoryLayout memoryLayout,
    Address start)
{
    private const int KnownId = 0x0003_0010;
    private const int KnownOffset = 0x20;
    private const string Text = "Core";

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private readonly Address _block = start + 0x10_0000;
    private readonly Address _nextBlock = start + 0x20_0000;
    private readonly Address _pool = start;

    // Skips the lock of 1 pointer and the index of the current block to the byte cursor of that block.
    private Address Cursor => _pool + memoryLayout.PointerSize + sizeof(int);

    // Id 0x30010 is block 3 and 2-byte offset 0x10, so its header sits 0x20 bytes into block 3 with the text 2 bytes behind it.
    [Test]
    public void Read_AsciiEntry_ReturnsText()
    {
        var memory = MemoryWithKnownEntry(wide: false, Text);
        ChunkedNameTable table = new(memory, _pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo(Text));
            Assert.That(memory.ReadsAt(BlockPointer(3)), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_block + KnownOffset), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_block + KnownOffset + sizeof(ushort)), Is.EqualTo(1));
        }
    }

    // A header with the low bit set reads the length as UTF-16 chars, so a char outside ASCII comes back intact.
    [Test]
    public void Read_WideEntry_ReturnsText()
    {
        var memory = MemoryWithKnownEntry(wide: true, "Grüße");
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo("Grüße"));
    }

    // Offset 0xFFFD is a 4-char entry at byte 0x1FFFA that ends at the last byte of block 0 and id 0x10000 is the 1st entry of block 1.
    [Test]
    public void Read_LastEntryOfBlockAndFirstOfNext_ReadsBoth()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block, _nextBlock);
        memory.AddPoolEntry(_block + 0x1FFFA, wide: false, "Last");
        memory.AddPoolEntry(_nextBlock, wide: false, "First");
        ChunkedNameTable table = new(memory, _pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(0xFFFD).Unwrap(), Is.EqualTo("Last"));
            Assert.That(table.Read(0x1_0000).Unwrap(), Is.EqualTo("First"));
            Assert.That(memory.ReadsAt(BlockPointer(0)), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(BlockPointer(1)), Is.EqualTo(1));
        }
    }

    // Block 8191 is the last block of a pool.
    [Test]
    public void Read_LastBlock_ReadsItsEntry()
    {
        var blocks = new Address[8192];
        blocks[8191] = _block;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, blocks);
        memory.AddPoolEntry(_block, wide: false, Text);

        Assert.That(new ChunkedNameTable(memory, _pool).Read(8191 << 16).Unwrap(), Is.EqualTo(Text));
    }

    // The 1st block past the 8192 of a pool fails without reading memory, because a torn id would index past the block pointers.
    [Test]
    public void Read_Block8192_FailsWithoutRead()
    {
        const int Id = 8192 << 16;

        FakeMemory memory = new() { Layout = memoryLayout };
        ChunkedNameTable table = new(memory, _pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(Id).UnwrapErr().Message, Is.EqualTo($"Name {Id} is in block 8192, past the 8192 blocks of a name pool."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A negative id fails without reading memory, because its high bits would read as block 0xFFFF.
    [Test]
    public void Read_NegativeId_FailsWithoutRead()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        ChunkedNameTable table = new(memory, _pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(-1).UnwrapErr().Message, Is.EqualTo("Name -1 is below 0."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A block pointer of 0 fails with the id and the block, even when address 0 has a valid entry.
    [Test]
    public void Read_UnallocatedBlock_FailsWithIdAndBlock()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, Address.Zero, Address.Zero, Address.Zero);
        memory.AddPoolEntry(Address.Zero + KnownOffset, wide: false, Text);
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(table.Read(KnownId).UnwrapErr().Message, Is.EqualTo($"The pointer of block 3 of name {KnownId} is 0."));
    }

    // A pool the memory doesn't contain fails with the block and the address of its pointer.
    [Test]
    public void Read_PoolUnreadable_FailsWithBlockPointerAddress()
    {
        ChunkedNameTable table = new(new FakeMemory { Layout = memoryLayout }, _pool);

        var address = BlockPointer(3);

        Assert.That(
            table.Read(KnownId).UnwrapErr().Message,
            Is.EqualTo($"Can't read the pointer of block 3 of name {KnownId}: reading {address} failed. No block contains {address}."));
    }

    // A block pointer into nothing fails with the entry.
    [Test]
    public void Read_HeaderUnreadable_FailsWithEntry()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, Address.Zero, Address.Zero, _block);
        ChunkedNameTable table = new(memory, _pool);

        var entry = _block + KnownOffset;

        Assert.That(
            table.Read(KnownId).UnwrapErr().Message,
            Is.EqualTo($"Can't read the header of name {KnownId} from its entry at {entry}. No block contains {entry}."));
    }

    // A torn header with the largest length reads past the block, so the read fails with the length and does not cut the text.
    [Test]
    public void Read_TornHeaderLengthPastBlock_FailsWithTheLength()
    {
        var entry = _block + KnownOffset;
        var characters = entry + sizeof(ushort);

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, Address.Zero, Address.Zero, _block);
        memory.Add(entry, [0xC0, 0xFF, (byte)'C', (byte)'o', (byte)'r', (byte)'e']);
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(
            table.Read(KnownId).UnwrapErr().Message,
            Is.EqualTo($"Can't read the 1023 characters of name {KnownId} from its entry at {entry}. No block contains {characters}."));
    }

    // A length of 0 fails with the id and the entry, because the pool doesn't store an empty name.
    [Test]
    public void Read_LengthZero_FailsWithIdAndEntry()
    {
        var memory = MemoryWithKnownEntry(wide: false, string.Empty);
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(
            table.Read(KnownId).UnwrapErr().Message,
            Is.EqualTo($"The header of name {KnownId} at {_block + KnownOffset} has length 0. Check the name id."));
    }

    // A header of 0 can be a position the pool hasn't filled yet, so the same id returns the text once the pool has written it.
    [Test]
    public void Read_LengthZeroThenEntryWritten_ReadsAgain()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        byte[] bytes = new byte[sizeof(ushort) + Text.Length];
        memory.AddNamePool(_pool, Address.Zero, Address.Zero, Address.Zero, _block);
        memory.Add(_block + KnownOffset, bytes);
        ChunkedNameTable table = new(memory, _pool);

        var first = table.Read(KnownId);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(Text.Length << 6));
        Encoding.ASCII.GetBytes(Text).CopyTo(bytes, sizeof(ushort));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsErr, Is.True);
            Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo(Text));
        }
    }

    // The 10 bits of the length reach 1023, which comes back whole in bytes for ASCII and in chars for UTF-16.
    [TestCase(false)]
    [TestCase(true)]
    public void Read_TextOf1023_ReturnsText(bool wide)
    {
        string text = new('x', 1023);
        var memory = MemoryWithKnownEntry(wide, text);
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo(text));
    }

    // Each char has a 0 low byte and the block ends right after the text, so a read up to a terminator would fail or cut the text.
    [Test]
    public void Read_TextWithEmbeddedZeroChar_ReturnsFullLength()
    {
        var memory = MemoryWithKnownEntry(wide: true, "ĀȀ");
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo("ĀȀ"));
    }

    // The 2nd read of an id returns the 1st text without reading memory, although the block pointer has changed since.
    [Test]
    public void Read_SameIdTwice_ReadsMemoryOnce()
    {
        var memory = MemoryWithKnownEntry(wide: false, Text);
        ChunkedNameTable table = new(memory, _pool);

        string first = table.Read(KnownId).Unwrap();
        int reads = memory.Reads;
        memory.WritePointer(BlockPointer(3), _nextBlock);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(Text));
            Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo(Text));
            Assert.That(memory.Reads, Is.EqualTo(reads));
        }
    }

    // A failed read stays out of the cache, so the same id returns the text once the pool has allocated its block.
    [Test]
    public void Read_FailedIdThenBlockAdded_ReadsAgain()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, Address.Zero, Address.Zero, Address.Zero);
        memory.AddPoolEntry(_block + KnownOffset, wide: false, Text);
        ChunkedNameTable table = new(memory, _pool);

        var first = table.Read(KnownId);
        memory.WritePointer(BlockPointer(3), _block);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsErr, Is.True);
            Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo(Text));
        }
    }

    // Id 0x30011 points 2 bytes into the entry of Core, so the header reads "Co" as a UTF-16 length of 445 that ends past the used bytes.
    // The failure stays out of the cache, so the 2nd read checks the header again.
    [Test]
    public void Read_IdInsideAnotherEntry_FailsWithTheEndAndStaysOutOfCache()
    {
        const int Id = KnownId + 1;

        var memory = MemoryWithKnownEntry(wide: false, Text);
        memory.WriteInt32(Cursor, KnownOffset + sizeof(ushort) + Text.Length);
        ChunkedNameTable table = new(memory, _pool);

        var entry = _block + KnownOffset + sizeof(ushort);
        var first = table.Read(Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.UnwrapErr().Message,
                Is.EqualTo($"The entry of name {Id} at {entry} ends at byte 926, past the 38 used bytes of block 3. Check the name id."));
            Assert.That(table.Read(Id).IsErr, Is.True);
            Assert.That(memory.ReadsAt(entry), Is.EqualTo(2));
        }
    }

    // A header past the byte cursor of the current block fails without a read of the header, and the same id reads once the cursor has moved past it.
    [Test]
    public void Read_HeaderPastCursorThenCursorMoved_ReadsAgain()
    {
        var memory = MemoryWithKnownEntry(wide: false, Text);
        memory.WriteInt32(Cursor, KnownOffset);
        ChunkedNameTable table = new(memory, _pool);

        var entry = _block + KnownOffset;
        var first = table.Read(KnownId);
        int headerReads = memory.ReadsAt(entry);
        memory.WriteInt32(Cursor, KnownOffset + sizeof(ushort) + Text.Length);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.UnwrapErr().Message,
                Is.EqualTo($"The entry of name {KnownId} at {entry} ends at byte 34, past the 32 used bytes of block 3. Check the name id."));
            Assert.That(headerReads, Is.Zero);
            Assert.That(table.Read(KnownId).Unwrap(), Is.EqualTo(Text));
        }
    }

    // A block past the current block of the pool fails, because the engine fills the blocks in order.
    [Test]
    public void Read_BlockPastCurrentBlock_FailsWithBothBlocks()
    {
        var memory = MemoryWithKnownEntry(wide: false, Text);
        memory.WriteInt32(_pool + memoryLayout.PointerSize, 2);
        ChunkedNameTable table = new(memory, _pool);

        Assert.That(
            table.Read(KnownId).UnwrapErr().Message,
            Is.EqualTo($"Name {KnownId} is in block 3, past the current block 2 of the name pool. Check the name id."));
    }

    // Cursors the memory doesn't contain fail with their address.
    [Test]
    public void Read_CursorsUnreadable_FailsWithCursorsAddress()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(BlockPointer(0), new byte[4 * memoryLayout.PointerSize]);
        memory.WritePointer(BlockPointer(3), _block);
        memory.AddPoolEntry(_block + KnownOffset, wide: false, Text);
        ChunkedNameTable table = new(memory, _pool);

        var address = _pool + memoryLayout.PointerSize;

        Assert.That(
            table.Read(KnownId).UnwrapErr().Message,
            Is.EqualTo($"Can't read the cursors of the name pool for name {KnownId}: reading {address} failed. No block contains {address}."));
    }

    // A memory without a pointer size refuses every pointer read, so the read fails with that reason and does not throw.
    [Test]
    public void Read_MemoryWithoutPointerSize_Fails()
    {
        ChunkedNameTable table = new(new FakeMemory { Layout = default }, _pool);

        Assert.That(table.Read(KnownId).UnwrapErr().Message, Does.Contain("pointer size of 0 bytes"));
    }

    // A pool at the end of the address range wraps around to unreadable memory and does not throw, although the build turns on overflow checks.
    [Test]
    public void Read_PoolAtEndOfAddressRange_Fails()
    {
        ChunkedNameTable table = new(new FakeMemory { Layout = memoryLayout }, new Address(ulong.MaxValue));

        Assert.That(table.Read(KnownId).IsErr, Is.True);
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(() => new ChunkedNameTable(null!, _pool), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    // Skips the lock of 1 pointer and the 2 4-byte cursors in front of the block pointers.
    private Address BlockPointer(int block)
    {
        return _pool + memoryLayout.PointerSize + (2 * sizeof(int)) + (block * memoryLayout.PointerSize);
    }

    private FakeMemory MemoryWithKnownEntry(bool wide, string text)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, Address.Zero, Address.Zero, _block);
        memory.AddPoolEntry(_block + KnownOffset, wide, text);

        return memory;
    }
}
