using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class CollectionKindEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    private const int IntDictOffset = 0x1C0;
    private const int IntDictSlotSize = 0x10;
    private const int IntSetSlotSize = 0xC;

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), "uproperty"),
        new(MemoryLayout.X86, new Address(0x8000_0000), "ffield"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "uproperty"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "ffield"),
    ];

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    private UnrealPath Data => _game.GWorld.Member("GameMode").Member("Data");

    private Address DictPairs => _fakeGame.CollectionData + 0x4500;

    private Address IntDictPairs => _fakeGame.CollectionData + 0x4700;

    private Address IntSet => _fakeGame.Data + FakeGame.IntSetOffset;

    private Address IntSetSlots => _fakeGame.CollectionData + 0x4400;

    // Leaves room for 1024 slots of 12 bytes up to the end of the collection block.
    private Address LongSetSlots => _fakeGame.CollectionData + 0x5000;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // 1024 elements is the bound itself, so the whole read still takes every element.
    [Test]
    public void Read_ArrayAtBound_ReadsEveryElement()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, 1024);

        for (int i = 0; i < 1024; i++)
        {
            _fakeGame.Memory.WriteInt32(_fakeGame.CollectionData + (i * sizeof(int)), i);
        }

        int[] values = Data.Member<int[]>("IntArray").Read().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Has.Length.EqualTo(1024));
            Assert.That(values[0], Is.Zero);
            Assert.That(values[1023], Is.EqualTo(1023));
        }
    }

    // An array that grows past the kept buffer reads the new elements and gives a new array.
    [Test]
    public void CreateWatcher_ArrayGrowsPastTheBuffer_ReadsTheNewElements()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member<int[]>("IntArray").CreateWatcher(ticks);
        _ = watcher.Current;

        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, 600);
        _fakeGame.Memory.WriteInt32(_fakeGame.CollectionData + (599 * sizeof(int)), 7);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Has.Length.EqualTo(600));
            Assert.That(watcher.Current![2], Is.EqualTo(300));
            Assert.That(watcher.Current[599], Is.EqualTo(7));
            Assert.That(watcher.Old, Has.Length.EqualTo(3));
        }
    }

    // An array that empties gives an empty array once and then keeps it, so Changed is true once.
    [Test]
    public void CreateWatcher_ArrayEmpties_ChangesOnceThenKeepsTheEmptyArray()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member<int[]>("IntArray").CreateWatcher(ticks);
        _ = watcher.Current;

        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, 0);
        ticks.Value++;
        int[]? empty = watcher.Current;
        bool changed = watcher.Changed;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty, Is.Empty);
            Assert.That(changed, Is.True);
            Assert.That(watcher.Current, Is.SameAs(empty));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A set without slots reads as empty without the data pointer and a key lookup reads false.
    [Test]
    public void Read_SetWithoutSlots_IsEmpty()
    {
        _fakeGame.Memory.WriteInt32(IntSet + 0x8, 0);
        _fakeGame.Memory.WriteInt32(IntSet + 0x34, 0);
        _fakeGame.Memory.WritePointer(IntSet, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member<HashSet<int>>("IntSet").Read().Unwrap(), Is.Empty);
            Assert.That(Data.Member("IntSet").Member<int>("Count").Read().Unwrap(), Is.Zero);
            Assert.That(Data.Member("IntSet").Element<bool>(11).Read().Unwrap(), Is.False);
        }
    }

    // Every slot is free, so the old bytes in the slots don't count and the set reads as empty.
    [Test]
    public void Read_SetWithOnlyFreeSlots_IsEmpty()
    {
        _fakeGame.Memory.Write(IntSet + 0x10, 0u);
        _fakeGame.Memory.WriteInt32(IntSet + 0x34, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member<HashSet<int>>("IntSet").Read().Unwrap(), Is.Empty);
            Assert.That(Data.Member("IntSet").Member<int>("Count").Read().Unwrap(), Is.Zero);
            Assert.That(Data.Member("IntSet").Element<bool>(22).Read().Unwrap(), Is.False);
        }
    }

    // More free slots than the 4 slots or a negative number of free slots comes from a damaged header, so Count fails with both numbers.
    [TestCase(5)]
    [TestCase(-2)]
    public void Count_FreeSlotsOutsideTheSlots_FailsWithBothNumbers(int free)
    {
        _fakeGame.Memory.WriteInt32(IntSet + 0x34, free);

        Assert.That(
            Data.Member("IntSet").Count.Read().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data.IntSet.Count. IntSet has {free} free slots of 4. Check the NumFreeIndices offset in the profile."));
    }

    // A negative number of slots fails with that number and not with the slots minus the free slots.
    [Test]
    public void Count_NegativeSlots_FailsWithTheSlots()
    {
        _fakeGame.Memory.WriteInt32(IntSet + 0x8, -5);
        _fakeGame.Memory.WriteInt32(IntSet + 0x34, -10);

        Assert.That(
            Data.Member("IntSet").Count.Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntSet.Count. IntSet has the count -5, which is below 0."));
    }

    // A slot size of 0 would read every element from slot 0, so the set fails with the size until the game writes a real one.
    [Test]
    public void Read_SetSlotSizeZero_FailsUntilTheSizeIsSet()
    {
        var slotSize = _fakeGame.PropertyAddress("IntSet") + FakeGame.SetSlotSizeOffset;
        _fakeGame.Memory.WriteInt32(slotSize, 0);
        string message = Data.Member<HashSet<int>>("IntSet").Read().UnwrapErr().Message;

        _fakeGame.Memory.WriteInt32(slotSize, IntSetSlotSize);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.IntSet. The elements of IntSet are 0 bytes. Check SetLayout in the profile."));
            Assert.That(Data.Member<HashSet<int>>("IntSet").Read().Unwrap(), Has.Count.EqualTo(3));
        }
    }

    // An inner property with an element size of -4 would read the array backwards, so the array fails with the size.
    [Test]
    public void Read_ArrayElementSizeNegative_FailsWithTheSize()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.PropertyAddress("IntArrayInner") + FakeGame.PropertyElementSizeOffset, -4);

        Assert.That(
            Data.Member<int[]>("IntArray").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray. The elements of IntArray are -4 bytes. Check ElementSize in the profile."));
    }

    // An index outside the array fails the same way on every tick, so a steady miss returns the same error.
    [Test]
    public void Element_SteadyIndexOutOfRange_ReturnsTheSameError()
    {
        var value = Data.Member("IntArray").Element<int>(5);
        var first = value.Read().UnwrapErr();

        Assert.That(value.Read().UnwrapErr(), Is.SameAs(first));
    }

    // Slots with a data pointer of 0 fail the read with the number of slots.
    [Test]
    public void Read_SetSlotsDataNull_FailsWithTheCount()
    {
        _fakeGame.Memory.WritePointer(IntSet, Address.Zero);

        Assert.That(
            Data.Member<HashSet<int>>("IntSet").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntSet. IntSet has 4 elements, but its data pointer is 0."));
    }

    // Slot 39 is bit 7 of the 2nd word of the bits, so it counts and slot 32 next to it doesn't.
    [Test]
    public void Read_SetSlotInTheSecondWord_TestsTheBitOfThatWord()
    {
        WriteLongSet(40, 38);
        _fakeGame.Memory.Write(IntSet + 0x10, 0b1u);
        _fakeGame.Memory.Write(IntSet + 0x14, 1u << 7);
        HashSet<int> expected = [1000, 1039];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member<HashSet<int>>("IntSet").Read().Unwrap(), Is.EquivalentTo(expected));
            Assert.That(Data.Member("IntSet").Element<bool>(1039).Read().Unwrap(), Is.True);
            Assert.That(Data.Member("IntSet").Element<bool>(1032).Read().Unwrap(), Is.False);
        }
    }

    // 1024 slots is the bound itself, so the last bit of the 32nd word on the heap still counts.
    [Test]
    public void Read_SetAtBoundWithHeapBits_ReadsTheLastSlot()
    {
        var heapBits = _fakeGame.CollectionData + 0x4C00;
        WriteLongSet(1024, 1022);

        for (int word = 0; word < 32; word++)
        {
            _fakeGame.Memory.Write(heapBits + (word * sizeof(uint)), word switch { 0 => 1u, 31 => 1u << 31, _ => 0u });
        }

        _fakeGame.Memory.WritePointer(IntSet + _fakeGame.SparseSecondaryBitsOffset, heapBits);
        HashSet<int> expected = [1000, 2023];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member<HashSet<int>>("IntSet").Read().Unwrap(), Is.EquivalentTo(expected));
            Assert.That(Data.Member("IntSet").Element<bool>(2023).Read().Unwrap(), Is.True);
        }
    }

    // A torn read can show 22 in 2 slots after 33 went away, so the set gives {11, 22} instead of the last set.
    [Test]
    public void CreateWatcher_SlotRepeatsAnElement_GivesTheNewSet()
    {
        HashSet<int> expected = [11, 22];
        ManualTicks ticks = new();
        var watcher = Data.Member<HashSet<int>>("IntSet").CreateWatcher(ticks);
        _ = watcher.Current;

        _fakeGame.Memory.WriteInt32(IntSetSlots + (2 * IntSetSlotSize), 22);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EquivalentTo(expected));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A torn read can show the pair of 5 in 2 slots after 6 went away, so the map gives 2 pairs instead of the last map.
    [Test]
    public void CreateWatcher_SlotRepeatsAPair_GivesTheNewDictionary()
    {
        Dictionary<int, int> expected = new() { [4] = 40, [5] = 50 };
        ManualTicks ticks = new();
        var watcher = Data.Member<Dictionary<int, int>>("IntDict").CreateWatcher(ticks);
        _ = watcher.Current;

        WritePair(2, 5, 50);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EquivalentTo(expected));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // 2 slots with the key 5 and other values keep the value of the later slot instead of failing.
    [Test]
    public void Read_SlotRepeatsAKeyWithAnotherValue_KeepsTheLaterSlot()
    {
        Dictionary<int, int> expected = new() { [4] = 40, [5] = 60 };
        WritePair(2, 5, 60);

        Assert.That(Data.Member<Dictionary<int, int>>("IntDict").Read().Unwrap(), Is.EquivalentTo(expected));
    }

    // The pair of 6 moves from slot 2 to slot 0, so the lookup misses at the slot it matched last and finds the new slot.
    [Test]
    public void Element_KeyMovesToAnotherSlot_FindsTheNewSlot()
    {
        var value = Data.Member("IntDict").Element<int>(6);
        int first = value.Read().Unwrap();

        WritePair(0, 6, 66);
        WritePair(2, 4, 40);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(60));
            Assert.That(value.Read().Unwrap(), Is.EqualTo(66));
        }
    }

    // The slot of key 6 gets freed with its old bytes, so the lookup at that slot doesn't count and the key isn't there.
    [Test]
    public void Element_SlotOfLastMatchFreed_FailsWithTheKey()
    {
        var value = Data.Member("IntDict").Element<int>(6);
        _ = value.Read().Unwrap();

        _fakeGame.Memory.Write(_fakeGame.Data + IntDictOffset + 0x10, 0b011u);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                value.Read().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.IntDict[6]. IntDict doesn't have the key 6."));
            Assert.That(Data.Member<Dictionary<int, int>>("IntDict").Read().Unwrap().ContainsKey(6), Is.False);
        }
    }

    // A key outside the BMP and with accents matches the UTF-16 text of the FString key.
    [Test]
    public void Element_UnicodeStringKey_MatchesTheUtf16Text()
    {
        string key = "ü-日本-😀";
        var chars = _fakeGame.CollectionData + 0x4620;
        _fakeGame.Memory.WriteUtf16(chars, key + '\0');
        _fakeGame.Memory.WritePointer(DictPairs + 0x20, chars);
        _fakeGame.Memory.WriteInt32(DictPairs + 0x20 + 0x8, key.Length + 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("Dict").Element<int>(key).Read().Unwrap(), Is.EqualTo(2));
            Assert.That(Data.Member<Dictionary<string, int>>("Dict").Read().Unwrap()[key], Is.EqualTo(2));
        }
    }

    // An empty FString key has a count of 0 and a data pointer of 0, so the empty text finds it.
    [Test]
    public void Element_EmptyStringKey_MatchesAnEmptyFString()
    {
        _fakeGame.Memory.WritePointer(DictPairs, Address.Zero);
        _fakeGame.Memory.WriteInt32(DictPairs + 0x8, 0);

        Assert.That(Data.Member("Dict").Element<int>(string.Empty).Read().Unwrap(), Is.EqualTo(1));
    }

    // A watcher of 1 map value keeps working while the key changes slots on every tick.
    [Test]
    public void CreateWatcher_KeySwapsSlotsEveryTick_ReadsTheValueEachTime()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member("IntDict").Element<int>(4).CreateWatcher(ticks);
        List<object?> values = [watcher.Current];

        for (int tick = 0; tick < 4; tick++)
        {
            bool front = tick % 2 == 1;
            WritePair(0, front ? 4 : 6, front ? 40 + tick : 60);
            WritePair(2, front ? 6 : 4, front ? 60 : 40 + tick);
            ticks.Value++;
            values.Add(watcher.Current);
        }

        object[] expected = [40, 40, 41, 42, 43];

        Assert.That(values, Is.EqualTo(expected));
    }

    // Writes the header of IntSet with its slots at LongSetSlots and the value 1000 + i in slot i.
    private void WriteLongSet(int slotCount, int freeCount)
    {
        _fakeGame.Memory.WritePointer(IntSet, LongSetSlots);
        _fakeGame.Memory.WriteInt32(IntSet + 0x8, slotCount);
        _fakeGame.Memory.WriteInt32(IntSet + 0x34, freeCount);

        for (int i = 0; i < slotCount; i++)
        {
            _fakeGame.Memory.WriteInt32(LongSetSlots + (i * IntSetSlotSize), 1000 + i);
        }
    }

    // Writes the key and the value of slot slot of IntDict.
    private void WritePair(int slot, int key, int value)
    {
        var pair = IntDictPairs + (slot * IntDictSlotSize);
        _fakeGame.Memory.WriteInt32(pair, key);
        _fakeGame.Memory.WriteInt32(pair + 0x4, value);
    }
}
