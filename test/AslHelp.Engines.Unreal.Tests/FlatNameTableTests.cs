using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class FlatNameTableTests(
    MemoryLayout memoryLayout,
    Address start,
    NameEntryLayout layout)
{
    private const int Id = 3;
    private const int LargestMatchableId = 0x3FFF_FFFF;
    private const string Text = "Core";

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new NameEntryLayout(0, 12)),
        new(MemoryLayout.X86, new Address(0x8000_0000), new NameEntryLayout(4, 16)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new NameEntryLayout(8, 20)),
    ];

    private readonly Address _array = start;
    private readonly Address _data = start + 0x1000;
    private readonly Address _entry = start + 0x2000;

    // An index with the low bit clear reads as ASCII at the text offset of the layout.
    [Test]
    public void Read_AsciiEntry_ReturnsText()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(Text));
    }

    // An index with the low bit set reads as UTF-16, so a char outside ASCII comes back intact.
    [Test]
    public void Read_WideEntry_ReturnsText()
    {
        var memory = MemoryWithEntry((Id << 1) | 1, "Grüße");
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo("Grüße"));
    }

    // An id equal to the count is out of range, even when the block has a valid entry at that position.
    [Test]
    public void Read_IdAtCount_FailsWithoutRead()
    {
        var memory = MemoryWithEntry(Id << 1, Text, count: Id);
        FlatNameTable table = new(memory, _array, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(Id).UnwrapErr().Message, Is.EqualTo($"Name {Id} is outside the array of {Id} names."));
            Assert.That(memory.ReadsAt(_data + (Id * memoryLayout.PointerSize)), Is.Zero);
        }
    }

    // A negative id fails with no memory read.
    [Test]
    public void Read_NegativeId_FailsWithoutRead()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        FlatNameTable table = new(memory, _array, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Read(-1).UnwrapErr().Message, Is.EqualTo("Name -1 is below 0."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A count of 0 or below fails for id 0 with the count, even when the block has a valid entry at that position.
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Read_CountNotPositive_FailsWithTheCount(int count)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, count, _entry);
        memory.AddNameEntry(_entry, layout, 0, Text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(0).UnwrapErr().Message, Is.EqualTo($"Name 0 is outside the array of {count} names."));
    }

    // A null entry pointer fails with the id, even when address 0 has a valid entry.
    [Test]
    public void Read_NullEntryPointer_FailsWithTheId()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, Id + 1, Address.Zero, Address.Zero, Address.Zero, Address.Zero);
        memory.AddNameEntry(Address.Zero, layout, Id << 1, Text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Is.EqualTo($"The entry pointer of name {Id} is 0."));
    }

    // An entry with the index of another id fails with both ids even though the text is readable.
    [Test]
    public void Read_IndexOfAnotherId_FailsWithBothIds()
    {
        int index = (Id + 1) << 1;
        var memory = MemoryWithEntry(index, Text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo(
                $"The entry of name {Id} at {_entry} has index {index}, which is name {Id + 1}. "
                + $"Check NameEntryLayout.Index (0x{layout.Index:X}) against this engine version."));
    }

    // An index of -1 shifts to -1 and does not match id 0, although -1 / 2 is 0.
    [Test]
    public void Read_IndexOfMinusOneForIdZero_FailsWithTheIndex()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, 1, _entry);
        memory.AddNameEntry(_entry, layout, -1, Text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(0).UnwrapErr().Message, Does.Contain("has index -1, which is name -1."));
    }

    // An entry pointer outside the blocks fails with the entry, the index offset and the failed read.
    [Test]
    public void Read_EntryOutsideReadableMemory_FailsWithEntryAndCause()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, Id + 1, Address.Zero, Address.Zero, Address.Zero, _entry);
        FlatNameTable table = new(memory, _array, layout);

        var indexAddress = _entry + layout.Index;

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the index of name {Id} from its entry at {_entry}: reading {indexAddress} failed. "
                + $"No block contains {indexAddress}. Check NameEntryLayout.Index (0x{layout.Index:X}) against this engine version."));
    }

    // A count past the block of entry pointers fails the read of the entry pointer, so the message has the address of that pointer.
    [Test]
    public void Read_EntryPointerUnreadable_FailsWithThePointerAddress()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, Id + 1, _entry);
        memory.AddNameEntry(_entry, layout, Id << 1, Text);
        FlatNameTable table = new(memory, _array, layout);

        var slot = _data + (Id * memoryLayout.PointerSize);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo($"Can't read the entry pointer of name {Id}: reading {slot} failed. No block contains {slot}."));
    }

    // An array address outside the blocks fails with the array and the failed read.
    [Test]
    public void Read_ArrayOutsideReadableMemory_FailsWithTheArray()
    {
        FlatNameTable table = new(new FakeMemory { Layout = memoryLayout }, _array, layout);

        var countAddress = _array + memoryLayout.PointerSize;

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo($"Can't read the header of the name array at {_array}: reading {countAddress} failed. No block contains {countAddress}."));
    }

    // A readable count beside an unreadable data pointer fails with the array and the read of the data pointer.
    [Test]
    public void Read_DataPointerUnreadable_FailsWithTheArray()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array + memoryLayout.PointerSize, new byte[sizeof(int)]);
        memory.WriteInt32(_array + memoryLayout.PointerSize, Id + 1);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Is.EqualTo($"Can't read the header of the name array at {_array}: reading {_array} failed. No block contains {_array}."));
    }

    // A readable index with unreadable text behind it fails with the name offset and the failed read.
    [Test]
    public void Read_TextUnreadable_FailsWithNameOffset()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, Id + 1, Address.Zero, Address.Zero, Address.Zero, _entry);
        memory.Add(_entry, new byte[layout.Index + sizeof(int)]);
        memory.WriteInt32(_entry + layout.Index, Id << 1);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(
            table.Read(Id).UnwrapErr().Message,
            Does.Contain($"text of name {Id} from its entry at {_entry}")
                .And.Contain($"reading {_entry + layout.Name} failed.")
                .And.Contain($"NameEntryLayout.Name (0x{layout.Name:X})"));
    }

    // An array at the end of the address range wraps around to unreadable memory and does not throw, although the build turns on overflow checks.
    [Test]
    public void Read_ArrayAtEndOfAddressRange_Fails()
    {
        FlatNameTable table = new(new FakeMemory { Layout = memoryLayout }, new Address(ulong.MaxValue), layout);

        Assert.That(table.Read(Id).IsErr, Is.True);
    }

    // A memory with no pointer size refuses every pointer read, so the read fails with that reason and does not throw.
    [Test]
    public void Read_MemoryWithoutPointerSize_FailsWithThePointerSize()
    {
        FakeMemory memory = new() { Layout = default };
        memory.Add(_array, new byte[sizeof(int)]);
        memory.WriteInt32(_array, Id + 1);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Does.Contain("pointer size of 0 bytes"));
    }

    // The offset of the largest id an index can match overflows a 4-byte number, so the read of its entry pointer needs 8-byte math.
    [Test]
    public void Read_LargestMatchableId_ReadsItsEntryPointer()
    {
        var slot = _data + ((long)LargestMatchableId * memoryLayout.PointerSize);
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, int.MaxValue);
        memory.Add(slot, new byte[memoryLayout.PointerSize]);
        memory.WritePointer(slot, _entry);
        memory.AddNameEntry(_entry, layout, LargestMatchableId << 1, Text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(LargestMatchableId).Unwrap(), Is.EqualTo(Text));
    }

    // The 2nd read of an id returns the 1st text with no memory read, although the entry has changed since.
    [Test]
    public void Read_SameIdTwice_ReadsMemoryOnce()
    {
        var memory = MemoryWithEntry(Id << 1, Text);
        FlatNameTable table = new(memory, _array, layout);

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

    // An empty text is a name, so it comes back empty and goes into the cache.
    [Test]
    public void Read_EmptyTextTwice_ReadsMemoryOnce()
    {
        var memory = MemoryWithEntry(Id << 1, string.Empty);
        FlatNameTable table = new(memory, _array, layout);

        string first = table.Read(Id).Unwrap();
        int reads = memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Empty);
            Assert.That(table.Read(Id).Unwrap(), Is.Empty);
            Assert.That(memory.Reads, Is.EqualTo(reads));
        }
    }

    // A failed read stays out of the cache, so the same id returns the text once the entry exists.
    [Test]
    public void Read_FailedIdThenValidEntry_ReadsAgain()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, Id + 1, Address.Zero, Address.Zero, Address.Zero, _entry);
        FlatNameTable table = new(memory, _array, layout);

        var first = table.Read(Id);
        memory.AddNameEntry(_entry, layout, Id << 1, Text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsErr, Is.True);
            Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(Text));
        }
    }

    // An id past the count stays out of the cache, so the same id returns the text once the array has grown.
    [Test]
    public void Read_IdAtCountThenArrayGrows_ReadsAgain()
    {
        var memory = MemoryWithEntry(Id << 1, Text, count: Id);
        FlatNameTable table = new(memory, _array, layout);

        var first = table.Read(Id);
        memory.WriteInt32(_array + memoryLayout.PointerSize, Id + 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsErr, Is.True);
            Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(Text));
        }
    }

    // A text of 256 chars is the longest that comes back.
    [Test]
    public void Read_TextOf256_ReturnsText()
    {
        string text = new('x', 256);
        var memory = MemoryWithEntry(Id << 1, text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(text));
    }

    // A text with no terminator within 256 chars fails with the limit.
    [Test]
    public void Read_TextLongerThan256_FailsWithTheLimit()
    {
        var memory = MemoryWithEntry(Id << 1, new string('x', 257));
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Does.Contain("no terminator within 256 characters"));
    }

    // The limit of a wide text counts chars and not bytes, so a text of 256 chars comes back.
    [Test]
    public void Read_WideTextOf256_ReturnsText()
    {
        string text = new('x', 256);
        var memory = MemoryWithEntry((Id << 1) | 1, text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(text));
    }

    // A wide text with no terminator within 256 chars fails with the limit.
    [Test]
    public void Read_WideTextLongerThan256_FailsWithTheLimit()
    {
        var memory = MemoryWithEntry((Id << 1) | 1, new string('x', 257));
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).UnwrapErr().Message, Does.Contain("no terminator within 256 characters"));
    }

    // A char with a 0 low byte and a surrogate pair do not end a wide text early.
    [TestCase("ĀȀ")]
    [TestCase("a\U0001D518b")]
    public void Read_WideTextWithOddChars_ReturnsText(string text)
    {
        var memory = MemoryWithEntry((Id << 1) | 1, text);
        FlatNameTable table = new(memory, _array, layout);

        Assert.That(table.Read(Id).Unwrap(), Is.EqualTo(text));
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new FlatNameTable(null!, _array, layout),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    // A null layout throws with its parameter name.
    [Test]
    public void Constructor_NullLayout_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new FlatNameTable(new FakeMemory(), _array, null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("layout"));
    }

    private FakeMemory MemoryWithEntry(int index, string text, int count = Id + 1)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_array, _data, count, Address.Zero, Address.Zero, Address.Zero, _entry);
        memory.AddNameEntry(_entry, layout, index, text);

        return memory;
    }
}
