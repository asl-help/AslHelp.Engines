using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class FieldPropertyReaderEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    int superStruct,
    FieldStructLayout structLayout,
    FieldPropertyLayout fieldLayout,
    FieldClassLayout classLayout,
    PropertyLayout layout)
{
    private const int BlockSpacing = 0x100;
    private const int IntPropertyId = 1;
    private const int IntValueId = 2;
    private const int MaxProperties = 4096;
    private const int NoneId = 0;
    private const int UnknownOffset = -1;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);

    // Use the 4.25 x86, 4.25 x64, 5.4 x64 and 5.8 x64 profiles, with ArrayDim 4 bytes in front of ElementSize.
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(
            MemoryLayout.X86,
            new Address(0x8000_0000),
            0x28,
            new FieldStructLayout(0x30),
            new FieldPropertyLayout(0x04, 0x10, 0x14),
            new FieldClassLayout(0x00),
            new PropertyLayout(0x20, 0x24, 0x34)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            0x40,
            new FieldStructLayout(0x50),
            new FieldPropertyLayout(0x08, 0x20, 0x28),
            new FieldClassLayout(0x00),
            new PropertyLayout(0x38, 0x3C, 0x4C)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            0x40,
            new FieldStructLayout(0x50),
            new FieldPropertyLayout(0x08, 0x18, 0x20),
            new FieldClassLayout(0x00),
            new PropertyLayout(0x30, 0x34, 0x44)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            0x40,
            new FieldStructLayout(0x50),
            new FieldPropertyLayout(0x08, 0x18, 0x20),
            new FieldClassLayout(0x08),
            new PropertyLayout(0x30, 0x34, 0x44)),
    ];

    private readonly Address _actor = start + 0x1_1000;
    private readonly Address _block = start + 0x8_0000;
    private readonly Address _chain = start + 0x100_0000;
    private readonly Address _class = start + 0x2_0000;
    private readonly Address _nameArray = start;
    private readonly Address _pawn = start + 0x1_0000;
    private readonly Address _pool = start + 0x7_0000;
    private readonly Address _property1 = start + 0x4_0000;
    private readonly Address _property2 = start + 0x4_1000;
    private readonly Address _property3 = start + 0x4_2000;

    private Address TopOfAddressRange => new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);

    // Taking only the 1st property reads just the list pointer, because a caller can stop early.
    [Test]
    public void PropertiesOf_FirstOnly_ReadsOnlyTheListPointer()
    {
        var memory = MemoryWithChain();

        var first = CreateReader(memory).PropertiesOf(_pawn).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(_property1));
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
    }

    // 2 enumerators of 1 sequence keep their own position, so a nested enumeration of the same list works.
    [Test]
    public void PropertiesOf_TwoEnumeratorsAtOnce_KeepTheirOwnPositions()
    {
        var sequence = CreateReader(MemoryWithChain()).PropertiesOf(_pawn);

        using var ahead = sequence.GetEnumerator();
        using var behind = sequence.GetEnumerator();

        ahead.MoveNext();
        ahead.MoveNext();
        behind.MoveNext();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ahead.Current.Unwrap(), Is.EqualTo(_property2));
            Assert.That(behind.Current.Unwrap(), Is.EqualTo(_property1));
        }
    }

    // 2 properties that point at each other fail after 4096 items, because a damaged chain can loop.
    [Test]
    public void PropertiesOf_LoopOfTwo_FailsAfter4096AndReadsTheListOnce()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property2 + fieldLayout.Next, _property1);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(MaxProperties + 1));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(items[MaxProperties - 1].Unwrap(), Is.EqualTo(_property2));
            Assert.That(items[MaxProperties].UnwrapErr().Message, Does.StartWith($"The struct at {_pawn} has more than 4096 properties."));
            Assert.That(memory.ReadsAt(_pawn + structLayout.ChildProperties), Is.EqualTo(1));
        }
    }

    // The limit stops the next item only, so a next pointer of property 4096 that doesn't read ends the list with its failure.
    [Test]
    public void PropertiesOf_UnreadableNextOfLastProperty_YieldsFailureBehind4096()
    {
        var memory = MemoryWithStructs();
        memory.Add(_chain, new byte[((MaxProperties - 1) * BlockSpacing) + fieldLayout.Next]);
        memory.WritePointer(_pawn + structLayout.ChildProperties, _chain);

        for (int i = 0; i < MaxProperties - 1; i++)
        {
            var field = _chain + (i * BlockSpacing);
            memory.WritePointer(field + fieldLayout.Next, field + BlockSpacing);
        }

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();
        var last = _chain + ((MaxProperties - 1) * BlockSpacing);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(MaxProperties + 1));
            Assert.That(items[MaxProperties - 1].Unwrap(), Is.EqualTo(last));
            Assert.That(items[MaxProperties].UnwrapErr().Message, Does.StartWith($"Can't read the next property after the property at {last}:"));
        }
    }

    // A next pointer reads the full pointer size, so a block that ends right after it reads and a block that ends 1 byte earlier fails.
    [TestCase(0, false)]
    [TestCase(1, true)]
    public void PropertiesOf_BlockEndsAtNextPointer_NeedsEveryByte(int bytesShort, bool fails)
    {
        var memory = MemoryWithStructs();
        memory.WritePointer(_pawn + structLayout.ChildProperties, _property1);
        memory.Add(_property1, new byte[fieldLayout.Next + memoryLayout.PointerSize - bytesShort]);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(items, Has.Count.EqualTo(fails ? 2 : 1));
            Assert.That(items[^1].IsErr, Is.EqualTo(fails));
        }
    }

    // A struct at address 0 fails at the child properties offset and doesn't throw, because a super chain ends in that address.
    [Test]
    public void PropertiesOf_AddressZero_YieldsOneFailure()
    {
        var items = CreateReader(MemoryWithChain()).PropertiesOf(Address.Zero).ToList();
        var address = Address.Zero + structLayout.ChildProperties;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Does.StartWith($"Can't read the properties of the struct at {Address.Zero}: reading {address} failed."));
        }
    }

    // A garbage next pointer at the top of the address range comes back and its own next read ends the list with a failure.
    [Test]
    public void PropertiesOf_NextPointerAtTopOfAddressRange_YieldsAddressThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldLayout.Next, TopOfAddressRange);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[1].Unwrap(), Is.EqualTo(TopOfAddressRange));
            Assert.That(items[2].UnwrapErr().Message, Does.StartWith($"Can't read the next property after the property at {TopOfAddressRange}:"));
        }
    }

    // SuperOf returns the next struct to enumerate, so each struct up the chain lists its own properties once.
    [Test]
    public void PropertiesOf_SuperChainFromStructReader_ListsEachStructOnce()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_pawn + superStruct, _actor);
        memory.WritePointer(_actor + structLayout.ChildProperties, _property3);
        memory.WritePointer(_property2 + fieldLayout.Next, Address.Zero);

        FlatNameTable nameTable = new(memory, _nameArray, _entryLayout);
        ObjectReader objectReader = new(memory, nameTable, new ObjectLayout(0, 0, 0));
        StructReader structReader = new(memory, objectReader, new StructLayout(superStruct, 0), new ChildLayout(0));
        var reader = CreateReader(memory);
        List<Address> properties = [];

        for (var type = _pawn; type != Address.Zero; type = structReader.SuperOf(type).Unwrap())
        {
            properties.AddRange(Properties(reader, type));
        }

        Address[] expected = [_property1, _property2, _property3];

        Assert.That(properties, Is.EqualTo(expected));
    }

    // PropertiesOf reads the list and the next pointers and nothing else, so it works before the measurer has found the other offsets.
    [Test]
    public void PropertiesOf_OnlyListAndNextOffsetsKnown_ReadsOnlyThosePointers()
    {
        var memory = MemoryWithChain();
        FieldPropertyReader reader = new(
            memory,
            new FlatNameTable(memory, _nameArray, _entryLayout),
            structLayout,
            fieldLayout with { Class = UnknownOffset, Name = UnknownOffset },
            new FieldClassLayout(UnknownOffset),
            new PropertyLayout(UnknownOffset, UnknownOffset, UnknownOffset));

        Address[] expected = [_property1, _property2, _property3];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Properties(reader, _pawn), Is.EqualTo(expected));
            Assert.That(memory.Reads, Is.EqualTo(4));
        }
    }

    // A memory without a pointer size refuses every pointer read, so the list and the class name fail with that reason and don't throw.
    [Test]
    public void PropertiesOf_ClassNameOf_MemoryWithoutPointerSize_FailWithThatReason()
    {
        FakeMemory memory = new() { Layout = default };
        memory.Add(_pawn, new byte[BlockSpacing]);
        memory.Add(_property1, new byte[BlockSpacing]);

        var reader = CreateReader(memory);
        var items = reader.PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.Contain("pointer size of 0 bytes"));
            Assert.That(reader.ClassNameOf(_property1).UnwrapErr().Message, Does.Contain("pointer size of 0 bytes"));
        }
    }

    // A negative number fails before the table read, so a wrong name offset doesn't fill the cache of the table.
    [Test]
    public void NameOf_NegativeNumber_ReadsNothingFromTheTable()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + fieldLayout.Name + sizeof(int), -1);

        var name = CreateReader(memory).NameOf(_property1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(name.IsErr, Is.True);
            Assert.That(memory.ReadsAt(_nameArray + memoryLayout.PointerSize), Is.Zero);
        }
    }

    // A number that turns negative fails even though the table has the name in its cache.
    [Test]
    public void NameOf_NumberTurnsNegativeAfterNameCached_Fails()
    {
        var memory = MemoryWithChain();
        var reader = CreateReader(memory);

        string first = reader.NameOf(_property1).Unwrap();
        memory.WriteInt32(_property1 + fieldLayout.Name + sizeof(int), -1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("IntValue"));
            Assert.That(reader.NameOf(_property1).IsErr, Is.True);
        }
    }

    // A negative id keeps the failure of the pool in the message, because the id comes from a wrong name offset.
    [Test]
    public void NameOf_NegativeIdInPool_WrapsTableError()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + fieldLayout.Name, -1);
        memory.AddNamePool(_pool, _block);

        var address = _property1 + fieldLayout.Name;

        Assert.That(
            CreateReader(memory, new ChunkedNameTable(memory, _pool)).NameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the property at {_property1} from {address}. Name -1 is below 0. "
                + $"Check FieldPropertyLayout.Name (0x{fieldLayout.Name:X})."));
    }

    // UTF-16 text in the pool comes back whole, also with a character outside the 2-byte range.
    [Test]
    public void NameOf_ClassNameOf_WideTextInPool_ReturnText()
    {
        const int TextId = 0x40;

        string[] texts = ["Größe", "Value\U0001F600"];

        using (Assert.EnterMultipleScope())
        {
            foreach (string text in texts)
            {
                var memory = MemoryWithChain();
                memory.AddNamePool(_pool, _block);
                memory.AddPoolEntry(_block + (TextId * 2), wide: true, text);
                memory.WriteInt32(_property1 + fieldLayout.Name, TextId);
                memory.WriteInt32(_class + classLayout.Name, TextId);

                var reader = CreateReader(memory, new ChunkedNameTable(memory, _pool));

                Assert.That(reader.NameOf(_property1).Unwrap(), Is.EqualTo(text));
                Assert.That(reader.ClassNameOf(_property1).Unwrap(), Is.EqualTo(text));
            }
        }
    }

    // A class name that starts with F or U keeps that letter, because a class name doesn't have a prefix to strip.
    [TestCase("FloatProperty")]
    [TestCase("FieldPathProperty")]
    [TestCase("UInt32Property")]
    public void ClassNameOf_NameStartingLikeAPrefix_KeepsIt(string className)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(_nameArray, _entryLayout, className);
        memory.AddFieldClass(_class, classLayout, 0);
        memory.AddField(_property1, fieldLayout, 0, _class);

        Assert.That(CreateReader(memory).ClassNameOf(_property1).Unwrap(), Is.EqualTo(className));
    }

    // A number on the field class adds _ and the number minus 1, because ClassNameOf returns the name as the engine prints it.
    [Test]
    public void ClassNameOf_ClassNameWithNumber_ReturnsFullName()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_class + classLayout.Name + sizeof(int), 5);

        Assert.That(CreateReader(memory).ClassNameOf(_property1).Unwrap(), Is.EqualTo("IntProperty_4"));
    }

    // A block that ends right after the class name id fails the read of the id and the number, so the cause has the address of the id.
    [Test]
    public void ClassNameOf_ClassNumberOutsideReadableMemory_FailsAtClassNameId()
    {
        var memory = MemoryWithNames();
        memory.AddField(_property1, fieldLayout, IntValueId, _class);
        memory.Add(_class, new byte[classLayout.Name + sizeof(int)]);
        memory.WriteInt32(_class + classLayout.Name, IntPropertyId);

        var address = _class + classLayout.Name;

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the class at {_class} of the property at {_property1} from {address}. No block contains {address}. "
                + $"Check FieldClassLayout.Name (0x{classLayout.Name:X})."));
    }

    // A class name id outside the table keeps the failure of the table in the message.
    [Test]
    public void ClassNameOf_ClassNameIdOutsideTable_WrapsTableError()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_class + classLayout.Name, 9);

        var address = _class + classLayout.Name;

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the class at {_class} of the property at {_property1} from {address}. "
                + "Name 9 is outside the array of 3 names. "
                + $"Check FieldClassLayout.Name (0x{classLayout.Name:X})."));
    }

    // A garbage class pointer at the top of the address range fails with that class and doesn't throw.
    [Test]
    public void ClassNameOf_ClassAtTopOfAddressRange_FailsWithClass()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldLayout.Class, TopOfAddressRange);

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Does.StartWith($"Can't read the name of the class at {TopOfAddressRange} of the property at {_property1} from "));
    }

    // Every read goes to memory again, because the game can free a property and reuse its address.
    [Test]
    public void NameOf_ClassNameOf_OffsetOf_ElementSizeOf_ArrayDimOf_SameAddressAfterMemoryChanged_ReturnNewValues()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + layout.ArrayDim, 1);
        memory.WriteInt32(_property1 + layout.ElementSize, 4);
        memory.WriteInt32(_property1 + layout.Offset, 0x10);
        var reader = CreateReader(memory);

        string firstName = reader.NameOf(_property1).Unwrap();
        string firstClassName = reader.ClassNameOf(_property1).Unwrap();
        int firstArrayDim = reader.ArrayDimOf(_property1).Unwrap();
        int firstElementSize = reader.ElementSizeOf(_property1).Unwrap();
        int firstOffset = reader.OffsetOf(_property1).Unwrap();

        memory.WriteInt32(_property1 + fieldLayout.Name, NoneId);
        memory.WriteInt32(_class + classLayout.Name, IntValueId);
        memory.WriteInt32(_property1 + layout.ArrayDim, 2);
        memory.WriteInt32(_property1 + layout.ElementSize, 8);
        memory.WriteInt32(_property1 + layout.Offset, 0x20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstName, Is.EqualTo("IntValue"));
            Assert.That(firstClassName, Is.EqualTo("IntProperty"));
            Assert.That(firstArrayDim, Is.EqualTo(1));
            Assert.That(firstElementSize, Is.EqualTo(4));
            Assert.That(firstOffset, Is.EqualTo(0x10));
            Assert.That(reader.NameOf(_property1).Unwrap(), Is.EqualTo("None"));
            Assert.That(reader.ClassNameOf(_property1).Unwrap(), Is.EqualTo("IntValue"));
            Assert.That(reader.ArrayDimOf(_property1).Unwrap(), Is.EqualTo(2));
            Assert.That(reader.ElementSizeOf(_property1).Unwrap(), Is.EqualTo(8));
            Assert.That(reader.OffsetOf(_property1).Unwrap(), Is.EqualTo(0x20));
        }
    }

    // A garbage property address at the top of the address range is a failure from every read and doesn't throw.
    [Test]
    public void NameOf_ClassNameOf_OffsetOf_PropertyAtTopOfAddressRange_Fail()
    {
        var reader = CreateReader(MemoryWithChain());
        var top = TopOfAddressRange;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.NameOf(top).UnwrapErr().Message, Does.StartWith($"Can't read the name of the property at {top} from "));
            Assert.That(reader.ClassNameOf(top).UnwrapErr().Message, Does.StartWith($"Can't read the class of the property at {top}:"));
            Assert.That(reader.OffsetOf(top).UnwrapErr().Message, Does.StartWith($"Can't read the offset of the property at {top}:"));
        }
    }

    // Each member reads 4 bytes, so a block that ends right after it reads and a block that ends 1 byte earlier fails.
    [TestCase(0, false)]
    [TestCase(1, true)]
    public void OffsetOf_ElementSizeOf_ArrayDimOf_BlockEndsAtMember_NeedEveryByte(int bytesShort, bool fails)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadFromBlock(layout.ArrayDim, bytesShort, static (reader, prop) => reader.ArrayDimOf(prop)).IsErr, Is.EqualTo(fails));
            Assert.That(ReadFromBlock(layout.ElementSize, bytesShort, static (reader, prop) => reader.ElementSizeOf(prop)).IsErr, Is.EqualTo(fails));
            Assert.That(ReadFromBlock(layout.Offset, bytesShort, static (reader, prop) => reader.OffsetOf(prop)).IsErr, Is.EqualTo(fails));
        }
    }

    private static List<Address> Properties(FieldPropertyReader reader, Address type)
    {
        return [.. reader.PropertiesOf(type).Select(static item => item.Unwrap())];
    }

    private Result<int> ReadFromBlock(int member, int bytesShort, Func<FieldPropertyReader, Address, Result<int>> read)
    {
        var memory = MemoryWithNames();
        memory.Add(_property1, new byte[member + sizeof(int) - bytesShort]);

        return read(CreateReader(memory), _property1);
    }

    private FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(_nameArray, _entryLayout, "None", "IntProperty", "IntValue");

        return memory;
    }

    // Lays out Pawn and Actor with empty lists and the field class IntProperty.
    private FakeMemory MemoryWithStructs()
    {
        var memory = MemoryWithNames();
        memory.Add(_pawn, new byte[BlockSpacing]);
        memory.Add(_actor, new byte[BlockSpacing]);
        memory.AddFieldClass(_class, classLayout, IntPropertyId);

        return memory;
    }

    // Gives Pawn a chain of 3 properties that ends in a null pointer.
    private FakeMemory MemoryWithChain()
    {
        var memory = MemoryWithStructs();
        memory.WritePointer(_pawn + structLayout.ChildProperties, _property1);
        memory.AddField(_property1, fieldLayout, IntValueId, _class, _property2);
        memory.AddField(_property2, fieldLayout, IntValueId, _class, _property3);
        memory.AddField(_property3, fieldLayout, IntValueId, _class);

        return memory;
    }

    private FieldPropertyReader CreateReader(FakeMemory memory)
    {
        return CreateReader(memory, new FlatNameTable(memory, _nameArray, _entryLayout));
    }

    private FieldPropertyReader CreateReader(FakeMemory memory, INameTable nameTable)
    {
        return new FieldPropertyReader(memory, nameTable, structLayout, fieldLayout, classLayout, layout);
    }
}
