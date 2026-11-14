using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class IndirectNameTableTests(
    MemoryLayout memoryLayout,
    Address start,
    NameEntryLayout layout)
{
    private const int EntriesPerChunk = 16384;
    private const int Id = 3;
    private const int PoolLimit = 2_097_152;
    private const string Text = "Core";

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    // Uses the entry offsets of the 4.0 to 4.21 and 4.22 profiles.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new NameEntryLayout(0, 8)),
        new(MemoryLayout.X86, new Address(0x8000_0000), new NameEntryLayout(4, 8)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new NameEntryLayout(0, 16)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new NameEntryLayout(8, 12)),
    ];

    private readonly Address _chunk = start + 0x10_0000;
    private readonly Address _entry = start + 0x30_0000;
    private readonly Address _nextChunk = start + 0x20_0000;
    private readonly Address _nextEntry = start + 0x30_1000;
    private readonly Address _static = start;
    private readonly Address _table = start + 0x1000;

    // An index with the low bit clear reads as ASCII through the static, the chunk table and the chunk.
    [Test]
    public void Read_AsciiEntryInFirstChunk_ReturnsText()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(Text));
            Assert.That(memory.ReadsAt(_static), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_table), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_chunk + (Id * memoryLayout.PointerSize)), Is.EqualTo(1));
        }
    }

    // An index with the low bit set reads as UTF-16, so a char outside ASCII comes back intact.
    [Test]
    public void Read_WideEntry_ReturnsText()
    {
        var memory = MemoryWithEntry((Id << 1) | 1, "Grüße");
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo("Grüße"));
    }

    // Id 16383 is the last slot of chunk 0 and id 16384 is the 1st slot of chunk 1.
    [Test]
    public void Read_LastOfChunkAndFirstOfNext_ReadsBothChunks()
    {
        const int Last = EntriesPerChunk - 1;

        var entries = new Address[EntriesPerChunk];
        entries[Last] = _entry;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk, _nextChunk);
        memory.AddPointers(_chunk, entries);
        memory.AddPointers(_nextChunk, _nextEntry);
        memory.AddNameEntry(_entry, layout, Last << 1, "Last");
        memory.AddNameEntry(_nextEntry, layout, EntriesPerChunk << 1, "First");
        IndirectNameTable table = new(memory, _static, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(Last).Unwrap(), Is.EqualTo("Last"));
            Assert.That(table.Read(EntriesPerChunk).Unwrap(), Is.EqualTo("First"));
            Assert.That(memory.ReadsAt(_table), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_table + memoryLayout.PointerSize), Is.EqualTo(1));
        }
    }

    // The last id of the pool sits in the last slot of chunk 127.
    [Test]
    public void Read_LastIdOfPool_ReadsLastSlotOfLastChunk()
    {
        const int Last = PoolLimit - 1;

        var chunks = new Address[128];
        chunks[127] = _chunk;

        var entries = new Address[EntriesPerChunk];
        entries[EntriesPerChunk - 1] = _entry;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, chunks);
        memory.AddPointers(_chunk, entries);
        memory.AddNameEntry(_entry, layout, Last << 1, Text);

        Assert.That(new IndirectNameTable(memory, _static, layout).Read(Last).Unwrap(), Is.EqualTo(Text));
    }

    // The 1st id past the 2M names of a pool fails without reading memory, because a torn id would index past the chunk table.
    [Test]
    public void Read_IdAtPoolLimit_FailsWithoutRead()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(PoolLimit).UnwrapErr().Message, Is.EqualTo("Name 2097152 is past the 2097152 names a name pool can have."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A negative id fails without reading memory, because it would index chunk -1.
    [Test]
    public void Read_NegativeId_FailsWithoutRead()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(-1).UnwrapErr().Message, Is.EqualTo("Name -1 is below 0."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A chunk pointer of 0 fails with the id and the chunk, even when address 0 has a valid entry pointer.
    [Test]
    public void Read_UnallocatedChunk_FailsWithIdAndChunk()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk, Address.Zero);
        memory.AddPointers(Address.Zero, _entry);
        memory.AddNameEntry(_entry, layout, EntriesPerChunk << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(EntriesPerChunk).UnwrapErr().Message, Is.EqualTo("The pointer of chunk 1 of name 16384 is 0."));
    }

    // A null entry pointer fails with the id, even when address 0 has a valid entry.
    [Test]
    public void Read_NullEntryPointer_FailsWithTheId()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk);
        memory.AddPointers(_chunk, Address.Zero, Address.Zero, Address.Zero, Address.Zero);
        memory.AddNameEntry(Address.Zero, layout, Id << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Is.EqualTo($"The entry pointer of name {Id} is 0."));
    }

    // An entry with the index of another id fails with both ids even though the text is readable.
    [Test]
    public void Read_IndexOfAnotherId_FailsWithBothIds()
    {
        int index = (Id + 1) << 1;
        var memory = MemoryWithEntry(index, Text);
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo(
                $"The entry of name {Id} at {_entry} has index {index}, which is name {Id + 1}. "
                + $"Check NameEntryLayout.Index (0x{layout.Index:X})."));
    }

    // A static outside the blocks fails with the static and the failed read.
    [Test]
    public void Read_StaticUnreadable_FailsWithTheStatic()
    {
        IndirectNameTable table = new(new FakeMemory { Layout = memoryLayout }, _static, layout);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo($"Can't read the pointer to the chunk table of the name pool at {_static}. No block contains {_static}."));
    }

    // A static with the value 0 fails with the static, because the engine fills it at the 1st name.
    [Test]
    public void Read_StaticPointsAtZero_FailsWithTheStatic()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, Address.Zero);
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Is.EqualTo($"The pointer to the chunk table of the name pool at {_static} is 0."));
    }

    // A chunk table that ends before chunk 1 fails with the chunk and the address of its pointer.
    [Test]
    public void Read_ChunkPointerUnreadable_FailsWithChunkAndAddress()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk);
        IndirectNameTable table = new(memory, _static, layout);

        var slot = _table + memoryLayout.PointerSize;

        Assert.That(
            table.Read(EntriesPerChunk).UnwrapErr().Message,
            Is.EqualTo($"Can't read the pointer of chunk 1 of name 16384: reading {slot} failed. No block contains {slot}."));
    }

    // A chunk that ends before the slot of the id fails with the address of the entry pointer.
    [Test]
    public void Read_EntryPointerUnreadable_FailsWithThePointerAddress()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk);
        memory.AddPointers(_chunk, _entry);
        IndirectNameTable table = new(memory, _static, layout);

        var slot = _chunk + (Id * memoryLayout.PointerSize);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo($"Can't read the entry pointer of name {Id}: reading {slot} failed. No block contains {slot}."));
    }

    // An entry pointer outside the blocks fails with the entry, the index offset and the failed read.
    [Test]
    public void Read_EntryUnreadable_FailsWithIndexOffset()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk);
        memory.AddPointers(_chunk, Address.Zero, Address.Zero, Address.Zero, _entry);
        IndirectNameTable table = new(memory, _static, layout);

        var indexAddress = _entry + layout.Index;

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the index of name {Id} from its entry at {_entry}: reading {indexAddress} failed. "
                + $"No block contains {indexAddress}. Check NameEntryLayout.Index (0x{layout.Index:X})."));
    }

    // A readable index with unreadable text behind it fails with the name offset and the failed read.
    [Test]
    public void Read_TextUnreadable_FailsWithNameOffset()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk);
        memory.AddPointers(_chunk, Address.Zero, Address.Zero, Address.Zero, _entry);
        memory.Add(_entry, new byte[layout.Index + sizeof(int)]);
        memory.WriteInt32(_entry + layout.Index, Id << 1);
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Does.Contain($"text of name {Id} from its entry at {_entry}")
                .And.Contain($"reading {_entry + layout.Name} failed.")
                .And.Contain($"NameEntryLayout.Name (0x{layout.Name:X})"));
    }

    // A text of 1024 chars comes back, in bytes for ASCII and in chars for UTF-16.
    [TestCase(0)]
    [TestCase(1)]
    public void Read_TextOf1024_ReturnsText(int wideBit)
    {
        string text = new('x', 1024);
        var memory = MemoryWithEntry((Id << 1) | wideBit, text);
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(text));
    }

    // A text without a terminator in its 1st 1024 chars fails with the limit.
    [TestCase(0)]
    [TestCase(1)]
    public void Read_TextLongerThan1024_FailsWithTheLimit(int wideBit)
    {
        var memory = MemoryWithEntry((Id << 1) | wideBit, new string('x', 1025));
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Does.Contain("no terminator within 1024 characters"));
    }

    // The 2nd read of an id returns the 1st text without reading memory, although the entry has changed since.
    [Test]
    public void Read_SameIdTwice_ReadsMemoryOnce()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        string first = table.Read(Id).Unwrap();
        int reads = memory.Reads;
        memory.WriteInt32(_entry + layout.Index, (Id + 1) << 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(Text));
            Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(Text));
            Assert.That(memory.Reads, Is.EqualTo(reads));
        }
    }

    // A failed read stays out of the cache, so the same id returns the text once the engine has allocated its chunk.
    [Test]
    public void Read_FailedIdThenEntryAdded_ReadsAgain()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, Address.Zero);
        memory.AddPointers(_chunk, Address.Zero, Address.Zero, Address.Zero, _entry);
        memory.AddNameEntry(_entry, layout, Id << 1, Text);
        IndirectNameTable table = new(memory, _static, layout);

        var first = table.Read(Id);
        memory.WritePointer(_table, _chunk);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsErr, Is.True);
            Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(Text));
        }
    }

    // A memory without a pointer size refuses every pointer read, so the read fails with that reason and does not throw.
    [Test]
    public void Read_MemoryWithoutPointerSize_Fails()
    {
        FakeMemory memory = new() { Layout = default };
        IndirectNameTable table = new(memory, _static, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Does.Contain("pointer size of 0 bytes"));
    }

    // A static at the end of the address range wraps around to unreadable memory and does not throw, although the build turns on overflow checks.
    [Test]
    public void Read_StaticAtEndOfAddressRange_Fails()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        IndirectNameTable table = new(memory, new Address(ulong.MaxValue), layout);

        Assert.That(table.Read(Id).IsErr, Is.True);
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new IndirectNameTable(null!, _static, layout),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    private FakeMemory MemoryWithEntry(int index, string text)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_static, _table);
        memory.AddPointers(_table, _chunk);
        memory.AddPointers(_chunk, Address.Zero, Address.Zero, Address.Zero, _entry);
        memory.AddNameEntry(_entry, layout, index, text);

        return memory;
    }
}
