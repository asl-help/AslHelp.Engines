using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class MapKindTests(
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

    private Result<IMemberPath> Data => _game.GWorld.Path.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // Dict has FString keys, so "two" finds the 2nd pair and reads its value at the offset of the value property.
    [Test]
    public void Element_StringKey_ReadsTheValueAtTheValueOffset()
    {
        Assert.That(Data.Member("Dict").Item("two").Read<int>().Unwrap(), Is.EqualTo(2));
    }

    // NameDict has name keys, so a text matches a name and a name with a number matches its text with the number.
    [TestCase(FakeGame.NameText, 7)]
    [TestCase("Hp_4", 8)]
    public void Element_NameKey_MatchesByTheNameText(string key, int expected)
    {
        Assert.That(Data.Member("NameDict").Item(key).Read<int>().Unwrap(), Is.EqualTo(expected));
    }

    // A long key converts to the int key type, so 5L finds the value 50 like an int key finds 60.
    [Test]
    public void Element_IntegerKeysOfOtherTypes_ConvertToTheKeyType()
    {
        var intDict = Data.Member("IntDict");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(intDict.Item(5L).Read<int>().Unwrap(), Is.EqualTo(50));
            Assert.That(intDict.Item(6).Read<int>().Unwrap(), Is.EqualTo(60));
        }
    }

    // A key the map doesn't have and a long that doesn't fit an int key fail with the map and the key.
    [TestCase(7L, "7")]
    [TestCase(5_000_000_000L, "5000000000")]
    public void Element_MissingKey_FailsWithTheKey(long key, string text)
    {
        Assert.That(
            Data.Member("IntDict").Item(key).Read<int>().UnwrapErr().Message,
            Is.EqualTo($"Can't read GWorld.GameMode.Data.IntDict[{text}]. IntDict doesn't have the key {text}."));
    }

    // A key that matched before gets checked 1st, so a steady lookup only reads the header, the bits and that key.
    [Test]
    public void Element_SteadyKey_ChecksTheLastSlotFirst()
    {
        var value = Data.Member("IntDict").Item(6);
        _ = value.Read<int>().Unwrap();

        int reads = _fakeGame.Memory.ReadsAt(_fakeGame.CollectionData + 0x4700);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Read<int>().Unwrap(), Is.EqualTo(60));
            Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.CollectionData + 0x4700) - reads, Is.Zero);
        }
    }

    // MixedDict has struct values, so a key steps into the struct at the offset of the value property.
    [Test]
    public void Indexer_StructValue_StepsIntoTheValue()
    {
        Assert.That(Data.Member("MixedDict").Item(5).Member("B").Read<float>().Unwrap(), Is.EqualTo(6f));
    }

    // IntDict has int keys and int values, so it reads as a dictionary and a watcher keeps it while it doesn't change.
    [Test]
    public void CreateWatcher_IntToIntMap_ReadsADictionaryAndKeepsIt()
    {
        Dictionary<int, int> expected = new() { [4] = 40, [5] = 50, [6] = 60 };
        ManualTicks ticks = new();
        var watcher = Data.Member("IntDict").Value<Dictionary<int, int>>().Unwrap().CreateWatcher(ticks);
        var first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EquivalentTo(expected));
            Assert.That(watcher.Current, Is.SameAs(first));
        }
    }

    // A changed value gives a new dictionary, so Old keeps the value before.
    [Test]
    public void CreateWatcher_ChangedValue_GivesANewDictionary()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member("IntDict").Value<Dictionary<int, int>>().Unwrap().CreateWatcher(ticks);
        _ = watcher.Current;

        _fakeGame.Memory.WriteInt32(_fakeGame.CollectionData + 0x4700 + 0x14, 55);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current![5], Is.EqualTo(55));
            Assert.That(watcher.Old![5], Is.EqualTo(50));
        }
    }

    // A map of mixed struct values doesn't read as 1 value, so the read fails with a hint to index it.
    [Test]
    public void Read_StructValues_FailsWithElementsNotValue()
    {
        Assert.That(
            Data.Member("MixedDict").Read<Dictionary<int, int>>().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.MixedDict. MixedDict is MapProperty of StructProperty, which doesn't read as 1 value. "
                + "Index it or read Count."));
    }

    // ObjectDict has object values, so a key steps into the object and the whole map boxes its values as addresses.
    [Test]
    public void Item_MapWithObjectValues_StepsIntoTheObject()
    {
        var objectDict = Data.Member("ObjectDict");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectDict.Item("other").Member("Hp").Read<int>().Unwrap(), Is.EqualTo(500));
            Assert.That(
                objectDict.Read().Unwrap(),
                Is.EqualTo(new Dictionary<string, Address> { ["data"] = _fakeGame.Data, ["other"] = _fakeGame.OtherData }));
        }
    }

    // A path key can't be an address, so an index on a map keyed by objects is the position among the used pairs and skips the free slot.
    [Test]
    public void Item_MapKeyedByObjects_TakesAPosition()
    {
        var objectKeyDict = Data.Member("ObjectKeyDict");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(objectKeyDict.Item(0).Read<int>().Unwrap(), Is.EqualTo(1));
            Assert.That(objectKeyDict.Item(1).Read<int>().Unwrap(), Is.EqualTo(2));
            Assert.That(objectKeyDict.Read().Unwrap(), Is.EqualTo(new Dictionary<Address, int> { [_fakeGame.Data] = 1, [_fakeGame.OtherData] = 2 }));
        }
    }

    // Count after the map reads its number of pairs.
    [Test]
    public void Count_Map_ReadsTheNumberOfPairs()
    {
        Assert.That(Data.Member("IntDict").Count.Read<int>().Unwrap(), Is.EqualTo(3));
    }

    // 2 paths to MixedDict with FixtureCoord values share 1 read, so another value read between 2 ticks still gives the watcher its dictionary.
    [Test]
    public void CreateWatcher_OtherReadBetweenTicks_KeepsTheDictionary()
    {
        var coordType = _fakeGame.Memory.ReadPointer(_fakeGame.PropertyAddress("Coord") + FakeGame.StructTypeOffset).Unwrap();
        _fakeGame.Memory.WritePointer(_fakeGame.PropertyAddress("MixedDictValue") + FakeGame.StructTypeOffset, coordType);
        var y = _fakeGame.CollectionData + 0x4900 + 0x8;
        ManualTicks ticks = new();
        var watcher = Data.Member("MixedDict").Value<Dictionary<int, int[]>>().Unwrap().CreateWatcher(ticks);
        var first = watcher.Current;

        _fakeGame.Memory.WriteInt32(y, 1);
        _ = Data.Member("MixedDict").Read<Dictionary<int, int[]>>().Unwrap();
        _fakeGame.Memory.Write(y, 6f);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }
}
