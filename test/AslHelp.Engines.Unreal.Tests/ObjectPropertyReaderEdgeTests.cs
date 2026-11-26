using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ObjectPropertyReaderEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    ObjectLayout objectLayout,
    StructLayout structLayout,
    int fieldNext,
    PropertyLayout layout)
{
    private const int ActorId = 1;
    private const int BlockSpacing = 0x100;
    private const int EnumId = 11;
    private const int FieldId = 3;
    private const int FunctionId = 8;
    private const int IntPropertyId = 6;
    private const int IntValueId = 9;
    private const int LooksLikePropertyId = 12;
    private const int MaxFields = 4096;
    private const int NumericPropertyId = 5;
    private const int ObjectId = 0;
    private const int PastBlock = 0x200;
    private const int PawnId = 2;
    private const int PropertyId = 4;
    private const int StructId = 7;
    private const int TickId = 10;
    private const int UnknownOffset = -1;
    private const int ZeroBytes = 0xF0;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);

    // Use the 3.5860 x86, 3.12791 x64, 4.23 x86 and 4.23 x64 profiles, with ArrayDim 4 bytes in front of ElementSize.
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(
            MemoryLayout.X86,
            new Address(0x8000_0000),
            new ObjectLayout(0x28, 0x2C, 0x34),
            new StructLayout(0x3C, 0x4C),
            0x40,
            new PropertyLayout(0x44, 0x48, 0x64)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            new ObjectLayout(0x40, 0x48, 0x50),
            new StructLayout(0x78, 0x80),
            0x60,
            new PropertyLayout(0x68, 0x6C, 0x8C)),
        new(
            MemoryLayout.X86,
            new Address(0x8000_0000),
            new ObjectLayout(0x18, 0x10, 0x0C),
            new StructLayout(0x28, 0x2C),
            0x1C,
            new PropertyLayout(0x20, 0x24, 0x34)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            new ObjectLayout(0x20, 0x18, 0x10),
            new StructLayout(0x40, 0x48),
            0x28,
            new PropertyLayout(0x30, 0x34, 0x44)),
    ];

    private readonly Address _actor = start + 0x1_1000;
    private readonly Address _block = start + 0x8_0000;
    private readonly Address _chain = start + 0x100_0000;
    private readonly Address _enum = start + 0x3_0000;
    private readonly Address _enumClass = start + 0x2_1000;
    private readonly Address _fieldClass = start + 0x2_2000;
    private readonly Address _function = start + 0x3_1000;
    private readonly Address _function2 = start + 0x3_2000;
    private readonly Address _functionClass = start + 0x2_3000;
    private readonly Address _intProperty = start + 0x2_4000;
    private readonly Address _looksLikeProperty = start + 0x2_5000;
    private readonly Address _nameArray = start;
    private readonly Address _numericProperty = start + 0x2_6000;
    private readonly Address _object = start + 0x1_2000;
    private readonly Address _objectClass = start + 0x2_7000;
    private readonly Address _pawn = start + 0x1_0000;
    private readonly Address _pool = start + 0x7_0000;
    private readonly Address _property1 = start + 0x4_0000;
    private readonly Address _property2 = start + 0x4_1000;
    private readonly Address _property3 = start + 0x4_2000;
    private readonly Address _propertyClass = start + 0x2_8000;
    private readonly Address _structClass = start + 0x2_9000;
    private readonly Address _unreadable = start + 0x6_0000;

    private Address TopOfAddressRange => new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);

    // Every field has a class, so a class pointer of 0 ends the list with a failure and its field doesn't drop out as if it weren't a property.
    [Test]
    public void PropertiesOf_ZeroClassOnField_YieldsPropertyThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property2 + objectLayout.Class, Address.Zero);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"The class pointer of field 2 of the struct at {_pawn} is 0. "
                    + $"Check ObjectLayout.Class (0x{objectLayout.Class:X})."));
            Assert.That(memory.ReadsAt(_property3 + objectLayout.Outer), Is.Zero);
        }
    }

    // A wrong class offset on 0 bytes fails at field 1 with that offset, so a wrong offset doesn't look like a struct without properties.
    [Test]
    public void PropertiesOf_WrongClassOffsetOnZeroBytes_YieldsOneFailure()
    {
        var reader = CreateReader(MemoryWithChain(), objectLayout with { Class = ZeroBytes }, structLayout);

        var items = reader.PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"The class pointer of field 1 of the struct at {_pawn} is 0. "
                    + $"Check ObjectLayout.Class (0x{ZeroBytes:X})."));
        }
    }

    // Only the properties come back in chain order, with a function in front, an enum between them and a function behind them.
    [Test]
    public void PropertiesOf_OtherFieldsAroundProperties_KeepsPropertiesInOrder()
    {
        var memory = MemoryWithClasses();
        AddChain(
            memory,
            _pawn,
            (_function, _functionClass),
            (_property1, _intProperty),
            (_enum, _enumClass),
            (_property2, _intProperty),
            (_function2, _functionClass));

        Address[] expected = [_property1, _property2];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // The check reads the class of a field, so a function called Property stays out and a property called Function comes back.
    [Test]
    public void PropertiesOf_FieldCalledLikeTheOtherKind_UsesItsClass()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_function, _functionClass), (_property1, _intProperty));
        memory.WriteInt32(_function + objectLayout.Name, PropertyId);
        memory.WriteInt32(_property1 + objectLayout.Name, FunctionId);

        Address[] expected = [_property1];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A class name that only ends in Property doesn't derive from Property, so its field stays out.
    [Test]
    public void PropertiesOf_ClassNameEndsInProperty_SkipsField()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_function, _looksLikeProperty), (_property1, _intProperty));

        Address[] expected = [_property1];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A field whose class is Property comes back, because the check starts at the class itself.
    [Test]
    public void PropertiesOf_ClassCalledProperty_KeepsField()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_property1, _propertyClass));

        Address[] expected = [_property1];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A chain that goes on into the super struct ends at its 1st field with another outer, also when a function is the last field of the struct.
    [Test]
    public void PropertiesOf_SuperPropertyBehindFunction_NotIncluded()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_property1, _intProperty), (_function, _functionClass));
        AddChain(memory, _actor, (_property2, _intProperty));
        memory.WritePointer(_function + fieldNext, _property2);

        var reader = CreateReader(memory);

        Address[] expected = [_property1];
        Address[] expectedActor = [_property2];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Properties(reader, _pawn), Is.EqualTo(expected));
            Assert.That(Properties(reader, _actor), Is.EqualTo(expectedActor));
        }
    }

    // SuperOf returns the next struct to enumerate, so each struct up the chain lists its own properties once.
    [Test]
    public void PropertiesOf_SuperChainFromStructReader_ListsEachStructOnce()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_property1, _intProperty), (_property2, _intProperty));
        AddChain(memory, _actor, (_property3, _intProperty));

        var reader = CreateReader(memory);
        var structReader = CreateStructs(memory, objectLayout, structLayout);
        List<Address> properties = [];

        for (var type = _pawn; type != Address.Zero; type = structReader.SuperOf(type).Unwrap())
        {
            properties.AddRange(Properties(reader, type));
        }

        Address[] expected = [_property1, _property2, _property3];

        Assert.That(properties, Is.EqualTo(expected));
    }

    // A function that is its own next field fails the list after 4096 fields, so a loop without a property still stops.
    [Test]
    public void PropertiesOf_LoopingFunction_FailsAfter4096Fields()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_function, _functionClass));
        memory.WritePointer(_function + fieldNext, _function);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.StartWith($"The struct at {_pawn} has more than 4096 fields."));
            Assert.That(memory.ReadsAt(_function + objectLayout.Class), Is.EqualTo(MaxFields));
        }
    }

    // The limit counts every field on the chain, so a property behind 4096 functions fails the list and its class isn't read.
    [Test]
    public void PropertiesOf_PropertyBehind4096Functions_Fails()
    {
        var memory = MemoryWithClasses();
        memory.Add(_chain, new byte[(MaxFields + 1) * BlockSpacing]);
        memory.WritePointer(_pawn + structLayout.Children, _chain);

        for (int i = 0; i <= MaxFields; i++)
        {
            var field = _chain + (i * BlockSpacing);
            memory.WritePointer(field + objectLayout.Outer, _pawn);
            memory.WritePointer(field + objectLayout.Class, i < MaxFields ? _functionClass : _intProperty);
            memory.WritePointer(field + fieldNext, i < MaxFields ? field + BlockSpacing : Address.Zero);
        }

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].IsErr, Is.True);
            Assert.That(memory.ReadsAt(_chain + (MaxFields * BlockSpacing) + objectLayout.Class), Is.Zero);
        }
    }

    // Taking only the 1st property reads neither its next pointer nor the next field, because a caller can stop early.
    [Test]
    public void PropertiesOf_FirstOnly_StopsBeforeTheNextField()
    {
        var memory = MemoryWithChain();

        var first = CreateReader(memory).PropertiesOf(_pawn).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(_property1));
            Assert.That(memory.ReadsAt(_property1 + fieldNext), Is.Zero);
            Assert.That(memory.ReadsAt(_property2 + objectLayout.Outer), Is.Zero);
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

    // A struct at address 0 fails at the children offset and doesn't throw, because a super chain ends in that address.
    [Test]
    public void PropertiesOf_AddressZero_YieldsOneFailure()
    {
        var items = CreateReader(MemoryWithChain()).PropertiesOf(Address.Zero).ToList();
        var address = Address.Zero + structLayout.Children;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Does.StartWith($"Can't read the fields of the struct at {Address.Zero}: reading {address} failed."));
        }
    }

    // A wrong outer offset fails at field 1 with that offset, because the list reads the outer of a field before its class.
    [Test]
    public void PropertiesOf_WrongOuterOffset_FailsWithOuterOffset()
    {
        var reader = CreateReader(MemoryWithChain(), objectLayout with { Outer = PastBlock }, structLayout);

        var items = reader.PropertiesOf(_pawn).ToList();
        var address = _property1 + PastBlock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read field 1 of the struct at {_pawn}. "
                    + $"Can't read the outer object of the object at {_property1}: reading {address} failed. No block contains {address}. "
                    + $"Check ObjectLayout.Outer (0x{PastBlock:X})."));
        }
    }

    // A wrong name offset fails at the class of field 1 with that offset and doesn't drop the field as if it weren't a property.
    [Test]
    public void PropertiesOf_WrongNameOffset_FailsWithNameOffset()
    {
        var reader = CreateReader(MemoryWithChain(), objectLayout with { Name = PastBlock }, structLayout);

        var items = reader.PropertiesOf(_pawn).ToList();
        var address = _intProperty + PastBlock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the class of field 1 of the struct at {_pawn}. "
                    + $"Can't follow the super chain of the struct at {_intProperty} at level 1. "
                    + $"Can't read the name of the object at {_intProperty}: reading {address} failed. No block contains {address}. "
                    + $"Check ObjectLayout.Name (0x{PastBlock:X})."));
        }
    }

    // A wrong super offset fails at the class of field 1 with that offset and doesn't drop the field as if it weren't a property.
    [Test]
    public void PropertiesOf_WrongSuperOffset_FailsWithSuperOffset()
    {
        var reader = CreateReader(MemoryWithChain(), objectLayout, structLayout with { SuperStruct = PastBlock });

        var items = reader.PropertiesOf(_pawn).ToList();
        var address = _intProperty + PastBlock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the class of field 1 of the struct at {_pawn}. "
                    + $"Can't follow the super chain of the struct at {_intProperty} at level 1. "
                    + $"Can't read the super struct of the struct at {_intProperty}: reading {address} failed. No block contains {address}. "
                    + $"Check StructLayout.SuperStruct (0x{PastBlock:X})."));
        }
    }

    // A class up the chain with a negative name number fails, because its real name stays unknown and it can be Property.
    [Test]
    public void PropertiesOf_NegativeNumberOnClassChain_FailsWithNumber()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_numericProperty + objectLayout.Name + sizeof(int), -1);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Does.StartWith(
                    $"Can't read the class of field 1 of the struct at {_pawn}. "
                    + $"Can't follow the super chain of the struct at {_intProperty} at level 2.")
                    .And.Contain($"The object at {_numericProperty} has the name number -1"));
        }
    }

    // A garbage class pointer fails at level 1 of the class chain and the failure comes where the properties stop.
    [Test]
    public void PropertiesOf_ClassIntoUnreadableMemory_YieldsPropertyThenFailureAtLevel1()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property2 + objectLayout.Class, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();
        var address = _unreadable + objectLayout.Name;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the class of field 2 of the struct at {_pawn}. "
                    + $"Can't follow the super chain of the struct at {_unreadable} at level 1. "
                    + $"Can't read the name of the object at {_unreadable}: reading {address} failed. No block contains {address}. "
                    + $"Check ObjectLayout.Name (0x{objectLayout.Name:X})."));
        }
    }

    // The field position of an unreadable field counts the function in front of it, so it moves to field 3.
    [Test]
    public void PropertiesOf_UnreadableFieldBehindFunction_CountsTheFunction()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_property1, _intProperty), (_function, _functionClass));
        memory.WritePointer(_function + fieldNext, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[1].UnwrapErr().Message, Does.StartWith($"Can't read field 3 of the struct at {_pawn}."));
        }
    }

    // A garbage next pointer at the top of the address range wraps around to unreadable memory and ends the list with a failure.
    [Test]
    public void PropertiesOf_NextPointerAtTopOfAddressRange_YieldsPropertyThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldNext, TopOfAddressRange);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(items[1].UnwrapErr().Message, Does.StartWith($"Can't read field 2 of the struct at {_pawn}."));
        }
    }

    // A garbage class pointer at the top of the address range is a failure and doesn't throw.
    [Test]
    public void PropertiesOf_ClassAtTopOfAddressRange_YieldsOneFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + objectLayout.Class, TopOfAddressRange);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].UnwrapErr().Message, Does.Contain($"Can't read the name of the object at {TopOfAddressRange}"));
        }
    }

    // PropertiesOf reads the class chain and not the members of a property, so the list works before the measurer has found those offsets.
    [Test]
    public void PropertiesOf_OnlyChainOffsetsKnown_ReturnsProperties()
    {
        var memory = MemoryWithChain();
        ObjectPropertyReader reader = new(
            memory,
            CreateObjects(memory, objectLayout),
            CreateStructs(memory, objectLayout, structLayout),
            new PropertyLayout(UnknownOffset, UnknownOffset, UnknownOffset));

        Address[] expected = [_property1, _property2, _property3];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Properties(reader, _pawn), Is.EqualTo(expected));
            Assert.That(memory.ReadsAt(_property1 + UnknownOffset), Is.Zero);
            Assert.That(memory.ReadsAt(_property1 + objectLayout.Name), Is.Zero);
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

    // 4.23 and 4.24 have the name pool and object properties, so the class check and both reads go through the pool, with UTF-16 text too.
    [Test]
    public void PropertiesOf_NameOf_ClassNameOf_NamesInPool_ReturnTexts()
    {
        const int FunctionName = 0x40;
        const int IntPropertyName = 0x80;
        const int PropertyName = 0xC0;
        const int ValueName = 0x100;
        const string Value = "Größe";

        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, _block);
        memory.AddPoolEntry(_block + (FunctionName * 2), wide: false, "Function");
        memory.AddPoolEntry(_block + (IntPropertyName * 2), wide: false, "IntProperty");
        memory.AddPoolEntry(_block + (PropertyName * 2), wide: false, "Property");
        memory.AddPoolEntry(_block + (ValueName * 2), wide: true, Value);

        memory.AddObject(_pawn, objectLayout, FunctionName);
        memory.AddObject(_functionClass, objectLayout, FunctionName);
        memory.AddObject(_intProperty, objectLayout, IntPropertyName);
        memory.WritePointer(_intProperty + structLayout.SuperStruct, _propertyClass);
        memory.AddObject(_propertyClass, objectLayout, PropertyName);
        AddChain(memory, _pawn, (_function, _functionClass), (_property1, _intProperty));
        memory.WriteInt32(_property1 + objectLayout.Name, ValueName);

        ObjectReader objectReader = new(memory, new ChunkedNameTable(memory, _pool), objectLayout);
        StructReader structReader = new(memory, objectReader, structLayout, new ChildLayout(fieldNext));
        ObjectPropertyReader reader = new(memory, objectReader, structReader, layout);

        Address[] expected = [_property1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Properties(reader, _pawn), Is.EqualTo(expected));
            Assert.That(reader.NameOf(_property1).Unwrap(), Is.EqualTo(Value));
            Assert.That(reader.ClassNameOf(_property1).Unwrap(), Is.EqualTo("IntProperty"));
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
        memory.AddObject(_intProperty, objectLayout, 0);
        memory.AddObject(_property1, objectLayout, 0, klass: _intProperty);

        Assert.That(CreateReader(memory).ClassNameOf(_property1).Unwrap(), Is.EqualTo(className));
    }

    // A negative number on the class fails with the property and that number, because the real name of the class stays unknown.
    [Test]
    public void ClassNameOf_NegativeClassNameNumber_FailsWithPropertyAndNumber()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_intProperty + objectLayout.Name + sizeof(int), -1);

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the class name of the property at {_property1}. "
                + $"The object at {_intProperty} has the name number -1, which is below 0. "
                + $"Check ObjectLayout.Name (0x{objectLayout.Name:X})."));
    }

    // A class name id outside the table keeps the id and the failure of the table in the message.
    [Test]
    public void ClassNameOf_ClassNameIdOutsideTable_FailsWithPropertyAndId()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_intProperty + objectLayout.Name, 99);

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the class name of the property at {_property1}. "
                + $"Can't read the name of the object at {_intProperty} with the name id 99. Name 99 is outside the array of 13 names. "
                + $"Check ObjectLayout.Name (0x{objectLayout.Name:X})."));
    }

    // Every read goes to memory again, because the game can free a property and reuse its address.
    [Test]
    public void NameOf_ClassNameOf_OffsetOf_SameAddressAfterMemoryChanged_ReturnNewValues()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + layout.Offset, 0x10);
        var reader = CreateReader(memory);

        string firstName = reader.NameOf(_property1).Unwrap();
        string firstClassName = reader.ClassNameOf(_property1).Unwrap();
        int firstOffset = reader.OffsetOf(_property1).Unwrap();

        memory.WriteInt32(_property1 + objectLayout.Name, TickId);
        memory.WritePointer(_property1 + objectLayout.Class, _numericProperty);
        memory.WriteInt32(_property1 + layout.Offset, 0x20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstName, Is.EqualTo("IntValue"));
            Assert.That(firstClassName, Is.EqualTo("IntProperty"));
            Assert.That(firstOffset, Is.EqualTo(0x10));
            Assert.That(reader.NameOf(_property1).Unwrap(), Is.EqualTo("Tick"));
            Assert.That(reader.ClassNameOf(_property1).Unwrap(), Is.EqualTo("NumericProperty"));
            Assert.That(reader.OffsetOf(_property1).Unwrap(), Is.EqualTo(0x20));
        }
    }

    // A garbage property address at the top of the address range is a failure from every read and doesn't throw.
    [Test]
    public void NameOf_ClassNameOf_ArrayDimOf_PropertyAtTopOfAddressRange_Fail()
    {
        var reader = CreateReader(MemoryWithChain());
        var top = TopOfAddressRange;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.NameOf(top).UnwrapErr().Message, Does.Contain($"Can't read the name of the object at {top}"));
            Assert.That(reader.ClassNameOf(top).UnwrapErr().Message, Does.Contain($"Can't read the class of the object at {top}"));
            Assert.That(reader.ArrayDimOf(top).UnwrapErr().Message, Does.StartWith($"Can't read the array size of the property at {top}:"));
        }
    }

    // A big-endian target stores these members in that byte order, so each one reads back as the same number.
    [Test]
    public void OffsetOf_ElementSizeOf_ArrayDimOf_BigEndianMemory_ReturnValues()
    {
        byte[] bytes = new byte[BlockSpacing];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(layout.ArrayDim), 3);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(layout.ElementSize), 4);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(layout.Offset), 0x2A8);

        FakeMemory memory = new() { Layout = MemoryLayout.BigEndian32 };
        memory.Add(_property1, bytes);

        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.ArrayDimOf(_property1).Unwrap(), Is.EqualTo(3));
            Assert.That(reader.ElementSizeOf(_property1).Unwrap(), Is.EqualTo(4));
            Assert.That(reader.OffsetOf(_property1).Unwrap(), Is.EqualTo(0x2A8));
        }
    }

    private static List<Address> Properties(ObjectPropertyReader reader, Address type)
    {
        return [.. reader.PropertiesOf(type).Select(static item => item.Unwrap())];
    }

    private void AddChain(FakeMemory memory, Address type, params (Address Field, Address Class)[] fields)
    {
        memory.WritePointer(type + structLayout.Children, fields[0].Field);

        for (int i = 0; i < fields.Length; i++)
        {
            memory.AddObject(fields[i].Field, objectLayout, IntValueId, outer: type, klass: fields[i].Class);
            memory.WritePointer(fields[i].Field + fieldNext, i + 1 < fields.Length ? fields[i + 1].Field : Address.Zero);
        }
    }

    private void AddStruct(FakeMemory memory, Address type, int nameId, Address super)
    {
        memory.AddObject(type, objectLayout, nameId);
        memory.WritePointer(type + structLayout.SuperStruct, super);
    }

    // Lays out Pawn with its super structs and the class chains of IntProperty, Function, Enum and LooksLikeProperty.
    private FakeMemory MemoryWithClasses()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(
            _nameArray,
            _entryLayout,
            "Object",
            "Actor",
            "Pawn",
            "Field",
            "Property",
            "NumericProperty",
            "IntProperty",
            "Struct",
            "Function",
            "IntValue",
            "Tick",
            "Enum",
            "LooksLikeProperty");

        AddStruct(memory, _pawn, PawnId, _actor);
        AddStruct(memory, _actor, ActorId, _object);
        AddStruct(memory, _object, ObjectId, Address.Zero);
        AddStruct(memory, _intProperty, IntPropertyId, _numericProperty);
        AddStruct(memory, _numericProperty, NumericPropertyId, _propertyClass);
        AddStruct(memory, _propertyClass, PropertyId, _fieldClass);
        AddStruct(memory, _functionClass, FunctionId, _structClass);
        AddStruct(memory, _structClass, StructId, _fieldClass);
        AddStruct(memory, _enumClass, EnumId, _fieldClass);
        AddStruct(memory, _looksLikeProperty, LooksLikePropertyId, _fieldClass);
        AddStruct(memory, _fieldClass, FieldId, _objectClass);
        AddStruct(memory, _objectClass, ObjectId, Address.Zero);

        return memory;
    }

    // Gives Pawn a chain of 3 properties that ends in a null pointer.
    private FakeMemory MemoryWithChain()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, _pawn, (_property1, _intProperty), (_property2, _intProperty), (_property3, _intProperty));

        return memory;
    }

    private ObjectReader CreateObjects(FakeMemory memory, ObjectLayout objectReader)
    {
        return new ObjectReader(memory, new FlatNameTable(memory, _nameArray, _entryLayout), objectReader);
    }

    private StructReader CreateStructs(FakeMemory memory, ObjectLayout objectReader, StructLayout structReader)
    {
        return new StructReader(memory, CreateObjects(memory, objectReader), structReader, new ChildLayout(fieldNext));
    }

    private ObjectPropertyReader CreateReader(FakeMemory memory)
    {
        return CreateReader(memory, objectLayout, structLayout);
    }

    private ObjectPropertyReader CreateReader(FakeMemory memory, ObjectLayout objectReader, StructLayout structReader)
    {
        return new ObjectPropertyReader(memory, CreateObjects(memory, objectReader), CreateStructs(memory, objectReader, structReader), layout);
    }
}
