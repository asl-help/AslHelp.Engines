using AslHelp.Engines.Measuring.Cli.Pdb;
using AsmResolver.Symbols.Pdb.Leaves;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class LayoutIndexTests
{
    private static readonly SimpleTypeRecord _byte = new(SimpleTypeKind.Byte);
    private static readonly SimpleTypeRecord _int = new(SimpleTypeKind.Int32);
    private static readonly string[] _oneMember = ["Value"];
    private static readonly string[] _threeMembers = ["First", "Second", "Third"];
    private static readonly string[] _twoMembers = ["First", "Second"];

    // 1 complete record gives 1 layout with the size of the record and the offsets of its field list.
    [Test]
    public void Find_CompleteRecord_ReturnsSizeAndOwnMembers()
    {
        var index = Index(Complete("Item", 0x10, Data("First", 0x0), Data("Second", 0x8)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Name, Is.EqualTo("Item"));
            Assert.That(layout.Size, Is.EqualTo(0x10));
            Assert.That(layout.Members.Select(static member => member.Name), Is.EqualTo(_twoMembers));
            Assert.That(layout.Member("Second")!.Offset, Is.EqualTo(0x8));
            Assert.That(layout.Member("Second")!.DeclaredBy, Is.EqualTo("Item"));
            Assert.That(layout.Member("Missing"), Is.Null);
        }
    }

    // A base class at 0x28 with a member at 0x8 puts the member at 0x30 of the derived type.
    [Test]
    public void Find_BaseClassMember_FoldsWithBaseOffset()
    {
        var parent = Complete("Parent", 0x30, Data("Next", 0x8));
        var index = Index(parent, Complete("Item", 0x40, Base(parent, 0x28), Data("Own", 0x38)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Next")!.Offset, Is.EqualTo(0x30));
            Assert.That(layout.Member("Next")!.DeclaredBy, Is.EqualTo("Parent"));
            Assert.That(layout.Member("Own")!.DeclaredBy, Is.EqualTo("Item"));
            Assert.That(layout.Members[0].Name, Is.EqualTo("Own"));
        }
    }

    // A base class of a base class adds both offsets.
    [Test]
    public void Find_TwoLevelBase_FoldsBothLevels()
    {
        var root = Complete("Root", 0x10, Data("Value", 0x4));
        var middle = Complete("Middle", 0x20, Base(root, 0x8), Data("Other", 0x18));
        var index = Index(root, middle, Complete("Item", 0x40, Base(middle, 0x10)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Value")!.Offset, Is.EqualTo(0x1C));
            Assert.That(layout.Member("Other")!.Offset, Is.EqualTo(0x28));
        }
    }

    // The base class field points at the forward record, so the fold finds the complete record by name.
    [Test]
    public void Find_BaseIsForwardRecord_FoldsTheCompleteRecord()
    {
        var index = Index(Forward("Parent"), Complete("Parent", 0x8, Data("Value", 0x4)), Complete("Item", 0x20, Base(Forward("Parent"), 0x10)));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Offset, Is.EqualTo(0x14));
    }

    // A member of the derived type comes before a base class member with the same name.
    [Test]
    public void Find_OwnAndBaseMemberSameName_ReturnsOwnFirst()
    {
        var parent = Complete("Parent", 0x10, Data("Value", 0x4));
        var index = Index(parent, Complete("Item", 0x20, Base(parent, 0x0), Data("Value", 0x10)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Value")!.Offset, Is.EqualTo(0x10));
            Assert.That(layout.Members.Count(static member => member.Name == "Value"), Is.EqualTo(2));
        }
    }

    // A bitfield reports its bits and the size of its storage word.
    [Test]
    public void Find_BitField_ReportsPositionCountAndWordSize()
    {
        var index = Index(Complete("Item", 0x8, Data("Flag", 0x4, new BitFieldTypeRecord(_int, 3, 5)), Data("Plain", 0x0)));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Flag")!.Offset, Is.EqualTo(0x4));
            Assert.That(layout.Member("Flag")!.BitPosition, Is.EqualTo(3));
            Assert.That(layout.Member("Flag")!.BitCount, Is.EqualTo(5));
            Assert.That(layout.Member("Flag")!.Size, Is.EqualTo(4));
            Assert.That(layout.Member("Plain")!.BitPosition, Is.Null);
            Assert.That(layout.Member("Plain")!.BitCount, Is.Null);
        }
    }

    // A forward record alone fails as forward only, with the name.
    [Test]
    public void Find_ForwardOnly_FailsWithTypeName()
    {
        Assert.That(Index(Forward("Item")).Find("Item").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeForwardOnly("Item")));
    }

    // A name without any record fails as not found, with the name.
    [Test]
    public void Find_Absent_FailsWithTypeName()
    {
        var index = Index(Forward("Other"), Complete("Another", 0x8));

        Assert.That(index.Find("Item").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeNotFound("Item")));
    }

    // 2 compile units with different packing give 2 layouts in record order, so the caller picks.
    [Test]
    public void Find_TwoDifferentCompleteRecords_ReturnsBoth()
    {
        var index = Index(Complete("Item", 0x10, Data("Value", 0x8)), Complete("Item", 0xC, Data("Value", 0x4)));

        var layouts = index.Find("Item").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layouts.Select(static layout => layout.Size), Is.EqualTo(new uint[] { 0x10, 0xC }));
            Assert.That(layouts.Select(static layout => layout.Member("Value")!.Offset), Is.EqualTo(new uint[] { 0x8, 0x4 }));
        }
    }

    // The same size with a different offset is a different layout too.
    [Test]
    public void Find_SameSizeDifferentOffset_ReturnsBoth()
    {
        var index = Index(Complete("Item", 0x10, Data("Value", 0x8)), Complete("Item", 0x10, Data("Value", 0x4)));

        Assert.That(index.Find("Item").Unwrap(), Has.Count.EqualTo(2));
    }

    // The same record in 2 compile units is 1 layout.
    [Test]
    public void Find_TwoIdenticalCompleteRecords_ReturnsOne()
    {
        var index = Index(Complete("Item", 0x10, Data("Value", 0x8)), Complete("Item", 0x10, Data("Value", 0x8)));

        Assert.That(index.Find("Item").Unwrap(), Has.Count.EqualTo(1));
    }

    // A base class with 2 layouts gives the derived type 2 layouts, so the fold doesn't pick 1.
    [Test]
    public void Find_BaseWithTwoLayouts_ReturnsALayoutForEach()
    {
        var index = Index(
            Complete("Parent", 0x10, Data("Value", 0x8)),
            Complete("Parent", 0xC, Data("Value", 0x4)),
            Complete("Item", 0x20, Base(Forward("Parent"), 0x10)));

        var layouts = index.Find("Item").Unwrap();

        Assert.That(layouts.Select(static layout => layout.Member("Value")!.Offset), Is.EqualTo(new uint[] { 0x18, 0x14 }));
    }

    // A base class without a complete record would lose members, so the lookup fails with the base class.
    [Test]
    public void Find_BaseWithoutCompleteRecord_FailsWithBaseName()
    {
        var index = Index(Forward("Parent"), Complete("Item", 0x20, Base(Forward("Parent"), 0x10)));

        string message = index.Find("Item").UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("'Item'"));
            Assert.That(message, Does.Contain("'Parent'"));
        }
    }

    // A member typed by a forward record gets the size of the complete record with that name.
    [Test]
    public void Find_ForwardMemberType_SizesFromCompleteRecord()
    {
        var index = Index(Complete("Inner", 0x18), Complete("Item", 0x20, Data("Value", 0x0, Forward("Inner"))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.EqualTo(0x18));
    }

    // A member typed by a forward record with 2 different sizes doesn't get a guessed size.
    [Test]
    public void Find_ForwardMemberTypeWithTwoSizes_LeavesSizeNull()
    {
        var index = Index(Complete("Inner", 0x18), Complete("Inner", 0x10), Complete("Item", 0x20, Data("Value", 0x0, Forward("Inner"))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.Null);
    }

    // A member typed by a forward record without a complete record doesn't have a size.
    [Test]
    public void Find_ForwardOnlyMemberType_LeavesSizeNull()
    {
        var index = Index(Complete("Item", 0x20, Data("Value", 0x0, Forward("Inner"))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.Null);
    }

    // Each kind of member type has its own size, for both pointer sizes.
    [Test]
    public void Find_PointerArrayEnumMembers_SizesEach()
    {
        var pointer64 = new PointerTypeRecord(_int, PointerAttributes.Near64, 8);
        var pointer32 = new PointerTypeRecord(_int, PointerAttributes.Near32, 4);
        var array = new ArrayTypeRecord(_byte, _int, 0x400);
        var enumeration = new EnumTypeRecord("Kind", _byte, default);
        var modifier = new ModifierTypeRecord(_int, ModifierAttributes.Const);
        var index = Index(Complete(
            "Item",
            0x500,
            Data("Pointer64", 0x0, pointer64),
            Data("Pointer32", 0x8, pointer32),
            Data("Array", 0xC, array),
            Data("Enum", 0x40C, enumeration),
            Data("Modifier", 0x410, modifier),
            Data("Simple", 0x414),
            Data("SimplePointer", 0x418, new SimpleTypeRecord(SimpleTypeKind.Void, SimpleTypeMode.NearPointer64)),
            Data("SimplePointer32", 0x420, new SimpleTypeRecord(SimpleTypeKind.Void, SimpleTypeMode.NearPointer32))));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Pointer64")!.Size, Is.EqualTo(8));
            Assert.That(layout.Member("Pointer32")!.Size, Is.EqualTo(4));
            Assert.That(layout.Member("Array")!.Size, Is.EqualTo(0x400));
            Assert.That(layout.Member("Enum")!.Size, Is.EqualTo(1));
            Assert.That(layout.Member("Modifier")!.Size, Is.EqualTo(4));
            Assert.That(layout.Member("Simple")!.Size, Is.EqualTo(4));
            Assert.That(layout.Member("SimplePointer")!.Size, Is.EqualTo(8));
            Assert.That(layout.Member("SimplePointer32")!.Size, Is.EqualTo(4));
        }
    }

    // A simple type gets the size of its kind.
    [TestCase(SimpleTypeKind.Boolean8, 1u)]
    [TestCase(SimpleTypeKind.NarrowCharacter, 1u)]
    [TestCase(SimpleTypeKind.SignedCharacter, 1u)]
    [TestCase(SimpleTypeKind.WideCharacter, 2u)]
    [TestCase(SimpleTypeKind.Character16, 2u)]
    [TestCase(SimpleTypeKind.Int16Short, 2u)]
    [TestCase(SimpleTypeKind.UInt16Short, 2u)]
    [TestCase(SimpleTypeKind.Int32Long, 4u)]
    [TestCase(SimpleTypeKind.UInt32, 4u)]
    [TestCase(SimpleTypeKind.Float32, 4u)]
    [TestCase(SimpleTypeKind.Character32, 4u)]
    [TestCase(SimpleTypeKind.HResult, 4u)]
    [TestCase(SimpleTypeKind.Int64Quad, 8u)]
    [TestCase(SimpleTypeKind.UInt64Quad, 8u)]
    [TestCase(SimpleTypeKind.Float64, 8u)]
    [TestCase(SimpleTypeKind.Float80, 10u)]
    [TestCase(SimpleTypeKind.Int128Oct, 16u)]
    public void Find_SimpleMember_SizesFromKind(SimpleTypeKind kind, uint size)
    {
        var index = Index(Complete("Item", 0x20, Data("Value", 0x0, new SimpleTypeRecord(kind))));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.EqualTo(size));
    }

    // A template name has to match whole, so another spelling doesn't find the record.
    [Test]
    public void Find_TemplateName_MatchesExactSpelling()
    {
        var index = Index(Complete("List<Item *,Default>", 0x10, Data("Value", 0x0)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.Find("List<Item *,Default>").IsOk, Is.True);
            Assert.That(index.Find("List<Item*,Default>").IsErr, Is.True);
            Assert.That(index.Find("List").IsErr, Is.True);
        }
    }

    // A leading scope and a dot between scopes find the record the PDB spells with ::.
    [TestCase("Outer::Inner")]
    [TestCase("::Outer::Inner")]
    [TestCase("Outer.Inner")]
    [TestCase("::Outer.Inner")]
    public void Find_NestedNameWithLeadingScope_Matches(string typeName)
    {
        var index = Index(Complete("Outer::Inner", 0x10, Data("Value", 0x4)));

        Assert.That(index.Find(typeName).Unwrap().Single().Name, Is.EqualTo("Outer::Inner"));
    }

    // A forward record under a nested name fails as forward through the same spellings.
    [Test]
    public void Find_ForwardNestedNameWithDot_FailsAsForwardOnly()
    {
        Assert.That(Index(Forward("Outer::Inner")).Find("Outer.Inner").UnwrapErr().Message, Does.Contain("forward"));
    }

    // A union lists each member at its own offset and has its own size.
    [Test]
    public void Find_UnionMember_FoldsMembersAtZero()
    {
        var index = Index(Union("Item", 0x8, Data("Small", 0x0), Data("Large", 0x0, new SimpleTypeRecord(SimpleTypeKind.Int64))));

        var layout = index.Find("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Size, Is.EqualTo(0x8));
            Assert.That(layout.Member("Small")!.Offset, Is.Zero);
            Assert.That(layout.Member("Large")!.Offset, Is.Zero);
            Assert.That(layout.Member("Large")!.Size, Is.EqualTo(8));
        }
    }

    // A member typed by a union gets the size of the union.
    [Test]
    public void Find_UnionTypedMember_SizesFromTheUnion()
    {
        var union = Union("Inner", 0x18);
        var index = Index(union, Complete("Item", 0x20, Data("Value", 0x0, union)));

        Assert.That(index.Find("Item").Unwrap().Single().Member("Value")!.Size, Is.EqualTo(0x18));
    }

    // A static member and a virtual function table don't have a place in the layout.
    [Test]
    public void Find_StaticMemberAndVTable_LeavesThemOut()
    {
        var pointer = new PointerTypeRecord(_int, PointerAttributes.Near64, 8);
        var index = Index(Complete("Item", 0x10, new VTableField(pointer), new StaticDataField("Shared", _int), Data("Value", 0x8)));

        Assert.That(index.Find("Item").Unwrap().Single().Members.Select(static member => member.Name), Is.EqualTo(_oneMember));
    }

    // A field list past the record limit continues in another list, in the same member order.
    [Test]
    public void Find_ContinuedFieldList_ReturnsMembersOfEveryPart()
    {
        var head = Complete("Item", 0x18, Data("First", 0x0));
        var middle = List(Data("Second", 0x8));
        var tail = List(Data("Third", 0x10));
        var next = new Dictionary<FieldListLeaf, FieldListLeaf> { [head.Fields!] = middle, [middle] = tail };

        var index = LayoutIndex.Build([head], list => next.GetValueOrDefault(list));

        Assert.That(
            index.Find("Item").Unwrap().Single().Members.Select(static member => member.Name),
            Is.EqualTo(_threeMembers));
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

    private static UnionTypeRecord Union(string name, ulong size, params CodeViewField[] fields)
    {
        return new UnionTypeRecord(size)
        {
            Name = name,
            Fields = List(fields),
        };
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
