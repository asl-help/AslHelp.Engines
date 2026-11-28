using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Objects;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ChunkedObjectArrayTests(
    MemoryLayout memoryLayout,
    Address start,
    ChunkedObjectArrayLayout layout,
    ObjectItemLayout item)
{
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    // Uses the offsets of the 4.21 x86, 4.20 x64, 4.21 x64 and 5.8 x64 profiles.
    // The 5.8 row has the count at 8, the chunk count at 0x10 and the object pointer 8 bytes into a 0x18-byte item.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new ChunkedObjectArrayLayout(0, 0xC, 0x14, 65536), new ObjectItemLayout(0, 0x10)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ChunkedObjectArrayLayout(0, 0x14, 0x1C, 66560), new ObjectItemLayout(0, 0x18)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ChunkedObjectArrayLayout(0, 0x14, 0x1C, 65536), new ObjectItemLayout(0, 0x18)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ChunkedObjectArrayLayout(0, 0x8, 0x10, 65536), new ObjectItemLayout(8, 0x18)),
    ];

    private readonly Address _array = start;
    private readonly Address _chunk = start + 0x10_0000;
    private readonly Address _lastChunk = start + 0x50_0000;
    private readonly Address _nextChunk = start + 0x30_0000;
    private readonly Address _object = start + 0x100_0000;
    private readonly Address _table = start + 0x1000;

    // The count sits at the NumElements offset.
    [Test]
    public void ReadCount_ReadableArray_ReturnsCount()
    {
        Assert.That(new ChunkedObjectArray(MemoryWithObjects(20), _array, layout, item).ReadCount().Unwrap(), Is.EqualTo(20));
    }

    // A failed read fails with the array and the failed read and does not throw.
    [Test]
    public void ReadCount_Unreadable_FailsWithArrayAndCause()
    {
        var address = _array + layout.NumElements;

        Assert.That(
            new ChunkedObjectArray(new FakeMemory { Layout = memoryLayout }, _array, layout, item).ReadCount().UnwrapErr().Message,
            Is.EqualTo($"Can't read the count of the object array at {_array}: reading {address} failed. No block contains {address}."));
    }

    // The last position of chunk 0 and the 1st of chunk 1 depend on the items per chunk. The chunk pointers sit behind the pointer at Objects.
    [Test]
    public void Read_TwoChunks_ReadsEveryPointerInOrder()
    {
        int count = layout.ElementsPerChunk + 1;
        var memory = MemoryWithObjects(count);

        var pointers = ReadAll(new ChunkedObjectArray(memory, _array, layout, item), count);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Enumerable.Range(0, count).Select(ObjectAt)));
            Assert.That(memory.ReadsAt(_array + layout.Objects), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_table), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_table + memoryLayout.PointerSize), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_nextChunk), Is.EqualTo(1));
        }
    }

    // The 1st position of chunk 1 reads from the 2nd chunk pointer and a position past the count is an empty item.
    [Test]
    public void ReadItem_IndexInSecondChunk_ReadsFromTheSecondChunk()
    {
        int count = layout.ElementsPerChunk + 1;
        var array = new ChunkedObjectArray(MemoryWithObjects(count), _array, layout, item);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(array.ReadItem(count - 1).Unwrap(), Is.EqualTo(new ObjectItem(ObjectAt(count - 1), null)));
            Assert.That(array.ReadItem(count).Unwrap(), Is.Default);
        }
    }

    // A count past the 2 chunks would let an index read past the chunk pointers, so ReadItem fails like Read before any chunk read.
    [Test]
    public void ReadItem_CountPastChunks_Fails()
    {
        int count = (2 * layout.ElementsPerChunk) + 1;
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumElements, count);
        memory.WriteInt32(_array + layout.NumChunks, 2);

        string message = new ChunkedObjectArray(memory, _array, layout, item).ReadItem(count - 1).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.StartWith($"The object array at {_array} has {count} objects but 2 chunks of {layout.ElementsPerChunk}."));
            Assert.That(memory.ReadsAt(_table), Is.Zero);
        }
    }

    // An empty item comes back as address 0, because the position of a pointer is the index of its object.
    [Test]
    public void Read_ZeroSlot_KeepsItInPlace()
    {
        var memory = MemoryWithObjects(3);
        memory.WritePointer(_chunk + item.Size + item.ObjectPointer, Address.Zero);

        Address[] expected = [ObjectAt(0), Address.Zero, ObjectAt(2)];

        Assert.That(ReadAll(new ChunkedObjectArray(memory, _array, layout, item), 3), Is.EqualTo(expected));
    }

    // A chunk pointer of 0 is a chunk the engine hasn't allocated yet, so its positions stay empty and the next chunk keeps its positions.
    [Test]
    public void Read_UnallocatedChunk_YieldsZeroForItsSlots()
    {
        int perChunk = layout.ElementsPerChunk;
        int count = (2 * perChunk) + 1;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, count, _chunk, Address.Zero, _lastChunk);
        memory.AddObjectItems(_chunk, item, [.. Enumerable.Range(0, perChunk).Select(ObjectAt)]);
        memory.AddObjectItems(_lastChunk, item, ObjectAt(2 * perChunk));

        var pointers = ReadAll(new ChunkedObjectArray(memory, _array, layout, item), count);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(count));
            Assert.That(pointers[perChunk - 1], Is.EqualTo(ObjectAt(perChunk - 1)));
            Assert.That(pointers.Skip(perChunk).Take(perChunk), Has.All.EqualTo(Address.Zero));
            Assert.That(pointers[^1], Is.EqualTo(ObjectAt(2 * perChunk)));
        }
    }

    // A count past the allocated chunks fails before any chunk read, because a count like that comes from a wrong offset or a torn read.
    [Test]
    public void Read_CountPastChunks_YieldsOneFailure()
    {
        int count = (2 * layout.ElementsPerChunk) + 1;
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumElements, count);
        memory.WriteInt32(_array + layout.NumChunks, 2);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(count).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"The object array at {_array} has {count} objects but 2 chunks of {layout.ElementsPerChunk}. "
                    + "Check the NumElements and NumChunks offsets of its layout."));
            Assert.That(memory.ReadsAt(_table), Is.Zero);
        }
    }

    // The largest chunk count times the items per chunk overflows a 4-byte number, so the check of the count needs 8-byte math.
    [Test]
    public void Read_LargestChunkCount_ReadsThePointers()
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumChunks, int.MaxValue);

        Assert.That(ReadAll(new ChunkedObjectArray(memory, _array, layout, item), 3), Has.Count.EqualTo(3));
    }

    // A chunk count below 0 fails before any chunk read.
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Read_NegativeChunkCount_YieldsOneFailure(int chunks)
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumChunks, chunks);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"The object array at {_array} has {chunks} chunks, which is below 0. "
                    + "Check the NumChunks offset of its layout."));
            Assert.That(memory.ReadsAt(_table), Is.Zero);
        }
    }

    // A pointer to the chunk pointers that does not read yields 1 failure with its address.
    [Test]
    public void Read_ChunkPointerArrayUnreadable_YieldsOneFailure()
    {
        var address = _array + layout.Objects;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array + layout.NumElements, new byte[sizeof(int)]);
        memory.WriteInt32(_array + layout.NumElements, 3);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo($"Can't read the data pointer of the object array at {_array}: reading {address} failed. No block contains {address}."));
        }
    }

    // A chunk count that does not read yields 1 failure with its address.
    [Test]
    public void Read_ChunkCountUnreadable_YieldsOneFailure()
    {
        var address = _array + layout.NumChunks;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddPointers(_array + layout.Objects, _table);
        memory.Add(_array + layout.NumElements, new byte[sizeof(int)]);
        memory.WriteInt32(_array + layout.NumElements, 3);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo($"Can't read the chunk count of the object array at {_array}: reading {address} failed. No block contains {address}."));
        }
    }

    // A chunk pointer past the table yields the chunks before it and then 1 failure with the chunk and its address.
    [Test]
    public void Read_ChunkPointerUnreadable_YieldsChunkThenFailure()
    {
        int perChunk = layout.ElementsPerChunk;
        var memory = MemoryWithObjects(perChunk);
        memory.WriteInt32(_array + layout.NumElements, perChunk + 1);
        memory.WriteInt32(_array + layout.NumChunks, 2);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(perChunk + 1).ToList();
        var address = _table + memoryLayout.PointerSize;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(perChunk + 1));
            Assert.That(
                items[^1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the pointer of chunk 1 of the object array at {_array}: reading {address} failed. "
                    + $"No block contains {address}."));
        }
    }

    // A chunk ending after 1500 items reads its 2nd block 1 item at a time, so the items up to 1500 still read and each item after it fails.
    [Test]
    public void Read_BlockPastReadableMemory_ReadsItsItemsOneByOne()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, 2000, _chunk);
        memory.Add(_chunk, new byte[1500 * item.Size]);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(2000).ToList();
        var address = _chunk + (1500 * item.Size) + item.ObjectPointer;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2000));
            Assert.That(items.Take(1500).All(static read => read.IsOk), Is.True);
            Assert.That(items.Skip(1500).All(static read => read.IsErr), Is.True);
            Assert.That(
                items[1500].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the 1 object pointers from position 1500 of the object array at {_array}: reading {address} failed. "
                    + $"No block contains {address}."));
        }
    }

    // A failed block in chunk 1 has the position in the whole array and not the position in its chunk.
    [Test]
    public void Read_BlockOfSecondChunkUnreadable_FailsWithPositionInTheArray()
    {
        int perChunk = layout.ElementsPerChunk;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, perChunk + 1, _chunk, _nextChunk);
        memory.Add(_chunk, new byte[perChunk * item.Size]);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(perChunk + 1).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(perChunk + 1));
            Assert.That(
                items[^1].UnwrapErr().Message,
                Does.StartWith($"Can't read the 1 object pointers from position {perChunk} of the object array"));
        }
    }

    // A chunk that doesn't read fails at each of its positions and the chunk behind it still reads.
    [Test]
    public void Read_BlockOfFirstChunkUnreadable_GoesOnWithNextChunk()
    {
        int perChunk = layout.ElementsPerChunk;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, perChunk + 1, _chunk, _nextChunk);
        memory.AddObjectItems(_nextChunk, item, ObjectAt(perChunk));

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(perChunk + 1).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(perChunk + 1));
            Assert.That(items.Take(perChunk).All(static read => read.IsErr), Is.True);
            Assert.That(items[^1].Unwrap(), Is.EqualTo(ObjectAt(perChunk)));
        }
    }

    // Only the 1st block where no item reads gets read 1 item at a time, so a wrong profile with many dead chunks costs at most 1 block of item reads.
    [Test]
    public void Read_TwoChunksUnreadable_ReadsItemsOneByOneOnlyInTheFirst()
    {
        int perChunk = layout.ElementsPerChunk;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddChunkedObjectArray(_array, layout, _table, 2 * perChunk, _chunk, _nextChunk);

        var items = new ChunkedObjectArray(memory, _array, layout, item).Read(2 * perChunk).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2 * perChunk));
            Assert.That(items.All(static read => read.IsErr), Is.True);
            Assert.That(memory.ReadsAt(_chunk + item.Size + item.ObjectPointer), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_nextChunk + item.Size + item.ObjectPointer), Is.Zero);
        }
    }

    // A count of 0 or below from the caller or an array count of 0 gives nothing.
    [TestCase(0, 3)]
    [TestCase(-1, 3)]
    [TestCase(3, 0)]
    public void Read_CountZeroOrBelow_GivesNothing(int count, int arrayCount)
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumElements, arrayCount);

        Assert.That(new ChunkedObjectArray(memory, _array, layout, item).Read(count), Is.Empty);
    }

    // Calling Read without enumerating reads nothing, because a search can stop early.
    [Test]
    public void Read_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithObjects(3);

        _ = new ChunkedObjectArray(memory, _array, layout, item).Read(3);

        Assert.That(memory.Reads, Is.Zero);
    }

    // The header reads again on every call, because the engine adds objects while it runs.
    [Test]
    public void Read_ArrayCountChangesBetweenCalls_ReadsTheNewCount()
    {
        var memory = MemoryWithObjects(3);
        ChunkedObjectArray array = new(memory, _array, layout, item);
        _ = array.Read(3).ToList();

        memory.WriteInt32(_array + layout.NumElements, 2);

        Assert.That(ReadAll(array, 3), Has.Count.EqualTo(2));
    }

    // A chunk without items would never move the position, so 0 or below throws at construction.
    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_ElementsPerChunkZero_ThrowsArgumentOutOfRangeException(int elementsPerChunk)
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        Assert.That(
            () => new ChunkedObjectArray(memory, _array, layout with { ElementsPerChunk = elementsPerChunk }, item),
            Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("layout")
                .And.Message.Contain($"ElementsPerChunk is {elementsPerChunk}"));
    }

    // An item too small for a pointer at its object offset throws, because the read would run past the item.
    [Test]
    public void Constructor_SizeSmallerThanObjectPlusPointer_ThrowsArgumentOutOfRangeException()
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        Assert.That(
            () => new ChunkedObjectArray(memory, _array, layout, new ObjectItemLayout(8, 8)),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("item"));
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new ChunkedObjectArray(null!, _array, layout, item),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    private static List<Address> ReadAll(ChunkedObjectArray array, int count)
    {
        return [.. array.Read(count).Select(static read => read.Unwrap())];
    }

    private Address ObjectAt(int position)
    {
        return _object + (position * 0x100L);
    }

    private FakeMemory MemoryWithObjects(int count)
    {
        int perChunk = layout.ElementsPerChunk;
        FakeMemory memory = new() { Layout = memoryLayout };

        if (count <= perChunk)
        {
            memory.AddChunkedObjectArray(_array, layout, _table, count, _chunk);
            memory.AddObjectItems(_chunk, item, [.. Enumerable.Range(0, count).Select(ObjectAt)]);
        }
        else
        {
            memory.AddChunkedObjectArray(_array, layout, _table, count, _chunk, _nextChunk);
            memory.AddObjectItems(_chunk, item, [.. Enumerable.Range(0, perChunk).Select(ObjectAt)]);
            memory.AddObjectItems(_nextChunk, item, [.. Enumerable.Range(perChunk, count - perChunk).Select(ObjectAt)]);
        }

        return memory;
    }
}
