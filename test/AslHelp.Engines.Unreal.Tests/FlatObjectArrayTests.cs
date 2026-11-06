using AslHelp.Memory;
using NUnit.Framework;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class FlatObjectArrayTests(
    MemoryLayout memoryLayout,
    Address start)
{
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private readonly Address _array = start;
    private readonly Address _data = start + 0x1000;
    private readonly Address _object = start + 0x10_0000;

    // The count sits 1 pointer past the data pointer.
    [Test]
    public void ReadCount_ReadableArray_ReturnsCount()
    {
        Assert.That(new FlatObjectArray(MemoryWithObjects(20), _array).ReadCount(), Is.EqualTo(20));
    }

    // A failed read returns -1 and does not throw.
    [Test]
    public void ReadCount_UnreadableArray_ReturnsMinusOne()
    {
        Assert.That(new FlatObjectArray(new FakeMemory { Layout = memoryLayout }, _array).ReadCount(), Is.EqualTo(-1));
    }

    // A count past the array count stops at the array count.
    [Test]
    public void Read_CountAboveArrayCount_StopsAtArrayCount()
    {
        var pointers = new FlatObjectArray(MemoryWithObjects(20), _array).Read(4_000_000).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(20));
            Assert.That(pointers[19], Is.EqualTo(ObjectAt(19)));
        }
    }

    // An empty position comes back as address 0, because the position of a pointer is the index of its object.
    [Test]
    public void Read_ZeroEntry_KeepsItInPlace()
    {
        var memory = MemoryWithObjects(3);
        memory.WritePointer(_data + memoryLayout.PointerSize, Address.Zero);

        Address[] expected = [ObjectAt(0), Address.Zero, ObjectAt(2)];

        Assert.That(new FlatObjectArray(memory, _array).Read(3), Is.EqualTo(expected));
    }

    // 2000 pointers with memory ending after 1500 give the 1st block of 1024 and the failed 2nd block does not throw.
    [Test]
    public void Read_BlockPastReadableMemory_StopsBeforeIt()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array, new byte[memoryLayout.PointerSize + sizeof(int)]);
        memory.WritePointer(_array, _data);
        memory.WriteInt32(_array + memoryLayout.PointerSize, 2000);
        memory.Add(_data, new byte[1500 * memoryLayout.PointerSize]);

        Assert.That(new FlatObjectArray(memory, _array).Read(2000).Count(), Is.EqualTo(1024));
    }

    // A sample of 512 takes 1 read of the block and not 1 read per pointer, because a real read is a system call.
    [Test]
    public void Read_SampleOf512_ReadsOneBlock()
    {
        var memory = MemoryWithObjects(2000);

        var pointers = new FlatObjectArray(memory, _array).Read(512).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(512));
            Assert.That(memory.ReadsAt(_data), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_data + memoryLayout.PointerSize), Is.Zero);
            Assert.That(memory.Reads, Is.EqualTo(3));
        }
    }

    // 2000 pointers take 2 blocks, the 2nd starting right after the 1024th pointer.
    [Test]
    public void Read_MoreThanOneBlock_ReadsEveryPointerInOrder()
    {
        var memory = MemoryWithObjects(2000);

        var pointers = new FlatObjectArray(memory, _array).Read(2000).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(2000));
            Assert.That(pointers[1024], Is.EqualTo(ObjectAt(1024)));
            Assert.That(memory.ReadsAt(_data + (1024 * memoryLayout.PointerSize)), Is.EqualTo(1));
        }
    }

    // Calling Read with no enumeration reads nothing, because a search can stop early.
    [Test]
    public void Read_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithObjects(3);

        _ = new FlatObjectArray(memory, _array).Read(3);

        Assert.That(memory.Reads, Is.Zero);
    }

    // Exactly 1 block takes 1 read and does not try a 2nd block behind it.
    [Test]
    public void Read_ExactlyOneBlock_ReadsOnce()
    {
        var memory = MemoryWithObjects(1024);

        var pointers = new FlatObjectArray(memory, _array).Read(1024).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(1024));
            Assert.That(pointers[1023], Is.EqualTo(ObjectAt(1023)));
            Assert.That(memory.ReadsAt(_data), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_data + (1024 * memoryLayout.PointerSize)), Is.Zero);
        }
    }

    // 1 pointer past a block takes a 2nd read of only that pointer.
    [Test]
    public void Read_OnePastABlock_ReadsTheRest()
    {
        var memory = MemoryWithObjects(1025);

        var pointers = new FlatObjectArray(memory, _array).Read(1025).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointers, Has.Count.EqualTo(1025));
            Assert.That(pointers[1024], Is.EqualTo(ObjectAt(1024)));
        }
    }

    // A count of 0 or below from the caller or the array gives nothing.
    [TestCase(0, 3)]
    [TestCase(-1, 3)]
    [TestCase(int.MinValue, 3)]
    [TestCase(3, 0)]
    [TestCase(3, -1)]
    [TestCase(3, int.MinValue)]
    public void Read_CountZeroOrBelow_GivesNothing(int count, int arrayCount)
    {
        var memory = MemoryWithObjects(3);
        memory.WriteInt32(_array + memoryLayout.PointerSize, arrayCount);

        Assert.That(new FlatObjectArray(memory, _array).Read(count), Is.Empty);
    }

    // A header that does not read gives nothing and does not throw.
    [Test]
    public void Read_UnreadableHeader_GivesNothing()
    {
        Assert.That(new FlatObjectArray(new FakeMemory { Layout = memoryLayout }, _array).Read(3), Is.Empty);
    }

    // A data pointer of 0 or into nothing gives nothing, because a header can be read before its array exists.
    [TestCase(0L)]
    [TestCase(0x1000L)]
    public void Read_DataPointerIntoNothing_GivesNothing(long data)
    {
        var memory = MemoryWithObjects(3);
        memory.WritePointer(_array, new Address((ulong)data));

        Assert.That(new FlatObjectArray(memory, _array).Read(3), Is.Empty);
    }

    // The header reads again on every call, because the engine moves the data when the array grows.
    [Test]
    public void Read_ArrayCountChangesBetweenCalls_ReadsTheNewCount()
    {
        var memory = MemoryWithObjects(3);
        FlatObjectArray array = new(memory, _array);
        _ = array.Read(3).ToList();

        memory.WriteInt32(_array + memoryLayout.PointerSize, 2);

        Assert.That(array.Read(3).Count(), Is.EqualTo(2));
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        Assert.That(() => new FlatObjectArray(null!, _array), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    private Address ObjectAt(int position)
    {
        return _object + (position * 0x100L);
    }

    private FakeMemory MemoryWithObjects(int count)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_array, new byte[memoryLayout.PointerSize + sizeof(int)]);
        memory.WritePointer(_array, _data);
        memory.WriteInt32(_array + memoryLayout.PointerSize, count);
        memory.Add(_data, new byte[count * memoryLayout.PointerSize]);

        for (int position = 0; position < count; position++)
        {
            memory.WritePointer(_data + (position * memoryLayout.PointerSize), ObjectAt(position));
        }

        return memory;
    }
}
