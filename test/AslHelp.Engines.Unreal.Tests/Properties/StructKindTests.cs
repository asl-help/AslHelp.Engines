using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class StructKindTests(
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

    private UnrealPath Data => _game.GWorld.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // A steady read is the slot, 3 reads per object hop, the class of the data object and X, so the struct step doesn't read a pointer.
    [Test]
    public void Read_StructMember_StepsByOffsetWithoutPointerRead()
    {
        var x = Data.Member("Coord").Member<int>("X");
        _ = x.Read().Unwrap();

        int reads = _fakeGame.Memory.Reads;
        int value = x.Read().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(FakeGame.CoordX));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (2 * 3) + 2 + 1));
        }
    }

    // Y sits 4 bytes into the struct, so the read adds both offsets.
    [Test]
    public void Read_SecondStructMember_AddsBothOffsets()
    {
        Assert.That(Data.Member("Coord").Member<int>("Y").Read().Unwrap(), Is.EqualTo(FakeGame.CoordY));
    }

    // Mixed has an int and a float, so a path that ends on it fails with its type and a hint to go on into a field.
    [Test]
    public void Read_PathEndsOnMixedStruct_FailsWithContinueMessage()
    {
        Assert.That(
            Data.Member<int>("Mixed").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Mixed. Mixed is the struct FixtureMixed. Continue the path into 1 of its fields."));
    }

    // Position has 3 FloatProperty members right after each other, so it reads as 1 float array and still steps into Y.
    [Test]
    public void Read_ThreeFloatMembers_ReturnsFloatArray()
    {
        float[] expected = [1f, 2f, 3f];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member<float[]>("Position").Read().Unwrap(), Is.EqualTo(expected));
            Assert.That(Data.Member("Position").Member<float>("Y").Read().Unwrap(), Is.EqualTo(2f));
        }
    }

    // DoublePosition has the same member names with DoubleProperty, so the element type comes from the members and not the name.
    [Test]
    public void Read_ThreeDoubleMembers_ReturnsDoubleArray()
    {
        double[] expected = [1.5, 2.5, 3.5];

        Assert.That(Data.Member<double[]>("DoublePosition").Read().Unwrap(), Is.EqualTo(expected));
    }

    // Coord has 2 IntProperty members, so it reads as an int array and a float array read fails with both types.
    [Test]
    public void Read_IntStructAsFloatArray_FailsWithBothTypes()
    {
        int[] expected = [FakeGame.CoordX, FakeGame.CoordY];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member<int[]>("Coord").Read().Unwrap(), Is.EqualTo(expected));
            Assert.That(
                Data.Member<float[]>("Coord").Read().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Coord. Coord is StructProperty, which reads as Int32[], not Single[]."));
        }
    }

    // A struct that doesn't change returns the array of the watcher before, so the watcher doesn't change or allocate a new array.
    [Test]
    public void CreateWatcher_SameVectorTwice_KeepsTheArray()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member<float[]>("Position").CreateWatcher(ticks);
        float[]? first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // Z sits 4 bytes past the end of Y, so the struct doesn't read as an array and fails with the struct end.
    [Test]
    public void Read_GapBetweenMembers_FailsWithStructEnd()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.PropertyAddress("VectorZ") + FakeGame.PropertyOffsetOffset, 0xC);

        Assert.That(
            _fakeGame.Attach().GWorld.Member("GameMode").Member("Data").Member<float[]>("Position").Read().UnwrapErr().Message,
            Does.EndWith("Position is the struct FixtureVector. Continue the path into 1 of its fields."));
    }

    // Z is a static array of 2 floats, so the struct doesn't read as an array that would drop the 2nd one and fails with the struct end.
    [Test]
    public void Read_StaticArrayMember_FailsWithStructEnd()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.PropertyAddress("VectorZ") + FakeGame.PropertyArrayDimOffset, 2);

        Assert.That(
            Data.Member<float[]>("Position").Read().UnwrapErr().Message,
            Does.EndWith("Position is the struct FixtureVector. Continue the path into 1 of its fields."));
    }

    // Hp is an IntProperty, so a member after it fails at Hp with its class.
    [Test]
    public void Read_MemberAfterValue_FailsWithNotContainer()
    {
        Assert.That(
            Data.Member("Hp").Member<int>("X").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.X at GWorld.GameMode.Data.Hp. Hp is IntProperty, which doesn't have members."));
    }

    // A struct pointer of 0 fails at the struct segment with the property and its address.
    [Test]
    public void Read_StructTypePointerNull_Fails()
    {
        var coord = _fakeGame.PropertyAddress("Coord");
        _fakeGame.Memory.WritePointer(coord + FakeGame.StructTypeOffset, Address.Zero);

        Assert.That(
            Data.Member("Coord").Member<int>("X").Read().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data.Coord.X at GWorld.GameMode.Data.Coord. The struct pointer of Coord at {coord} is 0. "
                + "Check Struct of StructProperty in the profile (0x70)."));
    }

    // A member that the struct doesn't have fails with the struct type and the member.
    [Test]
    public void Read_MemberNotOnStruct_FailsWithTheStructType()
    {
        Assert.That(
            Data.Member("Coord").Member<int>("Z").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Coord.Z. Class FixtureCoord doesn't have a property Z."));
    }

    // The path of a struct reads the address of the struct inside the data object.
    [Test]
    public void Read_PathToStruct_ReadsTheAddressOfTheStruct()
    {
        Assert.That(Data.Member("Coord").Read().Unwrap(), Is.EqualTo(_fakeGame.Data + 0xB0));
    }

    // A document without the struct offset still attaches and only a step into a struct fails with the missing path.
    [Test]
    public void Read_StructWithoutStructOffset_FailsWithProfilePath()
    {
        string structType = shape == "ffield" ? "FStructProperty" : "UStructProperty";
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", structType));

        Assert.That(
            game.GWorld.Member("GameMode").Member("Data").Member("Coord").Member<int>("X").Read().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.Coord.X at GWorld.GameMode.Data.Coord. "
                + $"Can't read StructProperty: the profile doesn't have offsets.{structType}.Struct."));
    }
}
