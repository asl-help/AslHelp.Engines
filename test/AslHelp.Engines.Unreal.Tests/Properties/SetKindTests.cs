using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class SetKindTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
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

    private Result<IMemberPath> IntSet => _game.GWorld.Path.Member("GameMode").Member("Data").Member("IntSet");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // The 4th slot is free, so the set has 11, 22 and 33 and its count is the 4 slots minus the free slot.
    [Test]
    public void Read_SetWithFreeSlot_SkipsTheFreeSlot()
    {
        HashSet<int> expected = [11, 22, 33];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _game.GWorld.Path.Member("GameMode").Member("Data").Member("IntSet").Read<HashSet<int>>().Unwrap(),
                Is.EquivalentTo(expected));
            Assert.That(IntSet.Count().Read<int>().Unwrap(), Is.EqualTo(3));
        }
    }

    // A key the set has reads true. A key it doesn't have and 44 in the free slot read false.
    [TestCase(22, true)]
    [TestCase(23, false)]
    [TestCase(44, false)]
    public void Element_Key_ReadsWhetherTheSetHasIt(int key, bool expected)
    {
        Assert.That(IntSet.Item(key).Read<bool>().Unwrap(), Is.EqualTo(expected));
    }

    // A number past the range of the elements can't be in the set, so it reads false like any other key the set doesn't have.
    [Test]
    public void Element_KeyPastTheRangeOfTheElements_ReadsFalse()
    {
        Assert.That(IntSet.Item(5_000_000_000).Read<bool>().Unwrap(), Is.False);
    }

    // A text key can't match int elements, so it fails with the key type of the set.
    [Test]
    public void Element_TextKeyOnIntSet_FailsWithKeyTypeMismatch()
    {
        Assert.That(
            IntSet.Item("x").Read<bool>().UnwrapErr().Message,
            Does.EndWith("The keys of IntSet are Int32, which the key \"x\" can't match."));
    }

    // A secondary pointer moves the bits to the heap, so the bits inside the set don't count.
    [Test]
    public void Read_HeapAllocationBits_ReadsTheBitsBehindTheSecondaryPointer()
    {
        var heapBits = _fakeGame.CollectionData + 0x7000;
        _fakeGame.Memory.Write(heapBits, 0b1001u);
        _fakeGame.Memory.WritePointer(_fakeGame.Data + FakeGame.IntSetOffset + _fakeGame.SparseSecondaryBitsOffset, heapBits);
        HashSet<int> expected = [11, 44];

        Assert.That(
            _game.GWorld.Path.Member("GameMode").Member("Data").Member("IntSet").Read<HashSet<int>>().Unwrap(),
            Is.EquivalentTo(expected));
    }

    // A bound passed at attach replaces the 1024 for the slots too.
    [Test]
    public void Element_MoreSlotsThanPassedBound_FailsWithThatBound()
    {
        var game = _fakeGame.Attach(new ValueBounds { MaxElements = 2 });
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntSetOffset + 0x8, 3);

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("IntSet").Item(11).Read<bool>().UnwrapErr().Message,
            Does.EndWith("IntSet has 3 elements, past the limit of 2."));
    }

    // 1025 slots is past the bound, so a lookup fails before it reads any slot.
    [Test]
    public void Element_MoreSlotsThanBound_Fails()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntSetOffset + 0x8, 1025);

        Assert.That(IntSet.Item(11).Read<bool>().UnwrapErr().Message, Does.EndWith("IntSet has 1025 elements, past the limit of 1024."));
    }

    // A watcher of a set that doesn't change keeps its set, so Changed stays false.
    [Test]
    public void CreateWatcher_SameSetTwice_KeepsTheSet()
    {
        ManualTicks ticks = new();
        var watcher = _game.GWorld.Path.Member("GameMode").Member("Data").Member("IntSet").Value<HashSet<int>>().Unwrap().CreateWatcher(ticks);
        var first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A path key can't be an address, so an index on a set of objects is the position among the used slots and skips the free slot.
    [Test]
    public void Item_SetOfObjects_StepsIntoTheObjectAtThePosition()
    {
        var objectSet = _game.GWorld.Path.Member("GameMode").Member("Data").Member("ObjectSet");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectSet.Item(1).Member("Hp").Read<int>().Unwrap(), Is.EqualTo(500));
            Assert.That(objectSet.Item(1).Read().Unwrap(), Is.EqualTo(_fakeGame.OtherData));
            Assert.That(
                objectSet.Read().Unwrap(),
                Is.EquivalentTo((Address[])[_fakeGame.Data, _fakeGame.OtherData]).And.InstanceOf<HashSet<Address>>());
        }
    }

    // A document without the bit array still attaches and only the set read fails with the missing paths.
    [Test]
    public void Read_SetWithoutBitArrayOffsets_FailsWithProfilePath()
    {
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FScriptBitArray"));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("IntSet").Item(11).Read<bool>().UnwrapErr().Message,
            Does.EndWith(
                "Can't read SetProperty: the profile doesn't have offsets.FScriptBitArray.AllocatorInstance, offsets.FScriptBitArray.NumBits."));
    }
}
