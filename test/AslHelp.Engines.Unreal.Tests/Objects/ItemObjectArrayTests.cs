using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Objects;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ItemObjectArrayTests(
    MemoryLayout memoryLayout,
    Address start,
    ItemObjectArrayLayout layout,
    ObjectItemLayout item)
{
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    // Uses the offsets of the 4.11 to 4.18 profiles and an object offset of 8, which 5.8 has.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new ItemObjectArrayLayout(0, 0x8), new ObjectItemLayout(0, 0xC)),
        new(MemoryLayout.X86, new Address(0x8000_0000), new ItemObjectArrayLayout(0, 0x8), new ObjectItemLayout(0, 0x10)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ItemObjectArrayLayout(0, 0xC), new ObjectItemLayout(0, 0x10)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ItemObjectArrayLayout(0, 0xC), new ObjectItemLayout(0, 0x18)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ItemObjectArrayLayout(0, 0xC), new ObjectItemLayout(8, 0x18)),
    ];

    private readonly Address _array = start;
    private readonly Address _data = start + 0x10_0000;
    private readonly Address _object = start + 0x100_0000;

    // The count sits at the NumElements offset.
    [Test]
    public void ReadCount_ReadableArray_ReturnsCount()
    {
        Assert.That(new ItemObjectArray(MemoryWithItems(20), _array, layout, item).ReadCount().Unwrap(), Is.EqualTo(20));
    }

    // A failed read fails with the array and the failed read and does not throw.
    [Test]
    public void ReadCount_Unreadable_FailsWithArrayAndCause()
    {
        var address = _array + layout.NumElements;

        Assert.That(
            new ItemObjectArray(new FakeMemory { Layout = memoryLayout }, _array, layout, item).ReadCount().UnwrapErr().Message,
            Is.EqualTo($"Can't read the count of the object array at {_array}: reading {address} failed. No block contains {address}."));
    }

    // Each pointer comes from the object offset of its item, 1 item size past the item before.
    [Test]
    public void Read_Items_ReturnsObjectPointerOfEachItem()
    {
        var pointers = ReadAll(new ItemObjectArray(MemoryWithItems(20), _array, layout, item), 20);

        Assert.That(pointers, Is.EqualTo(Enumerable.Range(0, 20).Select(ObjectAt)));
    }

    // ReadItem reads the object and the serial number of the item at the index and an empty item past the count.
    [Test]
    public void ReadItem_Index_ReturnsTheObjectAndTheSerialNumber()
    {
        var memory = MemoryWithItems(3);
        var serialItem = item with { SerialNumber = item.ObjectPointer + memoryLayout.PointerSize };
        memory.WriteInt32(_data + (2 * item.Size) + serialItem.SerialNumber!.Value, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new ItemObjectArray(memory, _array, layout, item).ReadItem(2).Unwrap(), Is.EqualTo(new ObjectItem(ObjectAt(2), null)));
            Assert.That(new ItemObjectArray(memory, _array, layout, serialItem).ReadItem(2).Unwrap(), Is.EqualTo(new ObjectItem(ObjectAt(2), 9)));
            Assert.That(new ItemObjectArray(memory, _array, layout, item).ReadItem(3).Unwrap(), Is.Default);
        }
    }

    // An empty item comes back as address 0, because the position of a pointer is the index of its object.
    [Test]
    public void Read_ZeroItem_KeepsItInPlace()
    {
        var memory = MemoryWithItems(3);
        memory.WritePointer(_data + item.Size + item.ObjectPointer, Address.Zero);

        Address[] expected = [ObjectAt(0), Address.Zero, ObjectAt(2)];

        Assert.That(ReadAll(new ItemObjectArray(memory, _array, layout, item), 3), Is.EqualTo(expected));
    }

    // 2000 items take 2 blocks, the 2nd starting 1024 items past the 1st.
    [Test]
    public void Read_MoreThanOneBlock_ReadsEveryPointerInOrder()
    {
        var memory = MemoryWithItems(2000);

        var pointers = ReadAll(new ItemObjectArray(memory, _array, layout, item), 2000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Is.EqualTo(Enumerable.Range(0, 2000).Select(ObjectAt)));
            Assert.That(memory.ReadsAt(_data + (1024 * item.Size)), Is.EqualTo(1));
        }
    }

    // A sample of 512 takes 1 read of the block and not 1 read per item, because a real read is a system call.
    [Test]
    public void Read_SampleOf512_ReadsOneBlock()
    {
        var memory = MemoryWithItems(2000);

        var pointers = ReadAll(new ItemObjectArray(memory, _array, layout, item), 512);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(512));
            Assert.That(memory.ReadsAt(_data), Is.EqualTo(1));
            Assert.That(memory.Reads, Is.EqualTo(3));
        }
    }

    // 2000 items with memory ending after 1500 read the 2nd block 1 item at a time, so the items up to 1500 still read and each one after it fails.
    [Test]
    public void Read_BlockPastReadableMemory_ReadsItsItemsOneByOne()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, layout, _data, 2000);
        memory.Add(_data, new byte[1500 * item.Size]);

        var items = new ItemObjectArray(memory, _array, layout, item).Read(2000).ToList();
        var address = _data + (1500 * item.Size) + item.ObjectPointer;

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

    // A count past the array count stops at the array count.
    [Test]
    public void Read_CountAboveArrayCount_StopsAtArrayCount()
    {
        var pointers = ReadAll(new ItemObjectArray(MemoryWithItems(20), _array, layout, item), 4_000_000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(20));
            Assert.That(pointers[19], Is.EqualTo(ObjectAt(19)));
        }
    }

    // A count of 0 or below from the caller or an array count of 0 gives nothing.
    [TestCase(0, 3)]
    [TestCase(-1, 3)]
    [TestCase(3, 0)]
    public void Read_CountZeroOrBelow_GivesNothing(int count, int arrayCount)
    {
        var memory = MemoryWithItems(3);
        memory.WriteInt32(_array + layout.NumElements, arrayCount);

        Assert.That(new ItemObjectArray(memory, _array, layout, item).Read(count), Is.Empty);
    }

    // A count that does not read yields 1 failure and does not throw.
    [Test]
    public void Read_UnreadableHeader_YieldsOneFailure()
    {
        var items = new ItemObjectArray(new FakeMemory { Layout = memoryLayout }, _array, layout, item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"Can't read the count of the object array at {_array}"));
        }
    }

    // A data pointer that does not read yields 1 failure with the array and the address of the pointer.
    [Test]
    public void Read_UnreadableDataPointer_YieldsOneFailure()
    {
        var address = _array + layout.Objects;
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array + layout.NumElements, new byte[sizeof(int)]);
        memory.WriteInt32(_array + layout.NumElements, 3);

        var items = new ItemObjectArray(memory, _array, layout, item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo($"Can't read the data pointer of the object array at {_array}: reading {address} failed. No block contains {address}."));
        }
    }

    // A data pointer of 0 or into nothing yields 1 failure, because a header can be read before its array exists.
    [TestCase(0L)]
    [TestCase(0x1000L)]
    public void Read_DataPointerIntoNothing_YieldsOneFailure(long data)
    {
        var memory = MemoryWithItems(3);
        var pointer = new Address((ulong)data);
        memory.WritePointer(_array + layout.Objects, pointer);

        var items = new ItemObjectArray(memory, _array, layout, item).Read(3).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Does.Contain($"position 0 of the object array at {_array}").And.Contain($"reading {pointer} failed"));
        }
    }

    // Calling Read without enumerating reads nothing, because a search can stop early.
    [Test]
    public void Read_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithItems(3);

        _ = new ItemObjectArray(memory, _array, layout, item).Read(3);

        Assert.That(memory.Reads, Is.Zero);
    }

    // An item too small for a pointer at its object offset throws, because the read would run past the item.
    [TestCase(8, 8)]
    [TestCase(0, 0)]
    [TestCase(-1, 0x18)]
    public void Constructor_SizeSmallerThanObjectPlusPointer_ThrowsArgumentOutOfRangeException(int objectOffset, int size)
    {
        FakeMemory memory = new() { Layout = memoryLayout };

        Assert.That(
            () => new ItemObjectArray(memory, _array, layout, new ObjectItemLayout(objectOffset, size)),
            Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("item")
                .And.Message.Contain($"-byte pointer at offset {objectOffset}"));
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new ItemObjectArray(null!, _array, layout, item),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    private static List<Address> ReadAll(ItemObjectArray array, int count)
    {
        return [.. array.Read(count).Select(static read => read.Unwrap())];
    }

    private Address ObjectAt(int position)
    {
        return _object + (position * 0x100L);
    }

    private FakeMemory MemoryWithItems(int count)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddItemObjectArray(_array, layout, _data, count);
        memory.AddObjectItems(_data, item, [.. Enumerable.Range(0, count).Select(ObjectAt)]);

        return memory;
    }
}
