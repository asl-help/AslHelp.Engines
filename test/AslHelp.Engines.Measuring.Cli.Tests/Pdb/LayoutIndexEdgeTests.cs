using AslHelp.Engines.Measuring.Cli.Pdb;
using AsmResolver.Symbols.Pdb.Leaves;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class LayoutIndexEdgeTests
{
    private const string VariadicName = "Call<void (__cdecl*)(int,...)>";

    private static readonly SimpleTypeRecord _int = new(SimpleTypeKind.Int32);

    // A template argument can contain dots, so a forward record under such a name fails as forward and not as absent.
    [Test]
    public void Find_ForwardOnlyNameWithDots_FailsAsForwardOnly()
    {
        Assert.That(Index(Forward(VariadicName)).Find(VariadicName).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeForwardOnly(VariadicName)));
    }

    // The exact spelling wins over the lookup that turns dots into scopes.
    [Test]
    public void Find_CompleteNameWithDots_MatchesExactly()
    {
        var index = Index(Complete(VariadicName, 0x8, Data("Value", 0x4)));

        Assert.That(index.Find(VariadicName).Unwrap().Single().Member("Value")!.Offset, Is.EqualTo(0x4));
    }

    // A forward record next to the complete record doesn't get in the way, in either record order.
    [Test]
    public void Find_ForwardAfterComplete_ReturnsTheComplete()
    {
        var index = Index(Complete("Item", 0x8, Data("Value", 0x4)), Forward("Item"));

        Assert.That(index.Find("Item").Unwrap().Single().Size, Is.EqualTo(0x8));
    }

    // An index without any records fails with the text of each spelling.
    [TestCase("Item")]
    [TestCase("::Item")]
    [TestCase("::")]
    [TestCase("")]
    public void Find_EmptyIndex_FailsWithTheTextAsGiven(string typeName)
    {
        Assert.That(Index().Find(typeName).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeNotFound(typeName)));
    }

    // An enum doesn't have a layout and its record isn't a class record to read a size from.
    [Test]
    public void Find_EnumName_FailsAsAbsent()
    {
        var index = Index(new EnumTypeRecord("Kind", _int, default));

        Assert.That(index.Find("Kind").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeNotFound("Kind")));
    }

    // A bitfield in a base class keeps its bits and only its offset moves.
    [Test]
    public void Find_BitFieldInBaseClass_KeepsBitsAndAddsOffset()
    {
        var parent = Complete("Parent", 0x8, Data("Flag", 0x4, new BitFieldTypeRecord(_int, 3, 5)));
        var index = Index(parent, Complete("Item", 0x20, Base(parent, 0x10)));

        var flag = index.Find("Item").Unwrap().Single().Member("Flag")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(flag.Offset, Is.EqualTo(0x14));
            Assert.That(flag.BitPosition, Is.EqualTo(3));
            Assert.That(flag.BitCount, Is.EqualTo(5));
            Assert.That(flag.Size, Is.EqualTo(4));
        }
    }

    // A bitfield over an enum takes its word size from the underlying type of the enum.
    [Test]
    public void Find_BitFieldOverEnum_SizesFromUnderlyingType()
    {
        var enumeration = new EnumTypeRecord("Kind", new SimpleTypeRecord(SimpleTypeKind.Byte), default);
        var index = Index(Complete("Item", 0x8, Data("Flag", 0x0, new BitFieldTypeRecord(enumeration, 0, 3))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Flag")!.Size, Is.EqualTo(1));
    }

    // Each base class folds at its own offset, after the own members and in the order of the field list.
    [Test]
    public void Find_TwoBaseClasses_FoldsEachAtItsOffset()
    {
        var left = Complete("Left", 0x8, Data("Shared", 0x4));
        var right = Complete("Right", 0x10, Data("Shared", 0x8), Data("Other", 0xC));
        var index = Index(left, right, Complete("Item", 0x20, Base(left, 0x0), Base(right, 0x8), Data("Own", 0x18)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Members.Select(static member => (member.Name, member.Offset)), Is.EqualTo(new (string, uint)[]
            {
                ("Own", 0x18),
                ("Shared", 0x4),
                ("Shared", 0x10),
                ("Other", 0x14),
            }));
            Assert.That(layout.Member("Shared")!.DeclaredBy, Is.EqualTo("Left"));
        }
    }

    // 2 paths to 1 base class aren't a cycle, so both copies of its members stay.
    [Test]
    public void Find_DiamondBaseClass_FoldsBothPaths()
    {
        var root = Complete("Root", 0x8, Data("Value", 0x4));
        var left = Complete("Left", 0x8, Base(Forward("Root"), 0x0));
        var right = Complete("Right", 0x8, Base(Forward("Root"), 0x0));
        var index = Index(root, left, right, Complete("Item", 0x20, Base(left, 0x0), Base(right, 0x10)));

        var layout = index.Find("Item").Unwrap().Single();

        Assert.That(layout.Members.Select(static member => member.Offset), Is.EqualTo(new uint[] { 0x4, 0x14 }));
    }

    // 2 base classes with 2 layouts each give a layout for each pair, in record order.
    [Test]
    public void Find_TwoBasesWithTwoLayoutsEach_ReturnsEveryPair()
    {
        var index = Index(
            Complete("Left", 0x8, Data("First", 0x0)),
            Complete("Left", 0x8, Data("First", 0x4)),
            Complete("Right", 0x8, Data("Second", 0x0)),
            Complete("Right", 0x8, Data("Second", 0x4)),
            Complete("Item", 0x20, Base(Forward("Left"), 0x0), Base(Forward("Right"), 0x10)));

        var layouts = index.Find("Item").Unwrap();

        Assert.That(
            layouts.Select(static layout => (layout.Member("First")!.Offset, layout.Member("Second")!.Offset)),
            Is.EqualTo(new (uint, uint)[] { (0x0, 0x10), (0x0, 0x14), (0x4, 0x10), (0x4, 0x14) }));
    }

    // 2 compile units can derive 2 types from each other, so the fold fails instead of recursing until the process ends.
    [Test]
    public void Find_BaseClassesInACycle_FailsWithTypeName()
    {
        var index = Index(
            Complete("Item", 0x20, Base(Forward("Parent"), 0x0)),
            Complete("Parent", 0x20, Base(Forward("Item"), 0x0)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeUnreadable("Item", SR.FormatPdbBaseClassCycle("Item"))));
    }

    // A record that derives from its own name is the shortest cycle.
    [Test]
    public void Find_BaseClassIsTheTypeItself_FailsWithTheName()
    {
        var index = Index(Complete("Item", 0x20, Base(Forward("Item"), 0x0)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // The fold state doesn't outlive a lookup, so the same type reads again and a failed lookup doesn't break the next.
    [Test]
    public void Find_SameTypeTwice_ReturnsEqualLayouts()
    {
        var parent = Complete("Parent", 0x8, Data("Value", 0x4));
        var index = Index(
            parent,
            Complete("Item", 0x20, Base(Forward("Parent"), 0x10)),
            Complete("Broken", 0x20, Base(Forward("Broken"), 0x0)));

        var first = index.Find("Item").Unwrap().Single();
        _ = index.Find("Broken");
        var second = index.Find("Item").Unwrap().Single();

        Assert.That(second, Is.EqualTo(first));
    }

    // A base class that isn't a class can only come from a bad file, so the lookup fails with the type.
    [Test]
    public void Find_BaseClassIsNoClass_FailsWithTypeName()
    {
        var index = Index(Complete("Item", 0x20, Base(_int, 0x0)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // An enum record passes as a composite record, but it doesn't have a layout to fold.
    [Test]
    public void Find_BaseClassIsAnEnum_FailsWithTypeName()
    {
        var index = Index(Complete("Item", 0x20, Base(new EnumTypeRecord("Kind", _int, default), 0x0)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // The 2nd record of a name fails the lookup, so a caller doesn't get the 1st layout as if it were all of them.
    [Test]
    public void Find_SecondRecordBaseMissing_FailsWithoutTheFirstLayout()
    {
        var index = Index(Complete("Item", 0x20, Data("Value", 0x0)), Complete("Item", 0x20, Base(Forward("Parent"), 0x0)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.Contain("'Parent'"));
    }

    // A virtual base doesn't have a fixed offset, so its members stay out and the rest of the type still reads.
    [Test]
    public void Find_VirtualBaseClass_LeavesItsMembersOut()
    {
        var parent = Complete("Parent", 0x8, Data("Value", 0x4));
        var pointer = new PointerTypeRecord(_int, PointerAttributes.Near64, 8);
        var index = Index(parent, Complete("Item", 0x20, new VBaseClassField(parent, pointer, 0x0, 0x1, false), Data("Own", 0x8)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Value"), Is.Null);
            Assert.That(layout.Member("Own")!.Offset, Is.EqualTo(0x8));
        }
    }

    // A nested type in the field list doesn't add a member.
    [Test]
    public void Find_NestedTypeField_LeavesItOut()
    {
        var inner = Complete("Item::Inner", 0x8, Data("Value", 0x4));
        var index = Index(inner, Complete("Item", 0x8, new NestedTypeField(inner, "Inner"), Data("Own", 0x0)));

        Assert.That(index.Find("Item").Unwrap().Single().Members, Has.Count.EqualTo(1));
    }

    // A base class in a later part of a continued field list folds like one in the 1st part.
    [Test]
    public void Find_BaseClassInContinuedList_FoldsIt()
    {
        var parent = Complete("Parent", 0x8, Data("Value", 0x4));
        var head = Complete("Item", 0x20, Data("Own", 0x0));
        var tail = List(Base(parent, 0x10));

        var index = LayoutIndex.Build([parent, head], list => list == head.Fields ? tail : null);

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Offset, Is.EqualTo(0x14));
    }

    // A continuation back to a list that already read can't end, so the lookup fails instead of growing until memory runs out.
    [Test]
    public void Find_ContinuationCycle_FailsWithTypeName()
    {
        var head = Complete("Item", 0x20, Data("Own", 0x0));
        var tail = List(Data("Other", 0x8));
        var next = new Dictionary<FieldListLeaf, FieldListLeaf> { [head.Fields!] = tail, [tail] = head.Fields! };

        var index = LayoutIndex.Build([head], list => next.GetValueOrDefault(list));

        Assert.That(index.Find("Item").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeUnreadable("Item", SR.FormatPdbFieldListCycle("Item"))));
    }

    // A member past 4 GiB doesn't fit the offset, so the lookup fails with the type instead of throwing out of Find.
    [Test]
    public void Find_MemberOffsetPastFourGiB_FailsWithTypeName()
    {
        var index = Index(Complete("Item", 0x20, Data("Value", 0x1_0000_0000)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // An array past 4 GiB doesn't fit the size either.
    [Test]
    public void Find_ArrayPastFourGiB_FailsWithTypeName()
    {
        var array = new ArrayTypeRecord(new SimpleTypeRecord(SimpleTypeKind.Byte), _int, 0x1_0000_0000);
        var index = Index(Complete("Item", 0x20, Data("Value", 0x0, array)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // A record past 4 GiB doesn't fit the size of a layout.
    [Test]
    public void Find_RecordPastFourGiB_FailsWithTypeName()
    {
        var index = Index(Complete("Item", 0x1_0000_0000, Data("Value", 0x0)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // An offset fits on its own and the sum with the base class offset doesn't.
    [Test]
    public void Find_FoldedOffsetPastFourGiB_FailsWithTypeName()
    {
        var parent = Complete("Parent", 0x20, Data("Value", 0x10));
        var index = Index(parent, Complete("Item", 0x20, Base(parent, uint.MaxValue)));

        Assert.That(index.Find("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // A const member of a forward type reads through to the complete record.
    [Test]
    public void Find_ModifierOverForwardType_SizesFromTheCompleteRecord()
    {
        var modifier = new ModifierTypeRecord(Forward("Inner"), ModifierAttributes.Const);
        var index = Index(Complete("Inner", 0x18), Complete("Item", 0x20, Data("Value", 0x0, modifier)));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.EqualTo(0x18));
    }

    // Every record of a type that is the same in each compile unit folds to 1 layout, however many units there are.
    [Test]
    public void Find_ManyEqualRecords_ReturnsOne()
    {
        var records = Enumerable.Range(0, 50_000).Select(static _ => (ITpiLeaf)Complete("Item", 0x10, Data("Value", 0x8)));

        Assert.That(LayoutIndex.Build(records, static _ => null).Find("Item").Unwrap(), Has.Count.EqualTo(1));
    }

    // Layouts with 1 size and 1 member count only differ in the offsets and each one stays, in record order.
    [Test]
    public void Find_ManyLayoutsWithOneSize_ReturnsEachInRecordOrder()
    {
        var records = Enumerable.Range(0, 1_000).Select(static offset => (ITpiLeaf)Complete("Item", 0x10000, Data("Value", (ulong)offset)));

        var layouts = LayoutIndex.Build(records, static _ => null).Find("Item").Unwrap();

        Assert.That(
            layouts.Select(static layout => layout.Member("Value")!.Offset),
            Is.EqualTo(Enumerable.Range(0, 1_000).Select(static offset => (uint)offset)));
    }

    // A record with many members keeps all of them in field list order.
    [Test]
    public void Find_ManyMembers_KeepsThemInOrder()
    {
        var fields = Enumerable.Range(0, 100_000).Select(static offset => (CodeViewField)Data($"M{offset}", (ulong)offset)).ToArray();

        var members = Index(Complete("Item", 0x20000, fields)).Find("Item").Unwrap().Single().Members;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(members, Has.Count.EqualTo(100_000));
            Assert.That(members[0].Name, Is.EqualTo("M0"));
            Assert.That(members[99_999].Offset, Is.EqualTo(99_999));
        }
    }

    // A simple type gets the size of its kind, for the kinds LayoutIndexTests leaves out.
    [TestCase(SimpleTypeKind.Byte, 1u)]
    [TestCase(SimpleTypeKind.SByte, 1u)]
    [TestCase(SimpleTypeKind.Character8, 1u)]
    [TestCase(SimpleTypeKind.UnsignedCharacter, 1u)]
    [TestCase(SimpleTypeKind.Boolean16, 2u)]
    [TestCase(SimpleTypeKind.Float16, 2u)]
    [TestCase(SimpleTypeKind.Int16, 2u)]
    [TestCase(SimpleTypeKind.UInt16, 2u)]
    [TestCase(SimpleTypeKind.Boolean32, 4u)]
    [TestCase(SimpleTypeKind.Float32PartialPrecision, 4u)]
    [TestCase(SimpleTypeKind.Int32, 4u)]
    [TestCase(SimpleTypeKind.UInt32Long, 4u)]
    [TestCase(SimpleTypeKind.Boolean64, 8u)]
    [TestCase(SimpleTypeKind.Int64, 8u)]
    [TestCase(SimpleTypeKind.UInt64, 8u)]
    [TestCase(SimpleTypeKind.Boolean128, 16u)]
    [TestCase(SimpleTypeKind.Float128, 16u)]
    [TestCase(SimpleTypeKind.Int128, 16u)]
    [TestCase(SimpleTypeKind.UInt128, 16u)]
    [TestCase(SimpleTypeKind.UInt128Oct, 16u)]
    public void Find_SimpleMember_SizesEveryKind(SimpleTypeKind kind, uint size)
    {
        var index = Index(Complete("Item", 0x20, Data("Value", 0x0, new SimpleTypeRecord(kind))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.EqualTo(size));
    }

    // A kind without a fixed size doesn't get a guessed size.
    [TestCase(SimpleTypeKind.None)]
    [TestCase(SimpleTypeKind.Void)]
    [TestCase(SimpleTypeKind.NotTranslated)]
    public void Find_SimpleMemberWithoutSize_LeavesSizeNull(SimpleTypeKind kind)
    {
        var index = Index(Complete("Item", 0x20, Data("Value", 0x0, new SimpleTypeRecord(kind))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.Null);
    }

    private static LayoutIndex Index(params ITpiLeaf[] leaves)
    {
        return LayoutIndex.Build(leaves, static _ => null);
    }

    private static ClassTypeRecord Complete(string name, ulong size, params CodeViewField[] fields)
    {
        return new ClassTypeRecord(CodeViewLeafKind.Structure, name, $".?AU{name}@@", size, StructureAttributes.HasUniqueName, null!)
        {
            Fields = List(fields),
        };
    }

    private static ClassTypeRecord Forward(string name)
    {
        const StructureAttributes Attributes = StructureAttributes.FwdRef | StructureAttributes.HasUniqueName;

        return new ClassTypeRecord(CodeViewLeafKind.Class, name, $".?AV{name}@@", 0, Attributes, null!);
    }

    private static FieldListLeaf List(params CodeViewField[] fields)
    {
        FieldListLeaf list = new();

        foreach (var field in fields)
        {
            list.Entries.Add(field);
        }

        return list;
    }

    private static InstanceDataField Data(string name, ulong offset, CodeViewTypeRecord? type = null)
    {
        return new InstanceDataField(name, offset, type ?? _int);
    }

    private static BaseClassField Base(CodeViewTypeRecord type, ulong offset)
    {
        return new BaseClassField(type)
        {
            Offset = offset,
        };
    }
}
