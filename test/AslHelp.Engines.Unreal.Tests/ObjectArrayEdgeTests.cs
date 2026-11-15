using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ObjectArrayEdgeTests(
    MemoryLayout memoryLayout,
    Address start)
{
    private const int MaxObjects = 64 * 1024 * 1024;
    private const int PerChunk = 4;
    private const int PointersPerChunk = 16384;
    private const int TableLength = 512;

    // Puts Objects at 0x10, which no profile has, so a read that skips the offset starts 16 bytes too early.
    private static readonly ChunkedObjectArrayLayout _chunkedLayout = new(0x10, 0x18, 0x1C, PerChunk);

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private static readonly ItemObjectArrayLayout _itemLayout = new(0x10, 0x18);

    private readonly Address _array = start;
    private readonly Address _chunk = start + 0x10_0000;
    private readonly Address _lastChunk = start + 0x30_0000;
    private readonly Address _nextChunk = start + 0x20_0000;
    private readonly Address _object = start + 0x100_0000;
    private readonly Address _table = start + 0x8000;

    private IndirectObjectArrayLayout IndirectLayout
        => new(0x10, 0x10 + (TableLength * memoryLayout.PointerSize), 0x14 + (TableLength * memoryLayout.PointerSize));

    private ObjectItemLayout Item => new(memoryLayout.PointerSize, 3 * memoryLayout.PointerSize);

    private Address TopOfAddressRange => new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);

    // A count outside 0 to 64M comes from a wrong offset or a torn read, so every shape fails even when the caller wants 3 pointers.
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    [TestCase(MaxObjects + 1)]
    [TestCase(int.MaxValue)]
    public void Read_CountOutsideRange_YieldsOneFailureInEveryShape(int count)
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        using (Assert.EnterMultipleScope())
        {
            foreach ((var array, var address, var reader) in EveryShape(memory))
            {
                memory.Add(address, new byte[sizeof(int)]);
                memory.WriteInt32(address, count);

                string expected =
                    $"The object array at {array} has {count} objects, which is outside 0 to {MaxObjects}. "
                    + "Check the address of the array and the offset of its count.";

                var items = reader.Read(3).ToList();

                Assert.That(reader.ReadCount().UnwrapErr().Message, Is.EqualTo(expected), reader.GetType().Name);
                Assert.That(items, Has.Count.EqualTo(1), reader.GetType().Name);
                Assert.That(items[0].UnwrapErr().Message, Is.EqualTo(expected), reader.GetType().Name);
            }
        }
    }

    // 64M objects is the largest count that passes, because a real array stays far below it.
    [Test]
    public void ReadCount_LargestCount_ReturnsItInEveryShape()
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        using (Assert.EnterMultipleScope())
        {
            foreach ((_, var address, var reader) in EveryShape(memory))
            {
                memory.Add(address, new byte[sizeof(int)]);
                memory.WriteInt32(address, MaxObjects);

                Assert.That(reader.ReadCount().Unwrap(), Is.EqualTo(MaxObjects), reader.GetType().Name);
            }
        }
    }

    // A count of 0 ends after the read of the count, so the rest of the header doesn't need to be readable.
    [Test]
    public void Read_EmptyArray_ReadsOnlyTheCountInEveryShape()
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        using (Assert.EnterMultipleScope())
        {
            foreach ((_, var address, var reader) in EveryShape(memory))
            {
                memory.Add(address, new byte[sizeof(int)]);

                int before = memory.Reads;
                var items = reader.Read(3).ToList();

                Assert.That(items, Is.Empty, reader.GetType().Name);
                Assert.That(memory.Reads - before, Is.EqualTo(1), reader.GetType().Name);
            }
        }
    }

    // A memory without a pointer size refuses every pointer read, so each shape fails with that reason and doesn't throw.
    [Test]
    public void Read_MemoryWithoutPointerSize_YieldsOneFailureInEveryShape()
    {
        FakeMemory memory = new() { Layout = default };
        memory.Add(_array, new byte[0x2000]);
        memory.WriteInt32(_array, 1);
        memory.WriteInt32(_array + IndirectLayout.NumElements, 1);
        memory.WriteInt32(_array + IndirectLayout.NumChunks, 1);
        memory.WriteInt32(_array + _itemLayout.NumElements, 1);
        memory.WriteInt32(_array + _chunkedLayout.NumChunks, 1);

        IObjectArray[] arrays =
        [
            new PointerObjectArray(memory, _array),
            new IndirectObjectArray(memory, _array, IndirectLayout),
            new ItemObjectArray(memory, _array, _itemLayout, Item),
            new ChunkedObjectArray(memory, _array, _chunkedLayout, Item),
        ];

        using (Assert.EnterMultipleScope())
        {
            foreach (var array in arrays)
            {
                var items = array.Read(1).ToList();

                Assert.That(items, Has.Count.EqualTo(1), array.GetType().Name);
                Assert.That(items[0].UnwrapErr().Message, Does.Contain("pointer size of 0 bytes"), array.GetType().Name);
            }
        }
    }

    // The table starts at the Objects offset of the array, so chunk 1 sits 1 pointer past that and not past the array.
    [Test]
    public void Read_IndirectTableAtObjectsOffset_ReadsEveryPointer()
    {
        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk + 1, _chunk, _nextChunk);
        memory.AddPointers(_chunk, Objects(0, PointersPerChunk));
        memory.AddPointers(_nextChunk, ObjectAt(PointersPerChunk));

        var pointers = ReadAll(new IndirectObjectArray(memory, _array, layout), int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Objects(0, PointersPerChunk + 1)));
            Assert.That(memory.ReadsAt(_array + layout.Objects + memoryLayout.PointerSize), Is.EqualTo(1));
        }
    }

    // The data pointer sits at the Objects offset of the array.
    [Test]
    public void Read_ItemDataPointerAtObjectsOffset_ReadsEveryPointer()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, _chunk, 3);
        memory.AddObjectItems(_chunk, Item, Objects(0, 3));

        Assert.That(ReadAll(new ItemObjectArray(memory, _array, _itemLayout, Item), 3), Is.EqualTo(Objects(0, 3)));
    }

    // The pointer to the chunk pointers sits at the Objects offset of the array.
    [Test]
    public void Read_ChunkedTablePointerAtObjectsOffset_ReadsEveryPointer()
    {
        var memory = MemoryWithChunks(PerChunk + 1, _chunk, _nextChunk);
        memory.AddObjectItems(_nextChunk, Item, ObjectAt(PerChunk));

        var pointers = ReadAll(new ChunkedObjectArray(memory, _array, _chunkedLayout, Item), int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Objects(0, PerChunk + 1)));
            Assert.That(memory.ReadsAt(_array + _chunkedLayout.Objects), Is.EqualTo(1));
        }
    }

    // A count at exactly 2 full chunks is inside the bound and the end of chunk 1 stops the reads before the pointer of chunk 2.
    [Test]
    public void Read_IndirectCountAtFullChunks_ReadsEveryPointerAndStops()
    {
        const int Count = 2 * PointersPerChunk;

        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, Count, _chunk, _nextChunk);
        memory.AddPointers(_chunk, Objects(0, PointersPerChunk));
        memory.AddPointers(_nextChunk, Objects(PointersPerChunk, PointersPerChunk));

        var pointers = ReadAll(new IndirectObjectArray(memory, _array, layout), int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Objects(0, Count)));
            Assert.That(memory.ReadsAt(_array + layout.Objects + (2 * memoryLayout.PointerSize)), Is.Zero);
        }
    }

    // A count at exactly 2 full chunks is inside the bound and the end of chunk 1 stops the reads before the pointer of chunk 2.
    [Test]
    public void Read_ChunkedCountAtFullChunks_ReadsEveryPointerAndStops()
    {
        var memory = MemoryWithChunks(2 * PerChunk, _chunk, _nextChunk);
        memory.AddObjectItems(_nextChunk, Item, Objects(PerChunk, PerChunk));

        var pointers = ReadAll(new ChunkedObjectArray(memory, _array, _chunkedLayout, Item), int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Objects(0, 2 * PerChunk)));
            Assert.That(memory.ReadsAt(_table + (2 * memoryLayout.PointerSize)), Is.Zero);
        }
    }

    // 1 object past 2 full chunks fails before any chunk read, because the bound is exactly chunks times the chunk size.
    [Test]
    public void Read_IndirectOnePastFullChunks_YieldsOneFailure()
    {
        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, (2 * PointersPerChunk) + 1, _chunk, _nextChunk);

        var items = new IndirectObjectArray(memory, _array, layout).Read(int.MaxValue).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The object array at {_array} has 32769 objects but 2 chunks of 16384."));
            Assert.That(memory.ReadsAt(_array + layout.Objects), Is.Zero);
        }
    }

    // 1 object past 2 full chunks fails before any chunk read, because the bound is exactly chunks times the chunk size.
    [Test]
    public void Read_ChunkedOnePastFullChunks_YieldsOneFailure()
    {
        var memory = MemoryWithChunks(2 * PerChunk, _chunk, _nextChunk);
        memory.WriteInt32(_array + _chunkedLayout.NumElements, (2 * PerChunk) + 1);

        var items = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(int.MaxValue).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The object array at {_array} has 9 objects but 2 chunks of 4."));
            Assert.That(memory.ReadsAt(_table), Is.Zero);
        }
    }

    // A sample fails the same as a full read when the array count is past the chunks, because that count comes from a wrong offset.
    [Test]
    public void Read_ChunkedSampleOfCountPastChunks_YieldsOneFailure()
    {
        var memory = MemoryWithChunks(2 * PerChunk, _chunk, _nextChunk);
        memory.WriteInt32(_array + _chunkedLayout.NumElements, 100);

        var items = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(2).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The object array at {_array} has 100 objects but 2 chunks of 4."));
            Assert.That(memory.ReadsAt(_table), Is.Zero);
        }
    }

    // A sample fails the same as a full read when the array count is past the chunks, because that count comes from a wrong offset.
    [Test]
    public void Read_IndirectSampleOfCountPastChunks_YieldsOneFailure()
    {
        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk + 1, _chunk);
        memory.AddPointers(_chunk, Objects(0, PointersPerChunk));

        var items = new IndirectObjectArray(memory, _array, layout).Read(2).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The object array at {_array} has 16385 objects but 1 chunks of 16384."));
            Assert.That(memory.ReadsAt(_array + layout.Objects), Is.Zero);
        }
    }

    // A count with 0 chunks can't be right, because the engine allocates a chunk before it counts an object.
    [Test]
    public void Read_IndirectZeroChunks_YieldsOneFailure()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, IndirectLayout, 3);

        var items = new IndirectObjectArray(memory, _array, IndirectLayout).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The object array at {_array} has 3 objects but 0 chunks of 16384."));
        }
    }

    // A count with 0 chunks can't be right, because the engine allocates a chunk before it counts an object.
    [Test]
    public void Read_ChunkedZeroChunks_YieldsOneFailure()
    {
        var memory = MemoryWithChunks(3);

        var items = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The object array at {_array} has 3 objects but 0 chunks of 4."));
        }
    }

    // An unallocated 1st chunk keeps its positions, so the next chunk still starts at the size of 1 chunk.
    [Test]
    public void Read_IndirectFirstChunkUnallocated_KeepsPositions()
    {
        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk + 1, Address.Zero, _nextChunk);
        memory.AddPointers(_nextChunk, ObjectAt(PointersPerChunk));

        var pointers = ReadAll(new IndirectObjectArray(memory, _array, layout), int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(PointersPerChunk + 1));
            Assert.That(pointers.Take(PointersPerChunk), Has.All.EqualTo(Address.Zero));
            Assert.That(pointers[^1], Is.EqualTo(ObjectAt(PointersPerChunk)));
        }
    }

    // An unallocated last chunk stops at the count and not at the end of that chunk.
    [Test]
    public void Read_ChunkedLastChunkUnallocated_StopsAtTheCount()
    {
        var memory = MemoryWithChunks(PerChunk + 2, _chunk, Address.Zero);

        var pointers = ReadAll(new ChunkedObjectArray(memory, _array, _chunkedLayout, Item), int.MaxValue);

        Address[] expected = [.. Objects(0, PerChunk), Address.Zero, Address.Zero];

        Assert.That(pointers, Is.EqualTo(expected));
    }

    // An unallocated 1st chunk still counts for the position in the failure of the block behind it.
    [Test]
    public void Read_ChunkedBlockUnreadableBehindUnallocatedChunk_FailsWithPositionInTheArray()
    {
        var memory = MemoryWithChunks(PerChunk + 1, Address.Zero, _nextChunk);

        var items = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(int.MaxValue).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(PerChunk + 1));
            Assert.That(
                items[^1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the 1 object pointers from position 4 of the object array at {_array}: reading {_nextChunk} failed. "
                    + $"No block contains {_nextChunk}."));
        }
    }

    // A chunk of 1500 takes a block of 1024 and a block of 476, so a failed 2nd block of chunk 1 is at position 2524 with length 476.
    [Test]
    public void Read_ChunkedSecondBlockOfSecondChunkUnreadable_FailsWithPositionInTheArray()
    {
        var layout = _chunkedLayout with { ElementsPerChunk = 1500 };
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, 3000, _chunk, _nextChunk);
        memory.AddObjectItems(_chunk, Item, Objects(0, 1500));
        memory.AddObjectItems(_nextChunk, Item, Objects(1500, 1024));

        var items = new ChunkedObjectArray(memory, _array, layout, Item).Read(int.MaxValue).ToList();
        var block = _nextChunk + (1024 * Item.Size);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Take(2524).Select(static read => read.Unwrap()), Is.EqualTo(Objects(0, 2524)));
            Assert.That(items, Has.Count.EqualTo(2525));
            Assert.That(
                items[^1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the 476 object pointers from position 2524 of the object array at {_array}: reading {block} failed. "
                    + $"No block contains {block}."));
        }
    }

    // 1 item per chunk is the smallest chunk the constructor allows, so every position has its own chunk pointer.
    [Test]
    public void Read_ChunkedOneItemPerChunk_KeepsPositions()
    {
        var layout = _chunkedLayout with { ElementsPerChunk = 1 };
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, 3, _chunk, Address.Zero, _lastChunk);
        memory.AddObjectItems(_chunk, Item, ObjectAt(0));
        memory.AddObjectItems(_lastChunk, Item, ObjectAt(2));

        Address[] expected = [ObjectAt(0), Address.Zero, ObjectAt(2)];

        Assert.That(ReadAll(new ChunkedObjectArray(memory, _array, layout, Item), int.MaxValue), Is.EqualTo(expected));
    }

    // The largest chunk size doesn't overflow the bound or the chunk math.
    [Test]
    public void Read_ChunkedLargestElementsPerChunk_ReadsThePointers()
    {
        var layout = _chunkedLayout with { ElementsPerChunk = int.MaxValue };
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, 3, _chunk);
        memory.AddObjectItems(_chunk, Item, Objects(0, 3));

        Assert.That(ReadAll(new ChunkedObjectArray(memory, _array, layout, Item), int.MaxValue), Is.EqualTo(Objects(0, 3)));
    }

    // A table pointer of 0 with objects counted is a failure, because only a chunk pointer of 0 means the engine hasn't allocated it yet.
    [Test]
    public void Read_ChunkedTablePointerZero_FailsWithChunk0()
    {
        var memory = MemoryWithChunks(3, _chunk);
        memory.WritePointer(_array + _chunkedLayout.Objects, Address.Zero);

        var items = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the pointer of chunk 0 of the object array at {_array}: reading {Address.Zero} failed. "
                    + $"No block contains {Address.Zero}."));
        }
    }

    // A chunk pointer at the top of the address range is 1 failure with that address and doesn't throw.
    [Test]
    public void Read_ChunkedChunkAtTopOfAddressRange_YieldsOneFailure()
    {
        var memory = MemoryWithChunks(3, TopOfAddressRange);

        var items = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.Contain($"reading {TopOfAddressRange} failed"));
        }
    }

    // A data pointer at the top of the address range is 1 failure with that address and doesn't throw.
    [Test]
    public void Read_ItemDataAtTopOfAddressRange_YieldsOneFailure()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, TopOfAddressRange, 3);

        var items = new ItemObjectArray(memory, _array, _itemLayout, Item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.Contain($"reading {TopOfAddressRange} failed"));
        }
    }

    // Empty items on both sides of a block boundary keep their positions, so the next pointer is still the index of its object.
    [Test]
    public void Read_ItemZeroSlotsAroundBlockBoundary_KeepPositions()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, _chunk, 1026);
        memory.AddObjectItems(_chunk, Item, Objects(0, 1026));
        memory.WritePointer(_chunk + (1023L * Item.Size) + Item.ObjectPointer, Address.Zero);
        memory.WritePointer(_chunk + (1024L * Item.Size) + Item.ObjectPointer, Address.Zero);

        var pointers = ReadAll(new ItemObjectArray(memory, _array, _itemLayout, Item), int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(1026));
            Assert.That(pointers[1022], Is.EqualTo(ObjectAt(1022)));
            Assert.That(pointers[1023], Is.EqualTo(Address.Zero));
            Assert.That(pointers[1024], Is.EqualTo(Address.Zero));
            Assert.That(pointers[1025], Is.EqualTo(ObjectAt(1025)));
        }
    }

    // A pointer that ends at the end of its item fits, so the constructor takes it and each read comes from the last bytes of the item.
    [Test]
    public void Read_ObjectPointerAtEndOfItem_ReadsThePointers()
    {
        ObjectItemLayout item = new(2 * memoryLayout.PointerSize, 3 * memoryLayout.PointerSize);

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, _chunk, 3);
        memory.AddObjectItems(_chunk, item, Objects(0, 3));

        Assert.That(ReadAll(new ItemObjectArray(memory, _array, _itemLayout, item), 3), Is.EqualTo(Objects(0, 3)));
    }

    // A pointer that ends 1 byte past its item doesn't fit, so both shapes with items throw.
    [Test]
    public void Constructor_ObjectPointerOneBytePastItem_ThrowsArgumentOutOfRangeException()
    {
        ObjectItemLayout item = new((2 * memoryLayout.PointerSize) + 1, 3 * memoryLayout.PointerSize);

        AssertBothThrow(item);
    }

    // The largest offset overflows a 4-byte sum with overflow checks on, so the check needs 8-byte math to throw the right type.
    [TestCase(int.MaxValue, 0x18)]
    [TestCase(int.MaxValue, int.MaxValue)]
    [TestCase(int.MinValue, 0x18)]
    [TestCase(0, int.MinValue)]
    public void Constructor_ExtremeItem_ThrowsArgumentOutOfRangeException(int objectPointer, int size)
    {
        AssertBothThrow(new ObjectItemLayout(objectPointer, size));
    }

    // A block of 1024 items of more than 1 KB each would overflow the size of its buffer, so both shapes with items throw past 1 KB.
    [TestCase(1025)]
    [TestCase(int.MaxValue)]
    public void Constructor_ItemPastSizeLimit_ThrowsArgumentOutOfRangeException(int size)
    {
        AssertBothThrow(new ObjectItemLayout(0, size));
    }

    // An item of exactly 1 KB is the largest that still reads.
    [Test]
    public void Read_ItemAtSizeLimit_ReturnsPointers()
    {
        ObjectItemLayout item = new(0, 1024);
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, _chunk, 3);
        memory.AddObjectItems(_chunk, item, Objects(0, 3));

        Assert.That(ReadAll(new ItemObjectArray(memory, _array, _itemLayout, item), 3), Is.EqualTo(Objects(0, 3)));
    }

    // The block reads decode only 4-byte and 8-byte pointers, so a memory with 3-byte or 5-byte pointers fails in every shape
    // instead of throwing or cutting each pointer to 4 bytes.
    [TestCase(3)]
    [TestCase(5)]
    public void Read_PointerSizeNotFourOrEight_YieldsOneFailureInEveryShape(int pointerSize)
    {
        // Keep every address below 16 MB, so it fits in 3 bytes.
        Address pointers = new(0x10_0000);
        Address indirect = new(0x20_0000);
        Address items = new(0x30_0000);
        Address chunked = new(0x40_0000);
        Address data = new(0x50_0000);
        Address table = new(0x60_0000);
        Address block = new(0x70_0000);
        ObjectItemLayout item = new(0, 16);

        FakeMemory memory = new() { Layout = new MemoryLayout((byte)pointerSize, ByteOrder.LittleEndian) };
        memory.AddNameArray(pointers, data, 1, block);
        memory.AddIndirectObjectArray(indirect, IndirectLayout, 1, block);
        memory.AddItemObjectArray(items, _itemLayout, block, 1);
        memory.AddChunkedObjectArray(chunked, _chunkedLayout, table, 1, block);
        memory.Add(block, new byte[item.Size]);

        (Address Array, IObjectArray Reader)[] shapes =
        [
            (pointers, new PointerObjectArray(memory, pointers)),
            (indirect, new IndirectObjectArray(memory, indirect, IndirectLayout)),
            (items, new ItemObjectArray(memory, items, _itemLayout, item)),
            (chunked, new ChunkedObjectArray(memory, chunked, _chunkedLayout, item)),
        ];

        using (Assert.EnterMultipleScope())
        {
            foreach ((var array, var reader) in shapes)
            {
                var read = reader.Read(1).ToList();

                Assert.That(read, Has.Count.EqualTo(1), reader.GetType().Name);
                Assert.That(
                    read[0].UnwrapErr().Message,
                    Is.EqualTo($"Can't read the object pointers of the object array at {array}: a pointer size of {pointerSize} bytes isn't 4 or 8."),
                    reader.GetType().Name);
            }
        }
    }

    // Stopping at the 1st pointer reads neither the 2nd block nor the next chunk pointer, because a search can stop early.
    [Test]
    public void Read_ChunkedFirstPointerOnly_ReadsOnlyTheFirstBlock()
    {
        var layout = _chunkedLayout with { ElementsPerChunk = 1500 };
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, 3000, _chunk, _nextChunk);
        memory.AddObjectItems(_chunk, Item, Objects(0, 1500));

        var first = new ChunkedObjectArray(memory, _array, layout, Item).Read(int.MaxValue).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(ObjectAt(0)));
            Assert.That(memory.ReadsAt(_chunk + (1024 * Item.Size)), Is.Zero);
            Assert.That(memory.ReadsAt(_table + memoryLayout.PointerSize), Is.Zero);
        }
    }

    // Stopping at the 1st pointer doesn't read the next chunk pointer, because a search can stop early.
    [Test]
    public void Read_IndirectFirstPointerOnly_ReadsOnlyTheFirstBlock()
    {
        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk + 1, _chunk, _nextChunk);
        memory.AddPointers(_chunk, Objects(0, PointersPerChunk));

        var first = new IndirectObjectArray(memory, _array, layout).Read(int.MaxValue).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(ObjectAt(0)));
            Assert.That(memory.ReadsAt(_chunk + (1024 * memoryLayout.PointerSize)), Is.Zero);
            Assert.That(memory.ReadsAt(_array + layout.Objects + memoryLayout.PointerSize), Is.Zero);
        }
    }

    // Stopping at the 1st pointer doesn't read the 2nd block, because a search can stop early.
    [Test]
    public void Read_ItemFirstPointerOnly_ReadsOnlyTheFirstBlock()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, _chunk, 2000);
        memory.AddObjectItems(_chunk, Item, Objects(0, 2000));

        var first = new ItemObjectArray(memory, _array, _itemLayout, Item).Read(int.MaxValue).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(ObjectAt(0)));
            Assert.That(memory.ReadsAt(_chunk + (1024 * Item.Size)), Is.Zero);
        }
    }

    // Each enumeration of 1 sequence reads the header again, because the engine adds objects while it runs.
    [Test]
    public void Read_SameSequenceTwice_ReadsTheNewCount()
    {
        var memory = MemoryWithChunks(3, _chunk);
        var sequence = new ChunkedObjectArray(memory, _array, _chunkedLayout, Item).Read(int.MaxValue);

        int first = CountOf(sequence);
        memory.WriteInt32(_array + _chunkedLayout.NumElements, 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(3));
            Assert.That(CountOf(sequence), Is.EqualTo(2));
        }
    }

    // 2 enumerators of 1 sequence keep their own position and their own buffer, also across a block boundary.
    [Test]
    public void Read_TwoEnumeratorsAtOnce_KeepTheirOwnPositions()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, _itemLayout, _chunk, 1026);
        memory.AddObjectItems(_chunk, Item, Objects(0, 1026));

        var sequence = new ItemObjectArray(memory, _array, _itemLayout, Item).Read(int.MaxValue);

        using var ahead = sequence.GetEnumerator();
        using var behind = sequence.GetEnumerator();

        for (int moves = 0; moves < 1025; moves++)
        {
            ahead.MoveNext();
        }

        behind.MoveNext();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ahead.Current.Unwrap(), Is.EqualTo(ObjectAt(1024)));
            Assert.That(behind.Current.Unwrap(), Is.EqualTo(ObjectAt(0)));
        }
    }

    // The engine allocates the chunk before it raises the count, so a count read 1st is never past the chunk count read after it.
    [Test]
    public void Read_IndirectEngineGrowsBetweenHeaderReads_ReadsTheFirstCount()
    {
        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk, _chunk, _nextChunk);
        memory.AddPointers(_chunk, Objects(0, PointersPerChunk));
        memory.AddPointers(_nextChunk, ObjectAt(PointersPerChunk));
        memory.WriteInt32(_array + layout.NumChunks, 1);

        GrowingMemory growing = new(
            memory,
            () =>
            {
                memory.WriteInt32(_array + layout.NumChunks, 2);
                memory.WriteInt32(_array + layout.NumElements, PointersPerChunk + 1);
            });

        var pointers = ReadAll(new IndirectObjectArray(growing, _array, layout), int.MaxValue);

        Assert.That(pointers, Is.EqualTo(Objects(0, PointersPerChunk)));
    }

    // The engine allocates the chunk before it raises the count, so a count read 1st is never past the chunk count read after it.
    [Test]
    public void Read_ChunkedEngineGrowsBetweenHeaderReads_ReadsTheFirstCount()
    {
        var memory = MemoryWithChunks(PerChunk, _chunk, _nextChunk);
        memory.AddObjectItems(_nextChunk, Item, ObjectAt(PerChunk));
        memory.WriteInt32(_array + _chunkedLayout.NumChunks, 1);

        GrowingMemory growing = new(
            memory,
            () =>
            {
                memory.WriteInt32(_array + _chunkedLayout.NumChunks, 2);
                memory.WriteInt32(_array + _chunkedLayout.NumElements, PerChunk + 1);
            });

        var pointers = ReadAll(new ChunkedObjectArray(growing, _array, _chunkedLayout, Item), int.MaxValue);

        Assert.That(pointers, Is.EqualTo(Objects(0, PerChunk)));
    }

    // A full table of 512 chunks is 8M positions and the last position comes from the last slot of chunk 511.
    [Test]
    public void Read_IndirectFullTable_ReadsLastPositionOfLastChunk()
    {
        const int Count = TableLength * PointersPerChunk;

        var chunks = new Address[TableLength];
        chunks[TableLength - 1] = _chunk;

        var pointers = new Address[PointersPerChunk];
        pointers[PointersPerChunk - 1] = _object;

        var layout = IndirectLayout;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, Count, chunks);
        memory.AddPointers(_chunk, pointers);

        int seen = 0;
        var last = Address.Zero;

        foreach (var item in new IndirectObjectArray(memory, _array, layout).Read(int.MaxValue))
        {
            last = item.Unwrap();
            seen++;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen, Is.EqualTo(Count));
            Assert.That(last, Is.EqualTo(_object));
        }
    }

    // A big-endian item has its pointer in that byte order, so offset 4 of a 12-byte item reads back as the same address.
    [Test]
    public void Read_BigEndianItems_ReadsObjectAtItsOffset()
    {
        FakeMemory memory = new() { Layout = MemoryLayout.BigEndian32 };
        memory.Add(new Address(0x1000), [0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x02]);
        memory.Add(
            new Address(0x2000),
            [
                0xAB, 0xAB, 0xAB, 0xAB, 0x80, 0x00, 0x00, 0x10, 0xAB, 0xAB, 0xAB, 0xAB,
                0xAB, 0xAB, 0xAB, 0xAB, 0x80, 0x00, 0x00, 0x20, 0xAB, 0xAB, 0xAB, 0xAB,
            ]);

        ItemObjectArray array = new(memory, new Address(0x1000), new ItemObjectArrayLayout(0, 4), new ObjectItemLayout(4, 12));

        Address[] expected = [new(0x8000_0010), new(0x8000_0020)];

        Assert.That(ReadAll(array, 2), Is.EqualTo(expected));
    }

    private static int CountOf(IEnumerable<Result<Address>> sequence)
    {
        return sequence.Count();
    }

    private static List<Address> ReadAll(IObjectArray array, int count)
    {
        return [.. array.Read(count).Select(static read => read.Unwrap())];
    }

    private void AssertBothThrow(ObjectItemLayout item)
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => new ItemObjectArray(memory, _array, _itemLayout, item),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("item"));
            Assert.That(
                () => new ChunkedObjectArray(memory, _array, _chunkedLayout, item),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("item"));
        }
    }

    private (Address Array, Address Count, IObjectArray Reader)[] EveryShape(FakeMemory memory)
    {
        var pointers = _array;
        var indirect = _array + 0x10_0000;
        var items = _array + 0x20_0000;
        var chunked = _array + 0x30_0000;

        return
        [
            (pointers, pointers + memoryLayout.PointerSize, new PointerObjectArray(memory, pointers)),
            (indirect, indirect + IndirectLayout.NumElements, new IndirectObjectArray(memory, indirect, IndirectLayout)),
            (items, items + _itemLayout.NumElements, new ItemObjectArray(memory, items, _itemLayout, Item)),
            (chunked, chunked + _chunkedLayout.NumElements, new ChunkedObjectArray(memory, chunked, _chunkedLayout, Item)),
        ];
    }

    // Adds the items of _chunk only, so any other chunk doesn't read until a test adds it.
    private FakeMemory MemoryWithChunks(int count, params Address[] chunks)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, _chunkedLayout, _table, count, chunks);
        memory.AddObjectItems(_chunk, Item, Objects(0, PerChunk));

        return memory;
    }

    private Address ObjectAt(int position)
    {
        return _object + (position * 0x100L);
    }

    private Address[] Objects(int first, int count)
    {
        return [.. Enumerable.Range(first, count).Select(ObjectAt)];
    }
}

/// <summary>
///     Runs an action once, right after the 1st read, so a test can change memory between 2 reads of a header.
/// </summary>
/// <param name="memory">The memory to read from.</param>
/// <param name="grow">The action to run after the 1st read.</param>
file sealed class GrowingMemory(
    FakeMemory memory,
    Action grow) : IMemoryReader
{
    private bool _grown;

    /// <inheritdoc/>
    public MemoryLayout Layout => memory.Layout;

    /// <inheritdoc/>
    public Result Read(Address address, Span<byte> buffer)
    {
        var read = memory.Read(address, buffer);

        if (!_grown)
        {
            _grown = true;
            grow();
        }

        return read;
    }
}
