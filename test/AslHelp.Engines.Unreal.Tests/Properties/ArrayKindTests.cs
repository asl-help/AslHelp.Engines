using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ArrayKindTests(
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

    private Result<IWatcherSource<int[]>> IntArray => Data.Member("IntArray").Value<int[]>();

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // TArray<int32> reads its 3 elements in order, the elements in 1 read after the count and the data pointer.
    [Test]
    public void Read_IntArray_ReturnsTheElementsInOneRead()
    {
        int[] expected = [100, 200, 300];
        _ = IntArray.Read().Unwrap();

        int reads = _fakeGame.Memory.ReadsAt(_fakeGame.CollectionData);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(IntArray.Read().Unwrap(), Is.EqualTo(expected));
            Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.CollectionData) - reads, Is.EqualTo(1));
        }
    }

    // A watcher of an array that doesn't change keeps its array, so Changed stays false.
    [Test]
    public void CreateWatcher_SameElementsTwice_KeepsTheArray()
    {
        ManualTicks ticks = new();
        var watcher = IntArray.Unwrap().CreateWatcher(ticks);
        int[]? first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A changed element gives a new array and leaves the old one as it was, so Old and Current differ.
    [Test]
    public void CreateWatcher_OneElementChanged_KeepsTheOldArray()
    {
        int[] old = [100, 200, 300];
        int[] current = [100, 250, 300];
        ManualTicks ticks = new();
        var watcher = IntArray.Unwrap().CreateWatcher(ticks);
        _ = watcher.Current;

        _fakeGame.Memory.WriteInt32(_fakeGame.CollectionData + 4, 250);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(current));
            Assert.That(watcher.Old, Is.EqualTo(old));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // 1025 elements is past the bound of 1024, so the read fails with both numbers before it reads the elements.
    [Test]
    public void Read_CountAboveBound_FailsWithCountAndBound()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, 1025);

        Assert.That(
            IntArray.Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray. IntArray has 1025 elements, past the limit of 1024."));
    }

    // A negative count comes from a damaged header and fails with the count.
    [Test]
    public void Read_NegativeCount_Fails()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, -2);

        Assert.That(
            IntArray.Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray. IntArray has the count -2, which is below 0."));
    }

    // A count of 3 with a data pointer of 0 fails but a count of 0 reads as empty without the data pointer.
    [Test]
    public void Read_NullData_FailsWithCountAndReadsEmptyWithoutOne()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.Data + FakeGame.IntArrayOffset, Address.Zero);
        string message = IntArray.Read().UnwrapErr().Message;

        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray. IntArray has 3 elements, but its data pointer is 0."));
            Assert.That(IntArray.Read().Unwrap(), Is.Empty);
        }
    }

    // An index reads 1 element and an index past the count or below 0 fails with the index and the count.
    [Test]
    public void Element_Index_ReadsOneElementAndFailsOutsideTheCount()
    {
        var intArray = Data.Member("IntArray");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(intArray.Item(2).Value<int>().Read().Unwrap(), Is.EqualTo(300));
            Assert.That(
                intArray.Item(3).Value<int>().Read().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray[3]. IntArray doesn't have index 3. It has 3 elements."));
            Assert.That(
                intArray.Item(-1).Value<int>().Read().UnwrapErr().Message,
                Does.EndWith("IntArray doesn't have index -1. It has 3 elements."));
        }
    }

    // 1 element by index only checks the count, so index 2000 of a 2001-element array reads past the bound of a whole read.
    [Test]
    public void Element_IndexPastBoundOfLongArray_Reads()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.IntArrayOffset + 0x8, 2001);
        _fakeGame.Memory.WriteInt32(_fakeGame.CollectionData + (2000 * sizeof(int)), 42);

        Assert.That(Data.Member("IntArray").Item(2000).Value<int>().Read().Unwrap(), Is.EqualTo(42));
    }

    // A string key on an array can't be an index, so it fails with the key type of the array.
    [Test]
    public void Element_StringKeyOnArray_FailsWithKeyTypeMismatch()
    {
        Assert.That(
            Data.Member("IntArray").Item("one").Value<int>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray[\"one\"]. The keys of IntArray are Int32, which the key \"one\" can't match."));
    }

    // CoordArray has 8-byte struct elements, so element 1 starts 8 bytes in and its X is 3.
    [Test]
    public void Indexer_StructArray_StepsByTheElementSizeOfInner()
    {
        Assert.That(Data.Member("CoordArray").Item(1).Member("X").Value<int>().Read().Unwrap(), Is.EqualTo(3));
    }

    // FixtureCoord has 2 int members, so an array of it reads as an array of int arrays.
    [Test]
    public void Read_ArrayOfNumberStructs_ReadsAnArrayPerElement()
    {
        int[][] expected = [[1, 2], [3, 4]];

        Assert.That(Data.Member("CoordArray").Value<int[][]>().Read().Unwrap(), Is.EqualTo(expected));
    }

    // Items has object elements, so an index reads the pointer and Items[1].Hp reads the Hp of the other data object.
    [Test]
    public void Indexer_ObjectArray_DereferencesTheElement()
    {
        Assert.That(Data.Member("Items").Item(1).Member("Hp").Value<int>().Read().Unwrap(), Is.EqualTo(500));
    }

    // A null element is a null object, so a member after it fails like any null object on a path.
    [Test]
    public void Indexer_NullObjectElement_FailsAsNullObject()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.CollectionData + 0x4300, Address.Zero);

        Assert.That(
            Data.Member("Items").Item(0).Member("Hp").Value<int>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Items[0].Hp: GWorld.GameMode.Data.Items[0] is null."));
    }

    // Each FString element reads as its text and a steady read keeps the texts and the array.
    [Test]
    public void CreateWatcher_StringArray_KeepsTheTextsAndTheArray()
    {
        string[] expected = ["a", "b", "c"];
        ManualTicks ticks = new();
        var watcher = Data.Member("StringArray").Value<string[]>().Unwrap().CreateWatcher(ticks);
        string[]? first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(expected));
            Assert.That(watcher.Current, Is.SameAs(first));
        }
    }

    // An int array read as long values fails with both array types.
    [Test]
    public void Read_AsWrongElementType_FailsWithBothTypes()
    {
        Assert.That(
            Data.Member("IntArray").Value<long[]>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.IntArray. IntArray is ArrayProperty, which reads as Int32[], not Int64[]."));
    }

    // Count after the array reads its number of elements and any member after it fails.
    [Test]
    public void Count_Array_ReadsTheCountAndRefusesOtherMembers()
    {
        var items = Data.Member("Items");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Count().Value<int>().Read().Unwrap(), Is.EqualTo(2));
            Assert.That(
                items.Member("Length").Value<int>().Read().UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read GWorld.GameMode.Data.Items.Length. "
                    + "Items is a collection, so it takes an index or Count, not the member Length."));
        }
    }

    // An index after a struct member fails at the index with the member, because a struct isn't a collection.
    [Test]
    public void Element_OnAStructMember_FailsNotCollection()
    {
        Assert.That(
            Data.Member("Mixed").Item(0).Value<int>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Mixed[0]. Mixed isn't a collection, so it can't take the index 0."));
    }

    // A document without the array offsets still attaches and only the array read fails with the missing paths.
    [Test]
    public void Read_ArrayWithoutArrayOffsets_FailsWithProfilePath()
    {
        string arrayType = shape == "ffield" ? "FArrayProperty" : "UArrayProperty";
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", arrayType));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("IntArray").Value<int[]>().Read().UnwrapErr().Message,
            Is.EqualTo($"Can't read GWorld.GameMode.Data.IntArray. Can't read ArrayProperty: the profile doesn't have offsets.{arrayType}.Inner."));
    }
}
