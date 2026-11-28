using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Objects;

[TestFixtureSource(nameof(_fixtures))]
public sealed class IndirectObjectArrayTests(
    MemoryLayout memoryLayout,
    Address start,
    IndirectObjectArrayLayout layout)
{
    private const int PointersPerChunk = 16384;

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    // Uses the offsets of the 4.8 profiles.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new IndirectObjectArrayLayout(0, 0x800, 0x804)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new IndirectObjectArrayLayout(0, 0x1000, 0x1004)),
    ];

    private readonly Address _array = start;
    private readonly Address _chunk = start + 0x10_0000;
    private readonly Address _lastChunk = start + 0x30_0000;
    private readonly Address _nextChunk = start + 0x20_0000;
    private readonly Address _object = start + 0x100_0000;

    // The count sits at the NumElements offset, behind the chunk table.
    [Test]
    public void ReadCount_ReadableArray_ReturnsCount()
    {
        Assert.That(new IndirectObjectArray(MemoryWithObjects(20), _array, layout).ReadCount().Unwrap(), Is.EqualTo(20));
    }

    // A failed read fails with the array and the failed read and does not throw.
    [Test]
    public void ReadCount_Unreadable_FailsWithArrayAndCause()
    {
        var address = _array + layout.NumElements;

        Assert.That(
            new IndirectObjectArray(new FakeMemory { Layout = memoryLayout }, _array, layout).ReadCount().UnwrapErr().Message,
            Is.EqualTo($"Can't read the count of the object array at {_array}: reading {address} failed. No block contains {address}."));
    }

    // Position 16383 is the last of chunk 0 and position 16384 the 1st of chunk 1, whose pointers sit in the table inside the array.
    [Test]
    public void Read_TwoChunks_ReadsEveryPointerInOrder()
    {
        var memory = MemoryWithObjects(PointersPerChunk + 1);

        var pointers = ReadAll(new IndirectObjectArray(memory, _array, layout), PointersPerChunk + 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Enumerable.Range(0, PointersPerChunk + 1).Select(ObjectAt)));
            Assert.That(memory.ReadsAt(_array + layout.Objects), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_array + layout.Objects + memoryLayout.PointerSize), Is.EqualTo(1));
        }
    }

    // An empty position comes back as address 0, because the position of a pointer is the index of its object.
    [Test]
    public void Read_ZeroSlot_KeepsItInPlace()
    {
        var memory = MemoryWithObjects(3);
        memory.WritePointer(_chunk + memoryLayout.PointerSize, Address.Zero);

        Address[] expected = [ObjectAt(0), Address.Zero, ObjectAt(2)];

        Assert.That(ReadAll(new IndirectObjectArray(memory, _array, layout), 3), Is.EqualTo(expected));
    }

    // A chunk pointer of 0 is a chunk the engine hasn't allocated yet, so its positions stay empty and the next chunk keeps its positions.
    [Test]
    public void Read_UnallocatedChunk_YieldsZeroForItsSlots()
    {
        const int Count = (2 * PointersPerChunk) + 1;

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, Count, _chunk, Address.Zero, _lastChunk);
        memory.AddPointers(_chunk, [.. Enumerable.Range(0, PointersPerChunk).Select(ObjectAt)]);
        memory.AddPointers(_lastChunk, ObjectAt(2 * PointersPerChunk));

        var pointers = ReadAll(new IndirectObjectArray(memory, _array, layout), Count);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(Count));
            Assert.That(pointers[PointersPerChunk - 1], Is.EqualTo(ObjectAt(PointersPerChunk - 1)));
            Assert.That(pointers.Skip(PointersPerChunk).Take(PointersPerChunk), Has.All.EqualTo(Address.Zero));
            Assert.That(pointers[^1], Is.EqualTo(ObjectAt(2 * PointersPerChunk)));
        }
    }

    // A count past the array count stops at the array count.
    [Test]
    public void Read_CountAboveArrayCount_StopsAtArrayCount()
    {
        var pointers = ReadAll(new IndirectObjectArray(MemoryWithObjects(20), _array, layout), 4_000_000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(20));
            Assert.That(pointers[19], Is.EqualTo(ObjectAt(19)));
        }
    }

    // A count of 0 or below from the caller or an array count of 0 gives nothing.
    [TestCase(0, 3)]
    [TestCase(-1, 3)]
    [TestCase(int.MinValue, 3)]
    [TestCase(3, 0)]
    public void Read_CountZeroOrBelow_GivesNothing(int count, int arrayCount)
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumElements, arrayCount);

        Assert.That(new IndirectObjectArray(memory, _array, layout).Read(count), Is.Empty);
    }

    // A count past the allocated chunks fails before any chunk read, because a count like that comes from a wrong offset or a torn read.
    [Test]
    public void Read_CountPastChunks_YieldsOneFailure()
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumElements, 40000);
        memory.WriteInt32(_array + layout.NumChunks, 2);

        var items = new IndirectObjectArray(memory, _array, layout).Read(40000).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"The object array at {_array} has 40000 objects but 2 chunks of 16384. "
                    + "Check the NumElements and NumChunks offsets of its layout."));
            Assert.That(memory.ReadsAt(_array + layout.Objects), Is.Zero);
        }
    }

    // A count past the 2 chunks would let an index read past them, so ReadItem fails like Read before any chunk read.
    [Test]
    public void ReadItem_CountPastChunks_Fails()
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumElements, 40000);
        memory.WriteInt32(_array + layout.NumChunks, 2);

        string message = new IndirectObjectArray(memory, _array, layout).ReadItem(39999).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.StartWith($"The object array at {_array} has 40000 objects but 2 chunks of 16384."));
            Assert.That(memory.ReadsAt(_array + layout.Objects), Is.Zero);
        }
    }

    // The table inside the array has 512 chunk pointers, so a chunk count past them or below 0 fails before any chunk read.
    [TestCase(513)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void Read_ChunkCountOutsideTable_YieldsOneFailure(int chunks)
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumChunks, chunks);

        var items = new IndirectObjectArray(memory, _array, layout).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"The object array at {_array} has {chunks} chunks, which is outside 0 to 512. "
                    + "Check the NumChunks offset of its layout."));
        }
    }

    // A full table of 512 chunks is still inside the array.
    [Test]
    public void Read_ChunkCountOf512_ReadsThePointers()
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + layout.NumChunks, 512);

        Assert.That(ReadAll(new IndirectObjectArray(memory, _array, layout), 3), Has.Count.EqualTo(3));
    }

    // A chunk count that does not read yields 1 failure with its address.
    [Test]
    public void Read_ChunkCountUnreadable_YieldsOneFailure()
    {
        var address = _array + layout.NumChunks;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array + layout.NumElements, new byte[sizeof(int)]);
        memory.WriteInt32(_array + layout.NumElements, 3);

        var items = new IndirectObjectArray(memory, _array, layout).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo($"Can't read the chunk count of the object array at {_array}: reading {address} failed. No block contains {address}."));
        }
    }

    // A chunk pointer that does not read yields 1 failure with the chunk and its address.
    [Test]
    public void Read_ChunkPointerUnreadable_YieldsOneFailure()
    {
        var address = _array + layout.Objects;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array + layout.NumElements, new byte[2 * sizeof(int)]);
        memory.WriteInt32(_array + layout.NumElements, 3);
        memory.WriteInt32(_array + layout.NumChunks, 1);

        var items = new IndirectObjectArray(memory, _array, layout).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the pointer of chunk 0 of the object array at {_array}: reading {address} failed. "
                    + $"No block contains {address}."));
        }
    }

    // 2000 pointers with a chunk ending after 1500 read the 2nd block 1 pointer at a time, so the pointers up to 1500 still read and each one after it fails.
    [Test]
    public void Read_BlockPastReadableMemory_ReadsItsItemsOneByOne()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, 2000, _chunk);
        memory.Add(_chunk, new byte[1500 * memoryLayout.PointerSize]);

        var items = new IndirectObjectArray(memory, _array, layout).Read(2000).ToList();
        var address = _chunk + (1500 * memoryLayout.PointerSize);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2000));
            Assert.That(items.Take(1500).All(static item => item.IsOk), Is.True);
            Assert.That(items.Skip(1500).All(static item => item.IsErr), Is.True);
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
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk + 1, _chunk, _nextChunk);
        memory.Add(_chunk, new byte[PointersPerChunk * memoryLayout.PointerSize]);

        var items = new IndirectObjectArray(memory, _array, layout).Read(PointersPerChunk + 1).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(PointersPerChunk + 1));
            Assert.That(items[^1].UnwrapErr().Message, Does.StartWith("Can't read the 1 object pointers from position 16384 of the object array"));
        }
    }

    // A chunk that doesn't read fails at each of its positions and the chunk behind it still reads.
    [Test]
    public void Read_BlockOfFirstChunkUnreadable_GoesOnWithNextChunk()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddIndirectObjectArray(_array, layout, PointersPerChunk + 1, _chunk, _nextChunk);
        memory.AddPointers(_nextChunk, ObjectAt(PointersPerChunk));

        var items = new IndirectObjectArray(memory, _array, layout).Read(PointersPerChunk + 1).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(PointersPerChunk + 1));
            Assert.That(items.Take(PointersPerChunk).All(static item => item.IsErr), Is.True);
            Assert.That(items[^1].Unwrap(), Is.EqualTo(ObjectAt(PointersPerChunk)));
        }
    }

    // Calling Read without enumerating reads nothing, because a search can stop early.
    [Test]
    public void Read_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithObjects(3);

        _ = new IndirectObjectArray(memory, _array, layout).Read(3);

        Assert.That(memory.Reads, Is.Zero);
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new IndirectObjectArray(null!, _array, layout),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    private static List<Address> ReadAll(IndirectObjectArray array, int count)
    {
        return [.. array.Read(count).Select(static item => item.Unwrap())];
    }

    private Address ObjectAt(int position)
    {
        return _object + (position * 0x100L);
    }

    private FakeMemory MemoryWithObjects(int count)
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        if (count <= PointersPerChunk)
        {
            memory.AddIndirectObjectArray(_array, layout, count, _chunk);
            memory.AddPointers(_chunk, [.. Enumerable.Range(0, count).Select(ObjectAt)]);
        }
        else
        {
            memory.AddIndirectObjectArray(_array, layout, count, _chunk, _nextChunk);
            memory.AddPointers(_chunk, [.. Enumerable.Range(0, PointersPerChunk).Select(ObjectAt)]);
            memory.AddPointers(_nextChunk, [.. Enumerable.Range(PointersPerChunk, count - PointersPerChunk).Select(ObjectAt)]);
        }

        return memory;
    }
}
