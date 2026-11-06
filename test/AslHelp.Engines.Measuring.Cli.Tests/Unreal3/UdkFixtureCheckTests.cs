using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UdkFixtureCheckTests(
    FakeLayout layout)
{
    private const int FreeValueAt = 0xE0;

    private static readonly TestFixtureData[] _fixtures =
    [
        new TestFixtureData(FakeLayout.Expected).SetArgDisplayNames("Expected"),
        new TestFixtureData(FakeLayout.Shifted).SetArgDisplayNames("Shifted"),
    ];

    private static readonly Address _shortFieldBlock = new(0x0600_0000);
    private static readonly IReadOnlyDictionary<string, UdkTarget> _targets = UdkTargets.ReadEmbedded();

    private FakeImage _image = null!;

    private static IReadOnlyList<string> FirstNames => _targets.Values.OfType<UdkNameArrayTarget>().Single().FirstNames;

    private Address FixtureClass => _image.ClassCalled(UdkFixtureCheck.ClassName);

    private Address FixtureField => _image.Memory.ReadPointer(FixtureClass + layout.Children).UnwrapOr(Address.Zero);

    // The filler sits in front of the classes, so a test can give it any role at the start of the object array.
    private Address Filler => _image.Memory.ReadPointer(FakeImage.ObjectPointerAt(FakeImage.FirstFillerPosition)).UnwrapOr(Address.Zero);

    [SetUp]
    public void SetUp()
    {
        _image = new FakeImage(layout, FirstNames, fillers: 1);
    }

    // The rule is about the class and the Default__ prefix, so an object of the class under any other name counts.
    [Test]
    public void Measure_InstanceUnderAnotherName_ReadsIt()
    {
        MakeFillerAnInstance();
        _image.ClearFixtureInstance();

        Assert.That(MeasureAndLog(), Does.Contain($"at {Filler}"));
    }

    // The check reads the prefix and not the whole name, so it skips a class default object of any other name too.
    [Test]
    public void Measure_OnlyInstanceHasTheDefaultPrefix_ThrowsInstanceNotFound()
    {
        MakeFillerAnInstance();
        _image.RenameEntry(NameIdOf(Filler), "Default__Other");
        _image.ClearFixtureInstance();

        Assert.That(Measure, Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FixtureInstanceNotFound));
    }

    // A property has the name of the class in the game, so the class has to come from the class pointer.
    [Test]
    public void Measure_PropertyWithTheNameOfTheClassInFront_ReadsTheInstance()
    {
        _image.Memory.WriteInt32(Filler + layout.Name, NameIdOf(FixtureClass));
        _image.Memory.WritePointer(Filler + layout.Class, _image.ClassCalled("IntProperty"));

        Assert.That(MeasureAndLog(), Does.Contain($"at {_image.FixtureInstance}"));
    }

    // The game spawns the instance under the class name, so the search does not read the class of any other object.
    [Test]
    public void Measure_InstanceWithTheClassName_ReadsNoClassOfOtherObjects()
    {
        // Use a filler behind the 512 sampled objects, because the search for the name offset reads every sampled object at each offset.
        _image = new FakeImage(layout, FirstNames, fillers: 600);

        var filler = _image.Memory.ReadPointer(FakeImage.ObjectPointerAt(FakeImage.FirstFillerPosition + 599)).UnwrapOr(Address.Zero);

        _ = Measure();

        Assert.That(_image.Memory.ReadsAt(filler + layout.Class), Is.Zero);
    }

    // The object array has no fixed order, so an instance in front of its class still counts.
    [Test]
    public void Measure_InstanceInFrontOfItsClass_ReadsIt()
    {
        MakeFillerAnInstance();
        _image.Memory.WriteInt32(Filler + layout.Name, NameIdOf(FixtureClass));
        _image.ClearFixtureInstance();

        Assert.That(MeasureAndLog(), Does.Contain($"at {Filler}"));
    }

    // A class without its name in the table has no candidates, which fails on the class and not on the instance.
    [Test]
    public void Measure_NoObjectWithTheClassName_ThrowsClassNotFound()
    {
        _image.RenameEntry(NameIdOf(FixtureClass), "OtherData");

        Assert.That(
            Measure,
            Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FormatClassNotFound(UdkFixtureCheck.ClassName)));
    }

    // The class and the field read the same as UTF-16, because the name table reads the wide bit of each entry.
    [Test]
    public void Measure_WideNames_ReadsTheValue()
    {
        _image.MakeNameWide(NameIdOf(FixtureClass));
        _image.MakeNameWide(NameIdOf(FixtureField));

        Assert.That(MeasureAndLog(), Does.Contain($"at {_image.FixtureInstance}"));
    }

    // A real class has many fields, so the search has to go on past a field with another name.
    [Test]
    public void Measure_FieldBehindAnotherField_ReadsTheValue()
    {
        _image.Memory.WritePointer(Filler + layout.Outer, FixtureClass);
        _image.Memory.WritePointer(Filler + layout.Next, FixtureField);
        _image.Memory.WritePointer(FixtureClass + layout.Children, Filler);

        Assert.That(MeasureAndLog(), Does.Contain($"at {_image.FixtureInstance}"));
    }

    // A field of another struct ends the chain, so the field behind it does not count.
    [Test]
    public void Measure_FieldBehindAFieldOfAnotherStruct_ThrowsFieldNotFound()
    {
        _image.Memory.WritePointer(Filler + layout.Next, FixtureField);
        _image.Memory.WritePointer(FixtureClass + layout.Children, Filler);

        Assert.That(Measure, Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FixtureFieldNotFound));
    }

    // A chain that loops ends at the limit of the reader, so a damaged class fails the row and does not hang it.
    [Test]
    public void Measure_FieldChainLoops_ThrowsFieldNotFound()
    {
        _image.Memory.WritePointer(Filler + layout.Outer, FixtureClass);
        _image.Memory.WritePointer(Filler + layout.Next, Filler);
        _image.Memory.WritePointer(FixtureClass + layout.Children, Filler);

        Assert.That(Measure, Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FixtureFieldNotFound));
    }

    // The offset comes from the field, so a value at another offset passes and the old position does not matter.
    [Test]
    public void Measure_ValueAtAnotherOffset_ReadsThroughTheField()
    {
        _image.Memory.WriteInt32(FixtureField + layout.Offset, FreeValueAt);
        _image.Memory.WriteInt32(_image.FixtureInstance + FixtureValueOffset(), 0);
        _image.Memory.WriteInt32(_image.FixtureInstance + FreeValueAt, UdkFixtureCheck.ExpectedValue);

        Assert.That(MeasureAndLog(), Does.Contain($"at {_image.FixtureInstance}"));
    }

    // A field that ends before its offset fails with the address of the offset, because no value can be read without it.
    [Test]
    public void Measure_FieldOffsetUnreadable_ThrowsWithTheAddress()
    {
        _image.Memory.Add(_shortFieldBlock, new byte[layout.Offset]);
        _image.Memory.WritePointer(_shortFieldBlock + layout.Outer, FixtureClass);
        _image.Memory.WriteInt32(_shortFieldBlock + layout.Name, NameIdOf(FixtureField));
        _image.Memory.WritePointer(FixtureClass + layout.Children, _shortFieldBlock);

        Assert.That(
            Measure,
            Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FormatFixtureOffsetUnreadable(_shortFieldBlock + layout.Offset)));
    }

    // An offset read from the wrong place can be any int, which has to fail as a value that does not read and not with another exception.
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void Measure_FieldOffsetOutOfTheObject_ThrowsWithTheAddress(int offset)
    {
        _image.Memory.WriteInt32(FixtureField + layout.Offset, offset);

        Assert.That(
            Measure,
            Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FormatFixtureValueUnreadable(_image.FixtureInstance + offset)));
    }

    // An object that does not read has no name, so it does not count as the instance.
    [Test]
    public void Measure_InstanceUnreadable_ThrowsInstanceNotFound()
    {
        // Put the instance behind the 512 sampled objects, because 1 sampled object that does not read rejects the array.
        _image = new FakeImage(layout, FirstNames, fillers: 600);

        int position = _image.Memory.Read<int>(_image.FixtureInstance + layout.Index).UnwrapOr(-1);
        _image.Memory.WritePointer(FakeImage.ObjectPointerAt(position), new Address(0x0700_0000));

        Assert.That(Measure, Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FixtureInstanceNotFound));
    }

    // 1337 logs 1 line and nothing goes to the error log.
    [Test]
    public void Measure_ValueRight_LogsOneLine()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        _ = UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, _targets, new WriterLogger(output, error));

        string[] lines = output.ToString().Split(Environment.NewLine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines.Count(static line => line.Contains(UdkFixtureCheck.FieldName, StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(error.ToString(), Is.Empty);
        }
    }

    // A wrong value logs no read line, so a failed row cannot look like a passed one in the log.
    [Test]
    public void Measure_ValueWrong_LogsNoReadLine()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        _image.Memory.WriteInt32(_image.FixtureInstance + FixtureValueOffset(), 0);

        Assert.That(
            () => UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, _targets, new WriterLogger(output, error)),
            Throws.InvalidOperationException);
        Assert.That(output.ToString(), Does.Not.Contain(UdkFixtureCheck.FieldName));
    }

    // The check does not depend on the targets, so a file with only the arrays fails on a wrong value too.
    [Test]
    public void Measure_TargetsWithOnlyTheArrays_StillChecksTheValue()
    {
        var targets = _targets.Where(static target => target.Value is not UdkObjectSlotTarget).ToDictionary();

        _image.Memory.WriteInt32(_image.FixtureInstance + FixtureValueOffset(), 0);

        Assert.That(
            () => UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, targets, NullLogger.Instance),
            Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FormatFixtureValueWrong(UdkFixtureCheck.ExpectedValue, 0)));
    }

    // Children and Next are found together, so only a direct call shows that Next alone fails the check.
    [Test]
    public void Run_NextMissing_ThrowsFixtureCheckNeedsNext()
    {
        var names = UdkArrays.FindNames(_image.Memory, FakeImage.Base, _image.Sections, FirstNames);
        var objects = UdkArrays.FindObjects(_image.Memory, FakeImage.Base, _image.Sections, names.Rvas);

        Dictionary<string, int> offsets = [];
        Dictionary<string, string> unmeasured = [];

        var measured = UdkMembers.Measure(_image.Memory, names, objects, offsets, unmeasured);
        _ = offsets.Remove(UdkMembers.Next);

        Assert.That(
            () => UdkFixtureCheck.Run(_image.Memory, measured, offsets, NullLogger.Instance),
            Throws.InvalidOperationException.With.Message.EqualTo(UdkSR.FormatFixtureCheckNeeds(UdkMembers.Next)));
    }

    // The instance sits past the 1st block of the array and the fixture check does not read its name again.
    [Test]
    public void Measure_InstancePastTheFirstBlock_ReadsItsNameOnce()
    {
        _image = new FakeImage(layout, FirstNames, fillers: 1100);

        string log = MeasureAndLog();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log, Does.Contain($"at {_image.FixtureInstance}"));
            Assert.That(_image.Memory.ReadsAt(_image.FixtureInstance + layout.Name), Is.EqualTo(1));
        }
    }

    private int FixtureValueOffset()
    {
        return _image.Memory.Read<int>(FixtureField + layout.Offset).UnwrapOr(-1);
    }

    private void MakeFillerAnInstance()
    {
        _image.Memory.WritePointer(Filler + layout.Class, FixtureClass);
        _image.Memory.WriteInt32(Filler + FixtureValueOffset(), UdkFixtureCheck.ExpectedValue);
    }

    private PlayerMeasurement Measure()
    {
        return UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, _targets, NullLogger.Instance);
    }

    private string MeasureAndLog()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        _ = UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, _targets, new WriterLogger(output, error));

        return output.ToString();
    }

    private int NameIdOf(Address obj)
    {
        return _image.Memory.Read<int>(obj + layout.Name).UnwrapOr(-1);
    }
}
