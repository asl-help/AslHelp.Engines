using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UdkMeasurementTests(
    FakeLayout layout)
{
    private const string Engine = "GEngine";
    private const string Names = "GNames";
    private const string Objects = "GObjObjects";
    private const string World = "GWorld";

    private static readonly TestFixtureData[] _fixtures =
    [
        new TestFixtureData(FakeLayout.Expected).SetArgDisplayNames("Expected"),
        new TestFixtureData(FakeLayout.Shifted).SetArgDisplayNames("Shifted"),
    ];

    private static readonly IReadOnlyDictionary<string, UdkTarget> _targets = UdkTargets.ReadEmbedded().Unwrap();

    private FakeImage _image = null!;

    private static IReadOnlyList<string> FirstNames => _targets.Values.OfType<UdkNameArrayTarget>().Single().FirstNames;

    [SetUp]
    public void SetUp()
    {
        _image = new FakeImage(layout, FirstNames);
    }

    // The path of the executable comes back unchanged, because the route search reads the file after the player ended.
    [Test]
    public void Measure_FakeImage_ReturnsTheExecutablePath()
    {
        Assert.That(Measure().ExecutablePath, Is.EqualTo("file.bin"));
    }

    // Every member comes back at the offset of the build, in a build whose offsets all differ from the expected table too.
    [Test]
    public void Measure_FakeImage_FindsEveryOffset()
    {
        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Is.EquivalentTo(layout.ToOffsets()));
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // The arrays are the RVAs of their headers, GEngine is the slot of the engine object and GWorld is the slot of TheWorld and not of OtherWorld.
    [Test]
    public void Measure_FakeImage_FindsEveryGlobal()
    {
        var globals = Measure().Globals;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(globals[Names], Is.EqualTo(Rvas(FakeImage.NameArrayRva)));
            Assert.That(globals[Objects], Is.EqualTo(Rvas(FakeImage.ObjectArrayRva)));
            Assert.That(globals[Engine], Is.EqualTo(Rvas(_image.EngineSlotRva)));
            Assert.That(globals[World], Is.EqualTo(Rvas(_image.WorldSlotRva)));
        }
    }

    // A global is a list of RVAs in RVA order, because 2 statics can point at 1 object and only the code decides which is the global.
    [Test]
    public void Measure_TwoSlotsOnOneObject_ListsBoth()
    {
        uint second = _image.AddSlot(_image.Engine);

        Assert.That(Measure().Globals[Engine], Is.EqualTo(Rvas(_image.EngineSlotRva, second)));
    }

    // The decoy section comes before the arrays, so an array whose pointers read no index has to be skipped.
    [Test]
    public void Measure_DecoyArrayBeforeTheObjectArray_SkipsIt()
    {
        Assert.That(Measure().Globals[Objects], Is.EqualTo(Rvas(FakeImage.ObjectArrayRva)));
    }

    // The measurement reads the object array once and the name of each object once, because a real player has about 100k objects.
    [Test]
    public void Measure_ArrayLongerThanABlock_ReadsItAndItsNamesOnce()
    {
        FakeImage image = new(layout, FirstNames, fillers: 1100);

        _ = Measure(image).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Memory.ReadsAt(FakeImage.ObjectPointerAt(1024)), Is.EqualTo(1));
            Assert.That(image.Memory.ReadsAt(image.Engine + layout.Name), Is.EqualTo(1));
        }
    }

    // Nothing else can be measured without the name array, so the message lists the 2 names the search starts from.
    [Test]
    public void Measure_NameArrayCleared_FailsNameArrayNotFound()
    {
        _image.ClearNameArray();

        Assert.That(MeasureError(), Does.Contain(FirstNames[0]).And.Contain(FirstNames[1]));
    }

    // 2 headers with different data fail with both RVAs, because picking the 1st can pick a stale copy.
    [Test]
    public void Measure_TwoNameArraysWithDifferentData_FailsAmbiguousWithBothRvas()
    {
        _image.DuplicateNameArray();

        Assert.That(MeasureError(), Does.Contain($"0x{FakeImage.ExtraHeaderRva:X}").And.Contain($"0x{FakeImage.NameArrayRva:X}"));
    }

    // 2 headers with 1 data pointer are 1 array with 2 RVAs and neither header counts as the object array.
    [Test]
    public void Measure_TwoHeadersOnOneNameArray_ListsBothRvas()
    {
        _image.AliasNameArray();

        var globals = Measure().Globals;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(globals[Names], Is.EqualTo(Rvas(FakeImage.NameArrayRva, FakeImage.ExtraHeaderRva)).AsCollection);
            Assert.That(globals[Objects], Is.EqualTo(Rvas(FakeImage.ObjectArrayRva)));
        }
    }

    // An array shorter than the list of first names gets skipped, so the failure is about the array and not about a name in it.
    [Test]
    public void Measure_NameArrayShorterThanFirstNames_FailsNameArrayNotFound()
    {
        string[] firstNames = [.. FirstNames, "Extra0", "Extra1", "Extra2", "Extra3"];
        FakeImage image = new(layout, firstNames);
        image.SetNameArrayCount(firstNames.Length - 1);
        Dictionary<string, UdkTarget> targets = new() { [Names] = new UdkNameArrayTarget(firstNames), [Objects] = new UdkObjectArrayTarget() };

        Assert.That(
            UdkMeasurement.Measure(image.Memory, FakeImage.Base, image.Sections, targets, "file.bin", NullLogger.Instance).UnwrapErr().Message,
            Is.EqualTo(UdkSR.FormatNameArrayNotFound(firstNames[0], firstNames[1])));
    }

    // A first name that does not read fails with the RVA, its id and the failed read and not as a wrong name.
    [Test]
    public void Measure_ThirdNameUnreadable_FailsWithIdAndCause()
    {
        _image.ClearEntryPointer(2);

        Assert.That(
            MeasureError(),
            Is.EqualTo($"The name array at RVA 0x{FakeImage.NameArrayRva:X} does not read name 2. The entry pointer of name 2 is 0."));
    }

    // Every first name gets checked and not only the 2 the search starts from.
    [Test]
    public void Measure_ThirdNameWrong_FailsWithTheName()
    {
        _image.RenameEntry(2, "Wrong");

        Assert.That(MeasureError(), Does.Contain("Wrong").And.Contain(FirstNames[2]));
    }

    // A name array alone measures nothing, so a missing object array fails the row.
    [Test]
    public void Measure_ObjectArrayCleared_FailsObjectArrayNotFound()
    {
        _image.ClearObjectArray();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ObjectArrayNotFound));
    }

    // A header that points into an unreadable page gets skipped like any other array that does not fit.
    [Test]
    public void Measure_ObjectArrayDataUnreadable_FailsObjectArrayNotFound()
    {
        _image.MakeObjectArrayDataUnreadable();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ObjectArrayNotFound));
    }

    // An array whose pointers end behind the sample passes the search and then fails with its RVA and the block that did not read.
    [Test]
    public void Measure_ObjectArrayUnreadablePastSample_FailsWithRvaAndCause()
    {
        FakeImage image = new(layout, FirstNames, fillers: 600);
        image.SetObjectArrayCount(1100);

        Assert.That(
            Measure(image).UnwrapErr().Message,
            Does.StartWith($"The object array at RVA 0x{FakeImage.ObjectArrayRva:X} does not read to its end.")
                .And.Contain("from position 0")
                .And.Contain($"No block contains {FakeImage.ObjectPointerAt(0)}."));
    }

    // Objects with no readable name at any offset fail the row, because every later search reads names.
    [Test]
    public void Measure_ObjectsScrambled_FailsNameOffsetNotFound()
    {
        _image.ScrambleObjects();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.NameOffsetNotFound));
    }

    // Without Children the members found through the properties go to unmeasured and the other members stay.
    [Test]
    public void MeasureMembers_ChildrenUnlinked_ReportsChildrenNextAndDependentsUnmeasured()
    {
        _image.ClearChildren();

        var (offsets, unmeasured) = MeasureMembers();

        string[] expected =
        [
            UdkMembers.Children,
            UdkMembers.ElementSize,
            UdkMembers.Next,
            UdkMembers.Offset,
            UdkMembers.PropertyClass,
            UdkMembers.PropertyLinkNext,
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unmeasured.Keys, Is.EquivalentTo(expected));
            Assert.That(unmeasured[UdkMembers.Children], Is.EqualTo(UdkSR.UnmeasuredChildren));
            Assert.That(unmeasured[UdkMembers.Offset], Is.EqualTo(UdkSR.FormatUnmeasuredNeeds(UdkMembers.Children)));
            Assert.That(offsets[UdkMembers.SuperField], Is.EqualTo(layout.Super));
            Assert.That(offsets[UdkMembers.EnumNames], Is.EqualTo(layout.FieldSize));
        }
    }

    // 1337 goes to the log with the address it came from, so a run shows which object passed.
    [Test]
    public void Measure_FixtureValueRight_LogsIt()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        _ = UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, _targets, "file.bin", new WriterLogger(output, error)).Unwrap();

        Assert.That(output.ToString(), Does.Contain($"FixtureData.IntValue: 1337 at {_image.FixtureInstance}"));
    }

    // A wrong value fails the row with both numbers, because a passing array and member search can still have the wrong offset.
    [Test]
    public void Measure_FixtureValueWrong_FailsWithBothValues()
    {
        _image.Memory.WriteInt32(_image.FixtureInstance + FakeImage.FixtureValueAt, 42);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FormatFixtureValueWrong(UdkFixtureCheck.ExpectedValue, 42)));
    }

    // A class with no object at all fails on the instance and not on the field or the value.
    [Test]
    public void Measure_NoFixtureInstance_FailsInstanceNotFound()
    {
        _image.ClearFixtureInstance();
        _image.ClearFixtureDefault();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FixtureInstanceNotFound));
    }

    // The class default object has the class too, but its value is the default and not what the game set.
    [Test]
    public void Measure_OnlyDefaultFixtureObject_FailsInstanceNotFound()
    {
        _image.ClearFixtureInstance();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FixtureInstanceNotFound));
    }

    // A class without the field fails on the field while the instance is still there.
    [Test]
    public void Measure_FixtureClassWithoutField_FailsFieldNotFound()
    {
        _image.ClearFixtureChildren();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FixtureFieldNotFound));
    }

    // A value behind the end of the readable memory fails with the address and the failed read and does not count as a wrong value.
    [Test]
    public void Measure_FixtureValueUnreadable_FailsWithAddressAndCause()
    {
        var address = _image.MoveFixtureValueOutOfTheObject();

        Assert.That(MeasureError(), Is.EqualTo($"{UdkSR.FormatFixtureValueUnreadable(address)} No block contains {address}."));
    }

    // A next pointer of the fixture class into unreadable memory fails with the next field and its outer offset and not as a missing field.
    [Test]
    public void Measure_FixtureFieldChainUnreadable_FailsWithCause()
    {
        var fixtureClass = _image.ClassCalled(UdkFixtureCheck.ClassName);
        var field = _image.Memory.ReadPointer(fixtureClass + layout.Children).Unwrap();
        int name = _image.Memory.Read<int>(_image.PropertyCalled("Linker") + layout.Name).Unwrap();

        // Rename the field, so the search goes on to the next pointer.
        _image.Memory.WriteInt32(field + layout.Name, name);
        _image.Memory.WritePointer(field + layout.Next, new Address(0x0700_0000));

        Assert.That(
            MeasureError(),
            Does.StartWith(UdkSR.FixtureChildrenUnreadable)
                .And.Contain("Can't read the outer object of the object at 0x7000000")
                .And.Contain($"ObjectLayout.Outer (0x{layout.Outer:X})"));
    }

    // Children goes to unmeasured, so the row fails because the value cannot be read without it.
    [Test]
    public void Measure_ChildrenUnlinked_FailsFixtureCheckNeedsChildren()
    {
        _image.ClearChildren();

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FormatFixtureCheckNeeds(UdkMembers.Children)));
    }

    // Only Offset goes to unmeasured here, so the failure is about Offset and not about Children.
    [Test]
    public void Measure_WrongPropertyOffset_FailsFixtureCheckNeedsOffset()
    {
        _image.Memory.WriteInt32(_image.PropertyCalled("Outer") + layout.Offset, 0);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FormatFixtureCheckNeeds(UdkMembers.Offset)));
    }

    // A player built without the fixture package fails on the class and not on a missing instance.
    [Test]
    public void Measure_FixtureClassMissing_FailsClassNotFound()
    {
        _image.Memory.WritePointer(_image.ClassCalled(UdkFixtureCheck.ClassName) + layout.Class, Address.Zero);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FormatClassNotFound(UdkFixtureCheck.ClassName)));
    }

    // The slots derive from their class through the super chain, so without SuperField both go to unmeasured with that reason.
    [Test]
    public void Measure_SuperChainBroken_ReportsSuperFieldAndSlotsUnmeasured()
    {
        _image.ClearSuperOfStruct();

        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured[UdkMembers.SuperField], Is.EqualTo(UdkSR.UnmeasuredSuperField));
            Assert.That(measurement.Unmeasured[Engine], Is.EqualTo(UdkSR.FormatUnmeasuredNeeds(UdkMembers.SuperField)));
            Assert.That(measurement.Unmeasured[World], Is.EqualTo(UdkSR.FormatUnmeasuredNeeds(UdkMembers.SuperField)));
            Assert.That(measurement.Globals, Does.ContainKey(Names).And.ContainKey(Objects).And.Not.ContainKey(Engine));
        }
    }

    // A global with no slot goes to unmeasured, because an empty list reads like a value that was looked up and doesn't have an address.
    [Test]
    public void Measure_SlotsCleared_ReportsSlotsUnmeasured()
    {
        _image.ClearSlots();

        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Globals, Does.Not.ContainKey(Engine));
            Assert.That(measurement.Unmeasured[Engine], Is.EqualTo(UdkSR.FormatUnmeasuredSlot("Engine")));
            Assert.That(measurement.Unmeasured[World], Is.EqualTo(UdkSR.FormatUnmeasuredSlot("World")));
        }
    }

    // A page that did not read is zeros, which hides only the slots on that page.
    [Test]
    public void Measure_SlotBehindUnreadablePage_FindsTheOthers()
    {
        _ = _image.AddSlotOnSecondPage(_image.Engine);
        _image.ClearSecondPage();

        Assert.That(Measure().Globals[Engine], Is.EqualTo(Rvas(_image.EngineSlotRva)));
    }

    // A slot on the 2nd page counts, so the search does not stop at the 1st page.
    [Test]
    public void Measure_SlotOnSecondPage_ListsIt()
    {
        uint second = _image.AddSlotOnSecondPage(_image.Engine);

        Assert.That(Measure().Globals[Engine], Is.EqualTo(Rvas(_image.EngineSlotRva, second)));
    }

    // PropertyClass gets checked through the property Outer, so a build with the pointer elsewhere reports it unmeasured and keeps UEnum.Names.
    [Test]
    public void Measure_PropertyClassNotAtPropertySize_ReportsItUnmeasured()
    {
        _image.ClearPropertyClass();

        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured[UdkMembers.PropertyClass], Is.EqualTo(UdkSR.UnmeasuredPropertyClass));
            Assert.That(measurement.Offsets[UdkMembers.EnumNames], Is.EqualTo(layout.FieldSize));
        }
    }

    // A first name written as UTF-16 with the wide bit set reads back through the name table.
    [Test]
    public void Measure_WideFirstName_StillReads()
    {
        _image.MakeNameWide(2);

        Assert.That(Measure().Globals[Names], Is.EqualTo(Rvas(FakeImage.NameArrayRva)));
    }

    // A file with fewer rows returns fewer globals, because the rows decide which globals to find.
    [Test]
    public void Measure_NoSlotRows_ReturnsOnlyTheArrays()
    {
        var targets = WithoutSlots();

        var measurement = UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, targets, "file.bin", NullLogger.Instance).Unwrap();

        string[] expected = [Names, Objects];

        Assert.That(measurement.Globals.Keys, Is.EquivalentTo(expected));
    }

    // The name ids of the chain grow from Object to Class in this build, which must not read as the size of a class.
    [Test]
    public void Measure_NameIdsGrowAlongTheChain_KeepsPropertySize()
    {
        string[] padding = [.. Enumerable.Range(0, 48).Select(static at => $"Padding{at}")];
        FakeImage image = new(layout, [.. FirstNames, .. padding, "Object", "Field", "Struct", "State", "Class"]);

        Assert.That(Measure(image).Unwrap().Offsets[UdkMembers.PropertySize], Is.EqualTo(layout.Size));
    }

    // The positions of the chain grow from Object to Class in this build, which must not read as the size of a class.
    [Test]
    public void Measure_IndexesGrowAlongTheChain_KeepsPropertySize()
    {
        FakeImage image = new(layout, FirstNames, fillers: 48);
        image.ReverseChainPositions();

        Assert.That(Measure(image).Unwrap().Offsets[UdkMembers.PropertySize], Is.EqualTo(layout.Size));
    }

    // The size of Object has to stay under 0x200, so 0x1FC counts and 0x200 does not.
    [TestCase(0x1FC, true)]
    [TestCase(0x200, false)]
    public void Measure_ObjectSizeAroundLimit_FindsSizeOnlyBelowIt(int objectSize, bool found)
    {
        var sized = layout with
        {
            ObjectSize = objectSize,
            FieldSize = objectSize + 0x10,
            StructSize = objectSize + 0x20,
            StateSize = objectSize + 0x30,
            ClassSize = objectSize + 0x40,
        };

        var measurement = Measure(new FakeImage(sized, FirstNames)).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets.ContainsKey(UdkMembers.PropertySize), Is.EqualTo(found));
            Assert.That(measurement.Unmeasured.ContainsKey(UdkMembers.EnumNames), Is.Not.EqualTo(found));
            Assert.That(measurement.Unmeasured.ContainsKey(UdkMembers.PropertyClass), Is.Not.EqualTo(found));
        }
    }

    // The size of Object has to be above the outer offset, because an object has members behind its outer pointer.
    [Test]
    public void Measure_ObjectSizeEqualsOuterOffset_ReportsPropertySizeUnmeasured()
    {
        var measurement = Measure(new FakeImage(layout with { ObjectSize = layout.Outer }, FirstNames)).Unwrap();

        Assert.That(measurement.Unmeasured[UdkMembers.PropertySize], Is.EqualTo(UdkSR.UnmeasuredPropertySize));
    }

    // 2 classes of equal size break the growth along the chain.
    [Test]
    public void Measure_TwoChainClassesOfEqualSize_ReportsPropertySizeUnmeasured()
    {
        var measurement = Measure(new FakeImage(layout with { StateSize = layout.StructSize }, FirstNames)).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured[UdkMembers.PropertySize], Is.EqualTo(UdkSR.UnmeasuredPropertySize));
            Assert.That(measurement.Unmeasured[UdkMembers.EnumNames], Is.EqualTo(UdkSR.FormatUnmeasuredNeeds(UdkMembers.PropertySize)));
            Assert.That(measurement.Unmeasured[UdkMembers.PropertyClass], Is.EqualTo(UdkSR.FormatUnmeasuredNeeds(UdkMembers.PropertySize)));
        }
    }

    // The last int below 0x100 still counts.
    [Test]
    public void Measure_SizeAtLastOffsetSearched_FindsIt()
    {
        var moved = layout with { Size = 0xFC };

        Assert.That(Measure(new FakeImage(moved, FirstNames)).Unwrap().Offsets, Is.EquivalentTo(moved.ToOffsets()));
    }

    // Both ends of the range of the name array count and the numbers next to them do not.
    [TestCase(15, false)]
    [TestCase(1_000_000, true)]
    [TestCase(1_000_001, false)]
    public void Measure_NameCountAroundRange_FindsOnlyInRange(int count, bool found)
    {
        _image.SetNameArrayCount(count);

        if (found)
        {
            Assert.That(Measure().Globals[Names], Is.EqualTo(Rvas(FakeImage.NameArrayRva)));
        }
        else
        {
            Assert.That(MeasureError(), Does.Contain(FirstNames[0]));
        }
    }

    // A count above the largest object array rejects the header.
    [Test]
    public void Measure_ObjectCountAboveRange_FailsObjectArrayNotFound()
    {
        _image.SetObjectArrayCount(4_000_001);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ObjectArrayNotFound));
    }

    // The largest object array is 16 MB of pointers and every search reads it to the end without a failure.
    [Test]
    public void Measure_ObjectCountAtRangeEnd_FindsEveryOffset()
    {
        _image.GrowObjectArray(4_000_000);

        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Is.EquivalentTo(layout.ToOffsets()));
            Assert.That(measurement.Globals[Engine], Is.EqualTo(Rvas(_image.EngineSlotRva)));
        }
    }

    // 15 objects are too few to trust a match of the index.
    [Test]
    public void Measure_FifteenObjectsInTheSample_FailsObjectArrayNotFound()
    {
        // Keep positions 0 and 2 to 15, because position 1 is empty.
        _image.KeepObjectPositions(16);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ObjectArrayNotFound));
    }

    // 16 objects are enough to trust a match of the index.
    [Test]
    public void FindObjects_SixteenObjectsInTheSample_FindsTheArray()
    {
        _image.KeepObjectPositions(17);

        var names = UdkArrays.FindNames(_image.Memory, FakeImage.Base, _image.Sections, FirstNames).Unwrap();
        var objects = UdkArrays.FindObjects(_image.Memory, FakeImage.Base, _image.Sections, names.Rvas).Unwrap();

        Assert.That(objects.Rvas, Is.EqualTo(Rvas(FakeImage.ObjectArrayRva)));
    }

    // 1 sampled object with another index rejects the array, because the index has to fit every sampled object.
    [Test]
    public void Measure_WrongIndexInsideTheSample_FailsObjectArrayNotFound()
    {
        _image.Memory.WriteInt32(_image.Core + layout.Index, 999);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ObjectArrayNotFound));
    }

    // Only the first 512 positions get sampled, so a wrong index behind them changes nothing.
    [Test]
    public void Measure_WrongIndexBehindTheSample_FindsTheArray()
    {
        FakeImage image = new(layout, FirstNames, fillers: 600);
        image.Memory.WriteInt32(image.Engine + layout.Index, 999);

        Assert.That(Measure(image).Unwrap().Globals[Objects], Is.EqualTo(Rvas(FakeImage.ObjectArrayRva)));
    }

    // A shorter copy of the start of the object array fits the sample, so the longer array wins and only its RVA gets listed.
    [Test]
    public void Measure_ShorterObjectArrayCopy_TakesTheLongerArray()
    {
        _image.DuplicateObjectArray(shorterBy: 1);

        Assert.That(Measure().Globals[Objects], Is.EqualTo(Rvas(FakeImage.ObjectArrayRva)));
    }

    // 2 object arrays of 1 length with different data fail with both RVAs, because nothing tells the copy from the original.
    [Test]
    public void Measure_TwoObjectArraysWithDifferentData_FailsAmbiguousWithBothRvas()
    {
        _image.DuplicateObjectArray();

        Assert.That(MeasureError(), Does.Contain($"0x{FakeImage.ExtraHeaderRva:X}").And.Contain($"0x{FakeImage.ObjectArrayRva:X}"));
    }

    // 2 headers with 1 data pointer are 1 object array with 2 RVAs.
    [Test]
    public void Measure_TwoHeadersOnOneObjectArray_ListsBothRvas()
    {
        _image.AliasObjectArray();

        Assert.That(Measure().Globals[Objects], Is.EqualTo(Rvas(FakeImage.ExtraHeaderRva, FakeImage.ObjectArrayRva)));
    }

    // 1 object in 27 without a name stays above the 95% limit.
    [Test]
    public void Measure_OneObjectWithoutName_FindsNameOffset()
    {
        _image.Memory.WriteInt32(_image.Engine + layout.Name, -1);

        Assert.That(Measure().Offsets[UdkMembers.ObjectName], Is.EqualTo(layout.Name));
    }

    // 2 objects in 27 without a name fall under the 95% limit, so the real offset stops being a candidate.
    [Test]
    public void Measure_TwoObjectsWithoutName_DoesNotUseTheNameOffset()
    {
        _image.Memory.WriteInt32(_image.Engine + layout.Name, -1);
        _image.Memory.WriteInt32(_image.Core + layout.Name, -1);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ClassOffsetNotFound));
    }

    // A first entry without an entry pointer is no name array.
    [TestCase(0)]
    [TestCase(1)]
    public void Measure_EntryPointerCleared_FailsNameArrayNotFound(int id)
    {
        _image.ClearEntryPointer(id);

        Assert.That(MeasureError(), Does.Contain(FirstNames[0]));
    }

    // The 1st 2 entries have to read 0 and 2 at 1 offset, because an entry stores its id shifted left by 1.
    [Test]
    public void Measure_SecondEntryWithAnotherId_FailsNameArrayNotFound()
    {
        _image.Memory.WriteInt32(_image.EntryAt(1) + layout.EntryIndex, 4);

        Assert.That(MeasureError(), Does.Contain(FirstNames[0]));
    }

    // The text has to sit at 1 offset in both entries.
    [Test]
    public void Measure_SecondEntryTextMoved_FailsNameArrayNotFound()
    {
        _image.Memory.WriteBytes(_image.EntryAt(1) + layout.EntryName, Encoding.ASCII.GetBytes($"\0\0\0\0{FirstNames[1]}\0"));

        Assert.That(MeasureError(), Does.Contain(FirstNames[0]));
    }

    // Nothing else can be searched without the class of Class, so the row fails.
    [Test]
    public void Measure_ClassWithoutSelfPointer_FailsClassOffsetNotFound()
    {
        _image.Memory.WritePointer(_image.ClassCalled("Class") + layout.Class, Address.Zero);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.ClassOffsetNotFound));
    }

    // Both Class and Object have to point at Core, so 1 of them without the pointer fails the row.
    [Test]
    public void Measure_ObjectClassNotInCore_FailsOuterOffsetNotFound()
    {
        _image.Memory.WritePointer(_image.ClassCalled("Object") + layout.Outer, Address.Zero);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.OuterOffsetNotFound));
    }

    // A class of the super chain that is not a class fails the row with its name.
    [TestCase("State")]
    [TestCase("Struct")]
    [TestCase("Field")]
    [TestCase("Object")]
    public void Measure_ChainClassMissing_FailsWithItsName(string name)
    {
        _image.Memory.WritePointer(_image.ClassCalled(name) + layout.Class, Address.Zero);

        Assert.That(MeasureError(), Is.EqualTo(UdkSR.FormatClassNotFound(name)));
    }

    // 4 fields are 1 too few, even with Outer, Name and Class among them.
    [Test]
    public void MeasureMembers_FourChildren_ReportsChildrenUnmeasured()
    {
        _image.Memory.WritePointer(_image.PropertyCalled("Class") + layout.Next, Address.Zero);

        Assert.That(MeasureMembers().Unmeasured[UdkMembers.Children], Is.EqualTo(UdkSR.UnmeasuredChildren));
    }

    // 5 fields without the property Class are not the fields of the class Object.
    [Test]
    public void MeasureMembers_ChildrenWithoutClass_ReportsChildrenUnmeasured()
    {
        int linker = _image.Memory.Read<int>(_image.PropertyCalled("Linker") + layout.Name).UnwrapOr(-1);
        _image.Memory.WriteInt32(_image.PropertyCalled("Class") + layout.Name, linker);

        Assert.That(MeasureMembers().Unmeasured[UdkMembers.Children], Is.EqualTo(UdkSR.UnmeasuredChildren));
    }

    // A field whose outer object is another struct ends the chain, which then has too few fields.
    [Test]
    public void MeasureMembers_ChildWithAnotherOuter_ReportsChildrenUnmeasured()
    {
        _image.Memory.WritePointer(_image.PropertyCalled("IsA") + layout.Outer, _image.Core);

        Assert.That(MeasureMembers().Unmeasured[UdkMembers.Children], Is.EqualTo(UdkSR.UnmeasuredChildren));
    }

    // A chain that loops back to its start ends at the limit of the reader and every offset still comes back.
    [Test]
    public void Measure_ChildrenLoop_FindsEveryOffset()
    {
        _image.Memory.WritePointer(_image.PropertyCalled("Linker") + layout.Next, _image.PropertyCalled("Outer"));

        Assert.That(Measure().Offsets, Is.EquivalentTo(layout.ToOffsets()));
    }

    // The property Name fits with 1 4-byte id, as it does with an id and a number.
    [Test]
    public void Measure_NameElementSizeOfFour_FindsElementSize()
    {
        _image.Memory.WriteInt32(_image.PropertyCalled("Name") + layout.ElementSize, sizeof(int));

        Assert.That(Measure().Offsets[UdkMembers.ElementSize], Is.EqualTo(layout.ElementSize));
    }

    // Any other element size on 1 of the 3 properties goes to unmeasured and the other members stay.
    [TestCase("Name", 12)]
    [TestCase("Outer", 8)]
    [TestCase("Class", 8)]
    public void Measure_WrongElementSize_ReportsItUnmeasured(string property, int size)
    {
        _image.Memory.WriteInt32(_image.PropertyCalled(property) + layout.ElementSize, size);

        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured[UdkMembers.ElementSize], Is.EqualTo(UdkSR.UnmeasuredElementSize));
            Assert.That(measurement.Offsets[UdkMembers.Offset], Is.EqualTo(layout.Offset));
        }
    }

    // 1 of the 3 properties that does not read back its offset leaves no int that fits all 3.
    [TestCase("Outer")]
    [TestCase("Name")]
    [TestCase("Class")]
    public void MeasureMembers_WrongPropertyOffset_ReportsItUnmeasured(string property)
    {
        _image.Memory.WriteInt32(_image.PropertyCalled(property) + layout.Offset, 0);

        var (offsets, unmeasured) = MeasureMembers();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unmeasured[UdkMembers.Offset], Is.EqualTo(UdkSR.UnmeasuredOffset));
            Assert.That(offsets[UdkMembers.ElementSize], Is.EqualTo(layout.ElementSize));
        }
    }

    // Properties with no link pointers leave nothing to find.
    [Test]
    public void Measure_NoPropertyLinks_ReportsLinkUnmeasured()
    {
        foreach (string property in (string[])["Outer", "Name", "Class"])
        {
            _image.Memory.WritePointer(_image.PropertyCalled(property) + layout.LinkNext, Address.Zero);
        }

        Assert.That(Measure().Unmeasured[UdkMembers.PropertyLinkNext], Is.EqualTo(UdkSR.UnmeasuredPropertyLinkNext));
    }

    // A link that leaves the properties is no link, because the next property of a struct is always a property.
    [Test]
    public void Measure_LinkToAClass_ReportsLinkUnmeasured()
    {
        _image.Memory.WritePointer(_image.PropertyCalled("Name") + layout.LinkNext, _image.ClassCalled("Object"));

        Assert.That(Measure().Unmeasured[UdkMembers.PropertyLinkNext], Is.EqualTo(UdkSR.UnmeasuredPropertyLinkNext));
    }

    // Next connects every property once the class doesn't have a function and the link is still the other pointer.
    [Test]
    public void Measure_ClassWithOnlyProperties_DoesNotUseNextAsLink()
    {
        _image.Memory.WritePointer(_image.PropertyCalled("IsA") + layout.Class, _image.ClassCalled("ObjectProperty"));

        Assert.That(Measure().Offsets[UdkMembers.PropertyLinkNext], Is.EqualTo(layout.LinkNext));
    }

    // Without the class Property its size cannot be read, so only PropertyClass goes to unmeasured.
    [Test]
    public void Measure_PropertyClassMissing_ReportsPropertyClassUnmeasured()
    {
        _image.Memory.WritePointer(_image.ClassCalled("Property") + layout.Class, Address.Zero);

        var measurement = Measure();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured.Keys, Is.EquivalentTo((string[])[UdkMembers.PropertyClass]));
            Assert.That(measurement.Offsets[UdkMembers.EnumNames], Is.EqualTo(layout.FieldSize));
        }
    }

    // A copy of an object outside the object array is no known object, even when it reads as the right class.
    [Test]
    public void Measure_SlotAtObjectOutsideTheArray_SkipsIt()
    {
        var copy = new Address(0x0900_0000);
        _image.Memory.Add(copy, _image.Memory.ReadArray<byte>(_image.Engine, 0x100).UnwrapOr([]));
        _ = _image.AddSlot(copy);

        Assert.That(Measure().Globals[Engine], Is.EqualTo(Rvas(_image.EngineSlotRva)));
    }

    // A static in an earlier section comes first in the list.
    [Test]
    public void Measure_SlotsInTwoSections_ListsThemInSectionOrder()
    {
        uint earlier = _image.Sections[0].Rva + 0x40;
        _image.Memory.WritePointer(FakeImage.Base + earlier, _image.Engine);

        Assert.That(Measure().Globals[Engine], Is.EqualTo(Rvas(earlier, _image.EngineSlotRva)));
    }

    // The object name of a slot compares ordinally, because a name in the table has 1 spelling.
    [Test]
    public void Measure_SlotObjectInAnotherCase_ReportsItUnmeasured()
    {
        var targets = WithoutSlots();
        targets[World] = new UdkObjectSlotTarget("World", "theworld");

        var measurement = UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, targets, "file.bin", NullLogger.Instance).Unwrap();

        Assert.That(measurement.Unmeasured[World], Is.EqualTo(UdkSR.FormatUnmeasuredSlot("World")));
    }

    // A class that no object derives from goes to unmeasured with the class in the reason.
    [Test]
    public void Measure_SlotOfUnknownClass_ReportsItUnmeasured()
    {
        var targets = WithoutSlots();
        targets[Engine] = new UdkObjectSlotTarget("Missing");

        var measurement = UdkMeasurement.Measure(_image.Memory, FakeImage.Base, _image.Sections, targets, "file.bin", NullLogger.Instance).Unwrap();

        Assert.That(measurement.Unmeasured[Engine], Is.EqualTo(UdkSR.FormatUnmeasuredSlot("Missing")));
    }

    private static Result<PlayerMeasurement> Measure(FakeImage image)
    {
        return UdkMeasurement.Measure(image.Memory, FakeImage.Base, image.Sections, _targets, "file.bin", NullLogger.Instance);
    }

    private static uint[] Rvas(params uint[] rvas)
    {
        return rvas;
    }

    private PlayerMeasurement Measure()
    {
        return Measure(_image).Unwrap();
    }

    private string MeasureError()
    {
        return Measure(_image).UnwrapErr().Message;
    }

    private (Dictionary<string, int> Offsets, Dictionary<string, string> Unmeasured) MeasureMembers()
    {
        var names = UdkArrays.FindNames(_image.Memory, FakeImage.Base, _image.Sections, FirstNames).Unwrap();
        var objects = UdkArrays.FindObjects(_image.Memory, FakeImage.Base, _image.Sections, names.Rvas).Unwrap();

        Dictionary<string, int> offsets = [];
        Dictionary<string, string> unmeasured = [];

        _ = UdkMembers.Measure(_image.Memory, names, objects, offsets, unmeasured).Unwrap();

        return (offsets, unmeasured);
    }

    private static Dictionary<string, UdkTarget> WithoutSlots()
    {
        return _targets
            .Where(static target => target.Value is not UdkObjectSlotTarget)
            .ToDictionary();
    }
}
