using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UdkFailureTests(
    FakeLayout layout)
{
    private const string Engine = "GEngine";
    private const string World = "GWorld";

    private static readonly TestFixtureData[] _fixtures =
    [
        new TestFixtureData(FakeLayout.Expected).SetArgDisplayNames("Expected"),
        new TestFixtureData(FakeLayout.Shifted).SetArgDisplayNames("Shifted"),
    ];

    private static readonly IReadOnlyDictionary<string, UdkTarget> _targets = UdkTargets.ReadEmbedded().Unwrap();
    private static readonly Address _unreadable = new(0x0700_0000);

    private FakeImage _image = null!;

    private static IReadOnlyList<string> FirstNames => _targets.Values.OfType<UdkNameArrayTarget>().Single().FirstNames;

    private Address FixtureClass => _image.ClassCalled(UdkFixtureCheck.ClassName);

    private Address FixtureField => _image.Memory.ReadPointer(FixtureClass + layout.Children).Unwrap();

    private Address Filler => _image.Memory.ReadPointer(FakeImage.ObjectPointerAt(FakeImage.FirstFillerPosition)).Unwrap();

    [SetUp]
    public void SetUp()
    {
        _image = new FakeImage(layout, FirstNames, fillers: 1);
    }

    // The only field of the class does not read its name, so the row has to show that failure and not a class without the field.
    [Test]
    public void Measure_FieldNameUnreadable_FailsWithTheCause()
    {
        _image.Memory.WriteInt32(FixtureField + layout.Name + sizeof(int), -1);

        Assert.That(
            Measure().UnwrapErr().Message,
            Does.StartWith(UdkSR.FixtureFieldNameUnreadable)
                .And.Contain($"The object at {FixtureField} has the name number -1")
                .And.Contain($"ObjectLayout.Name (0x{layout.Name:X})"));
    }

    // A field whose name does not read does not stop the search, because the field behind it can still be IntValue.
    [Test]
    public void Measure_FieldBehindAFieldWithAnUnreadableName_ReadsTheValue()
    {
        var field = FixtureField;

        _image.Memory.WriteInt32(Filler + layout.Name + sizeof(int), -1);
        _image.Memory.WritePointer(Filler + layout.Outer, FixtureClass);
        _image.Memory.WritePointer(Filler + layout.Next, field);
        _image.Memory.WritePointer(FixtureClass + layout.Children, Filler);

        Assert.That(Measure().IsOk, Is.True);
    }

    // The search stops at the field, so a next pointer into memory that does not read behind it does not fail the row.
    [Test]
    public void Measure_BrokenChainBehindTheField_ReadsTheValue()
    {
        _image.Memory.WritePointer(FixtureField + layout.Next, _unreadable);

        Assert.That(Measure().IsOk, Is.True);
    }

    // The address in the message is the hex form of the address and not a decimal number, because a person compares it with a memory view.
    [Test]
    public void Measure_FixtureValueUnreadable_WritesTheAddressInHex()
    {
        var address = _image.MoveFixtureValueOutOfTheObject();

        Assert.That(Measure().UnwrapErr().Message, Does.StartWith($"IntValue of FixtureData at 0x{address.Value:X} does not read."));
    }

    // A failed read on the super chain is a miss for that slot, so the row stays measured and the other slot is still found.
    [Test]
    public void Measure_SlotClassWithUnreadableName_ReportsOnlyThatSlotUnmeasured()
    {
        _image.Memory.WriteInt32(_image.ClassCalled("GameEngine") + layout.Name + sizeof(int), -1);

        var measurement = Measure().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured[Engine], Is.EqualTo(UdkSR.FormatUnmeasuredSlot("Engine")));
            Assert.That(measurement.Globals, Does.ContainKey(World));
        }
    }

    // A chain that fails behind the 5 fields keeps those fields, because a miss is normal while the search tries each next offset.
    [Test]
    public void Measure_ChildrenEndInUnreadableField_KeepsTheFieldsBefore()
    {
        _image.Memory.WritePointer(_image.PropertyCalled("Linker") + layout.Next, _unreadable);

        var measurement = Measure().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Is.EquivalentTo(layout.ToOffsets()));
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // The readers start with -1 for the offsets not found yet and a read 1 byte in front of an object succeeds on a real process.
    [Test]
    public void Measure_OffsetsNotFoundYet_ReadsNothingAtMinusOne()
    {
        _ = Measure().Unwrap();

        Address[] objects =
        [
            .. ((string[])["Class", "State", "Struct", "Field", "Object"]).Select(_image.ClassCalled),
            .. ((string[])["Outer", "Name", "Class"]).Select(_image.PropertyCalled),
            FixtureField,
            _image.FixtureInstance,
        ];

        Assert.That(objects.Select(obj => _image.Memory.ReadsAt(obj + -1)), Is.All.Zero);
    }

    // An executable without a section that reads leaves nothing to search, so the row fails on the name array.
    [Test]
    public void Measure_NoSections_FailsNameArrayNotFound()
    {
        Assert.That(
            UdkMeasurement.Measure(_image.Memory, FakeImage.Base, [], _targets, "file.bin", NullLogger.Instance).UnwrapErr().Message,
            Is.EqualTo(UdkSR.FormatNameArrayNotFound(FirstNames[0], FirstNames[1])));
    }

    private Result<PlayerMeasurement> Measure()
    {
        return UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, _targets, "file.bin", NullLogger.Instance);
    }
}
