using AslHelp.Engines.Errors;
using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using System;
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

    // Read the class of a property in the ffield shape at this offset, because FField keeps it in front of its name.
    private const int FieldClassOffset = 0x8;

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    private Result<IMemberPath> Data => _game.GWorld.Path.Member("GameMode").Member("Data");

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
        var x = Data.Member("Coord").Member("X");
        _ = x.Read<int>().Unwrap();

        int reads = _fakeGame.Memory.Reads;
        int value = x.Read<int>().Unwrap();

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
        Assert.That(Data.Member("Coord").Member("Y").Read<int>().Unwrap(), Is.EqualTo(FakeGame.CoordY));
    }

    // Mixed has an int and a float, so it reads as a StructValue and a typed int fails on the step with both types.
    [Test]
    public void Value_MixedStructAsInt_FailsWithBothTypes()
    {
        Assert.That(
            Data.Member("Mixed").Value<int>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Mixed. Mixed is StructProperty, which reads as StructValue, not Int32."));
    }

    // Position has 3 FloatProperty members right after each other, so it reads as 1 float array and still steps into Y.
    [Test]
    public void Read_ThreeFloatMembers_ReturnsFloatArray()
    {
        float[] expected = [1f, 2f, 3f];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("Position").Read<float[]>().Unwrap(), Is.EqualTo(expected));
            Assert.That(Data.Member("Position").Member("Y").Read<float>().Unwrap(), Is.EqualTo(2f));
        }
    }

    // DoublePosition is a Rotator of 3 DoubleProperty members, so the element type comes from the members.
    [Test]
    public void Read_ThreeDoubleMembers_ReturnsDoubleArray()
    {
        double[] expected = [1.5, 2.5, 3.5];

        Assert.That(Data.Member("DoublePosition").Read<double[]>().Unwrap(), Is.EqualTo(expected));
    }

    // Coord has 2 IntProperty members, so it reads as an int array and a float array read fails with both types.
    [Test]
    public void Read_IntStructAsFloatArray_FailsWithBothTypes()
    {
        int[] expected = [FakeGame.CoordX, FakeGame.CoordY];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("Coord").Read<int[]>().Unwrap(), Is.EqualTo(expected));
            Assert.That(
                Data.Member("Coord").Read<float[]>().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Coord. Coord is StructProperty, which reads as Int32[], not Single[]."));
        }
    }

    // Position is a Vector of 3 floats, so it also reads as a Vector with the floats widened to doubles.
    [Test]
    public void Read_FloatVectorAsVector_WidensToDoubles()
    {
        Assert.That(Data.Member("Position").Read<Vector>().Unwrap(), Is.EqualTo(new Vector(1, 2, 3)));
    }

    // A Vector prints its components with the invariant culture, so a log line reads the same in every culture.
    [Test]
    public void ToString_Vector_UsesTheInvariantCulture()
    {
        Assert.That(new Vector(1.25, -2.5, 3.75).ToString(), Is.EqualTo("(1.25, -2.5, 3.75)"));
    }

    // A Vector watcher keeps its value while the struct doesn't change and reports the new value when a member changes.
    [Test]
    public void CreateWatcher_Vector_ChangesWithAMember()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member("Position").Value<Vector>().Unwrap().CreateWatcher(ticks);
        _ = watcher.Current;
        ticks.Value++;
        bool changedWhileSteady = watcher.Changed;

        _fakeGame.Memory.Write(_fakeGame.Data + FakeGame.PositionOffset + 4, 5f);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changedWhileSteady, Is.False);
            Assert.That(watcher.Changed, Is.True);
            Assert.That(watcher.Current, Is.EqualTo(new Vector(1, 5, 3)));
        }
    }

    // DoublePosition is a Rotator of doubles, so it reads as a Rotator with its members in order.
    [Test]
    public void Read_DoubleRotatorAsRotator_ReadsTheDoubles()
    {
        Assert.That(Data.Member("DoublePosition").Read<Rotator>().Unwrap(), Is.EqualTo(new Rotator(1.5, 2.5, 3.5)));
    }

    // A Rotator isn't a Vector, Coord has ints and Position is a Vector, so none of them reads as the other type.
    [Test]
    public void Read_OtherNumberStructsAsVector_FailWithTheirTypes()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Data.Member("DoublePosition").Read<Vector>().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.DoublePosition. DoublePosition is StructProperty, which reads as Double[], not Vector."));
            Assert.That(
                Data.Member("Coord").Read<Vector>().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Coord. Coord is StructProperty, which reads as Int32[], not Vector."));
            Assert.That(
                Data.Member("Position").Read<Rotator>().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Position. Position is StructProperty, which reads as Single[], not Rotator."));
        }
    }

    // A struct that doesn't change returns the array of the watcher before, so the watcher doesn't change or allocate a new array.
    [Test]
    public void CreateWatcher_SameVectorTwice_KeepsTheArray()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member("Position").Value<float[]>().Unwrap().CreateWatcher(ticks);
        float[]? first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // Position is a number struct, so its boxed value is a StructValue with the member names in offset order and not a float array.
    [Test]
    public void ReadBoxed_NumberStruct_GivesAStructValueWithTheMemberNames()
    {
        var value = Data.Member("Position").Unwrap().CreateWatcher(new ManualTicks()).Current;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.TypeOf<StructValue>());
            Assert.That(((StructValue)value!).TypeName, Is.EqualTo("Vector"));
            Assert.That(((StructValue)value).Names, Is.EqualTo(["X", "Y", "Z"]));
            Assert.That(((StructValue)value)["Z"], Is.EqualTo(3f));
        }
    }

    // Mixed has an int and a float, so each member of its boxed value keeps its own type.
    [Test]
    public void ReadBoxed_MixedStruct_GivesEachMemberInItsOwnType()
    {
        var value = (StructValue)Data.Member("Mixed").Unwrap().CreateWatcher(new ManualTicks()).Current!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value["A"], Is.TypeOf<int>().And.EqualTo(1));
            Assert.That(value["B"], Is.TypeOf<float>().And.EqualTo(2f));
        }
    }

    // A struct that doesn't change returns the value of the tick before without allocating and 1 changed member gives a new value.
    [Test]
    public void ReadBoxed_UnchangedStruct_ReturnsTheLastValue()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member("Position").Unwrap().CreateWatcher(ticks);
        object? first = watcher.Current;
        ticks.Value++;
        _ = watcher.Current;

        long before = GC.GetAllocatedBytesForCurrentThread();
        ticks.Value++;
        object? same = watcher.Current;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        _fakeGame.Memory.Write(_fakeGame.Data + FakeGame.PositionOffset + 4, 5f);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(same, Is.SameAs(first));
            Assert.That(bytes, Is.Zero);
            Assert.That(watcher.Changed, Is.True);
            Assert.That(((StructValue)watcher.Current!)["Y"], Is.EqualTo(5f));
            Assert.That(((StructValue)watcher.Old!)["Y"], Is.EqualTo(2f));
        }
    }

    // B becomes a TextProperty, so the struct value fails with B and a step to A still reads.
    [Test]
    public void ReadBoxed_AMemberOfAKindTheLibraryDoesNotRead_FailsWithThatMember()
    {
        int classOffset = shape == "ffield" ? FieldClassOffset : FakeGame.ObjectLayout.Class;
        var textClass = _fakeGame.Memory.ReadPointer(_fakeGame.PropertyAddress("TextValue") + classOffset).Unwrap();
        _fakeGame.Memory.WritePointer(_fakeGame.PropertyAddress("B") + classOffset, textClass);
        var watcher = Data.Member("Mixed").Unwrap().CreateWatcher(new ManualTicks());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.Null);
            Assert.That(
                watcher.Error?.Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Mixed. B is TextProperty, which the library doesn't read."));
            Assert.That(watcher.Error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
            Assert.That(Data.Member("Mixed").Member("A").Read<int>().Unwrap(), Is.EqualTo(1));
        }
    }

    // A number struct reads as a StructValue and as an array. An int can't read as a StructValue.
    [Test]
    public void Value_StructValueAndArray_BothReadANumberStruct()
    {
        float[] expected = [1f, 2f, 3f];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Data.Member("Position").Read<StructValue>().Unwrap(),
                Is.EqualTo(new StructValue("Vector", ["X", "Y", "Z"], [1f, 2f, 3f])));
            Assert.That(Data.Member("Position").Read<float[]>().Unwrap(), Is.EqualTo(expected));
            Assert.That(
                Data.Member("Hp").Value<StructValue>().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Hp. Hp is IntProperty, which reads as Int32, not StructValue."));
        }
    }

    // Z sits 4 bytes past the end of Y, so the struct doesn't read as an array, only as a StructValue.
    [Test]
    public void Value_PaddingBetweenMembers_DoesNotReadAsArray()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.PropertyAddress("VectorZ") + FakeGame.PropertyOffsetOffset, 0xC);

        Assert.That(
            _fakeGame.Attach().GWorld.Path.Member("GameMode").Member("Data").Member("Position").Value<float[]>().UnwrapErr().Message,
            Does.EndWith("Position is StructProperty, which reads as StructValue, not Single[]."));
    }

    // Z is a static array of 2 floats, so the struct doesn't read as an array that would drop the 2nd one, only as a StructValue.
    [Test]
    public void Value_StaticArrayMember_DoesNotReadAsArray()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.PropertyAddress("VectorZ") + FakeGame.PropertyArrayDimOffset, 2);

        Assert.That(
            Data.Member("Position").Value<float[]>().UnwrapErr().Message,
            Does.EndWith("Position is StructProperty, which reads as StructValue, not Single[]."));
    }

    // Hp is an IntProperty, so a member after it fails at Hp with its class.
    [Test]
    public void Read_MemberAfterValue_FailsWithNotContainer()
    {
        Assert.That(
            Data.Member("Hp").Member("X").Read<int>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.X at GWorld.GameMode.Data.Hp. Hp is IntProperty, which doesn't have members."));
    }

    // A struct pointer of 0 fails at the struct segment with the property and its address.
    [Test]
    public void Read_StructTypePointerNull_Fails()
    {
        var coord = _fakeGame.PropertyAddress("Coord");
        _fakeGame.Memory.WritePointer(coord + FakeGame.StructTypeOffset, Address.Zero);

        Assert.That(
            Data.Member("Coord").Member("X").Read<int>().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data.Coord.X at GWorld.GameMode.Data.Coord. The struct pointer of Coord at {coord} is 0. "
                + "Check Struct of StructProperty in the profile (0x70)."));
    }

    // A member that the struct doesn't have fails with the struct type and the member.
    [Test]
    public void Read_MemberNotOnStruct_FailsWithTheStructType()
    {
        Assert.That(
            Data.Member("Coord").Member("Z").Read<int>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Coord.Z. Class FixtureCoord doesn't have a property Z."));
    }

    // A direct read of a struct path boxes the struct as a StructValue.
    [Test]
    public void Read_PathToStruct_ReadsAStructValue()
    {
        var coord = (StructValue)Data.Member("Coord").Read().Unwrap()!;

        Assert.That((coord["X"], coord["Y"]), Is.EqualTo((FakeGame.CoordX, FakeGame.CoordY)));
    }

    // A document without the struct offset still attaches and only a step into a struct fails with the missing path.
    [Test]
    public void Read_StructWithoutStructOffset_FailsWithProfilePath()
    {
        string structType = shape == "ffield" ? "FStructProperty" : "UStructProperty";
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", structType));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("Coord").Member("X").Read<int>().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.Coord.X at GWorld.GameMode.Data.Coord. "
                + $"Can't read StructProperty: the profile doesn't have offsets.{structType}.Struct."));
    }
}
