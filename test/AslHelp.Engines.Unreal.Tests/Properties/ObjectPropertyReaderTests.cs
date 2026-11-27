using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ObjectPropertyReaderTests(
    MemoryLayout memoryLayout,
    Address start,
    ObjectLayout objectLayout,
    StructLayout structLayout,
    int fieldNext,
    PropertyLayout layout)
{
    private const int ActorId = 1;
    private const int BlockSpacing = 0x100;
    private const int FieldId = 3;
    private const int FunctionId = 8;
    private const int IntPropertyId = 6;
    private const int IntValueId = 9;
    private const int NumericPropertyId = 5;
    private const int ObjectId = 0;
    private const int PastBlock = 0x200;
    private const int PawnId = 2;
    private const int PropertyId = 4;
    private const int StructId = 7;
    private const int TickId = 10;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);

    // Use the 4.0 and 4.23 x86 profiles and the 4.19 x64 Shipping PDB.
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(
            MemoryLayout.X86,
            new Address(0x8000_0000),
            new ObjectLayout(0x18, 0x10, 0x0C),
            new StructLayout(0x20, 0x24),
            0x1C,
            new PropertyLayout(0x20, 0x24, 0x3C)),
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
            new StructLayout(0x30, 0x38),
            0x28,
            new PropertyLayout(0x30, 0x34, 0x44)),
    ];

    private readonly Address _actor = start + 0x5100;
    private readonly Address _chain = start + 0x10_0000;
    private readonly Address _fieldClass = start + 0x6000;
    private readonly Address _function = start + 0x8000;
    private readonly Address _functionClass = start + 0x6100;
    private readonly Address _intProperty = start + 0x6200;
    private readonly Address _nameArray = start;
    private readonly Address _numericProperty = start + 0x6300;
    private readonly Address _object = start + 0x5200;
    private readonly Address _objectClass = start + 0x6400;
    private readonly Address _pawn = start + 0x5000;
    private readonly Address _property1 = start + 0x9000;
    private readonly Address _property2 = start + 0xA000;
    private readonly Address _property3 = start + 0xB000;
    private readonly Address _propertyClass = start + 0x6500;
    private readonly Address _structClass = start + 0x6600;
    private readonly Address _unreadable = start + 0xF000;

    // The children pointer and then the next pointers give the properties in chain order.
    [Test]
    public void PropertiesOf_ChainOfThree_ReturnsAllInOrder()
    {
        Address[] expected = [_property1, _property2, _property3];

        Assert.That(Properties(CreateReader(MemoryWithChain()), _pawn), Is.EqualTo(expected));
    }

    // 3 fields of 1 class read the super chain of that class once, because the check of a class holds for the whole enumeration.
    [Test]
    public void PropertiesOf_FieldsOfOneClass_ReadsClassChainOnce()
    {
        var memory = MemoryWithChain();

        _ = Properties(CreateReader(memory), _pawn);

        Assert.That(memory.ReadsAt(_intProperty + objectLayout.Name), Is.EqualTo(1));
    }

    // A class whose super chain changes between 2 enumerations gets checked again, because the game can free a class and reuse its address.
    [Test]
    public void PropertiesOf_ClassChainChangesBetweenCalls_ChecksClassAgain()
    {
        var memory = MemoryWithChain();
        var reader = CreateReader(memory);

        var first = Properties(reader, _pawn);
        memory.WritePointer(_intProperty + structLayout.SuperStruct, _structClass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Count.EqualTo(3));
            Assert.That(reader.PropertiesOf(_pawn), Is.Empty);
        }
    }

    // A null children pointer enumerates nothing.
    [Test]
    public void PropertiesOf_NullChildren_ReturnsNothing()
    {
        Assert.That(CreateReader(MemoryWithClasses()).PropertiesOf(_pawn), Is.Empty);
    }

    // A function on the chain stays out because its class doesn't derive from Property and the property after it comes back.
    [Test]
    public void PropertiesOf_FunctionBetweenProperties_SkipsIt()
    {
        var memory = MemoryWithChain();
        memory.AddObject(_function, objectLayout, TickId, outer: _pawn, klass: _functionClass);
        memory.WritePointer(_function + fieldNext, _property2);
        memory.WritePointer(_property1 + fieldNext, _function);

        Address[] expected = [_property1, _property2, _property3];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A chain that only has a function enumerates nothing.
    [Test]
    public void PropertiesOf_OnlyFunction_ReturnsNothing()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_function, objectLayout, TickId, outer: _pawn, klass: _functionClass);
        memory.WritePointer(_pawn + structLayout.Children, _function);

        Assert.That(CreateReader(memory).PropertiesOf(_pawn), Is.Empty);
    }

    // A chain that goes on into the properties of the super struct ends at the 1st field with another outer.
    [Test]
    public void PropertiesOf_SuperProperties_NotIncluded()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property2 + objectLayout.Outer, _actor);

        Address[] expected = [_property1];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A struct that doesn't read yields the failure of the fields as is.
    [Test]
    public void PropertiesOf_UnreadableType_YieldsOneFailure()
    {
        var items = CreateReader(MemoryWithNames()).PropertiesOf(_pawn).ToList();
        var address = _pawn + structLayout.Children;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the fields of the struct at {_pawn}: reading {address} failed. No block contains {address}. "
                    + $"Check StructLayout.Children (0x{structLayout.Children:X})."));
        }
    }

    // A next pointer into unreadable memory fails the outer read, which comes through as is after the property in front of it.
    [Test]
    public void PropertiesOf_UnreadableField_YieldsPropertyThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldNext, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(items[1].UnwrapErr().Message, Does.StartWith($"Can't read field 2 of the struct at {_pawn}."));
        }
    }

    // A wrong class offset fails at the 1st field whose class doesn't read, with the position, the field and the offset.
    [Test]
    public void PropertiesOf_UnreadableFieldClass_YieldsPropertyThenFailure()
    {
        var memory = MemoryWithChain();
        memory.AddPointers(_property1 + PastBlock, _intProperty);

        var items = CreateReader(memory, objectLayout with { Class = PastBlock }).PropertiesOf(_pawn).ToList();
        var address = _property2 + PastBlock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the class of field 2 of the struct at {_pawn}. "
                    + $"Can't read the class of the object at {_property2}: reading {address} failed. No block contains {address}. "
                    + $"Check ObjectLayout.Class (0x{PastBlock:X})."));
        }
    }

    // A class whose super chain doesn't read ends the list with a failure, so its field doesn't drop out as if it weren't a property.
    [Test]
    public void PropertiesOf_UnreadableClassChain_YieldsFailureWithLevel()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_numericProperty + structLayout.SuperStruct, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Does.StartWith(
                    $"Can't read the class of field 1 of the struct at {_pawn}. "
                    + $"Can't follow the super chain of the struct at {_intProperty} at level 3. "
                    + $"Can't read the name of the object at {_unreadable}"));
        }
    }

    // The position counts every field on the chain, so a function in front of a failure moves it to position 3.
    [Test]
    public void PropertiesOf_FailureBehindFunction_CountsTheFunction()
    {
        var memory = MemoryWithChain();
        memory.AddObject(_function, objectLayout, TickId, outer: _pawn, klass: _functionClass);
        memory.WritePointer(_function + fieldNext, _property2);
        memory.WritePointer(_property1 + fieldNext, _function);
        memory.WritePointer(_property2 + objectLayout.Class, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        Assert.That(items[^1].UnwrapErr().Message, Does.StartWith($"Can't read the class of field 3 of the struct at {_pawn}."));
    }

    // A wrong next offset yields the property in front of it and then the failure of the fields as is.
    [Test]
    public void PropertiesOf_UnreadableNextPointer_YieldsPropertyThenFailure()
    {
        var memory = MemoryWithChain();
        ObjectPropertyReader reader = new(memory, CreateObjects(memory, objectLayout), CreateStructs(memory, objectLayout, PastBlock), layout);

        var items = reader.PropertiesOf(_pawn).ToList();
        var address = _property1 + PastBlock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the next field after the field at {_property1}: reading {address} failed. No block contains {address}. "
                    + $"Check ChildLayout.Next (0x{PastBlock:X})."));
        }
    }

    // A property that is its own next property fails after 4096 items, because a damaged chain can loop.
    [Test]
    public void PropertiesOf_LoopingChain_FailsAfter4096()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldNext, _property1);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(4097));
            Assert.That(items[^1].UnwrapErr().Message, Does.StartWith($"The struct at {_pawn} has more than 4096 fields."));
        }
    }

    // A chain of 4096 ends at its null pointer and a chain of 4097 fails and doesn't read the class of the last property.
    [TestCase(4096, false)]
    [TestCase(4097, true)]
    public void PropertiesOf_ChainAroundLimit_ReturnsFirst4096AndFailsPastIt(int length, bool fails)
    {
        var memory = MemoryWithClasses();
        memory.Add(_chain, new byte[length * BlockSpacing]);
        memory.WritePointer(_pawn + structLayout.Children, _chain);

        for (int i = 0; i < length; i++)
        {
            var field = _chain + (i * BlockSpacing);
            memory.WritePointer(field + objectLayout.Outer, _pawn);
            memory.WritePointer(field + objectLayout.Class, _intProperty);
            memory.WritePointer(field + fieldNext, i + 1 < length ? field + BlockSpacing : Address.Zero);
        }

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();
        var properties = items.Where(static item => item.IsOk).Select(static item => item.Unwrap()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(properties, Has.Count.EqualTo(4096));
            Assert.That(properties[^1], Is.EqualTo(_chain + (4095 * BlockSpacing)));
            Assert.That(items[^1].IsErr, Is.EqualTo(fails));
            Assert.That(memory.ReadsAt(_chain + (4096 * BlockSpacing) + objectLayout.Class), Is.Zero);
        }
    }

    // A class that is its own super fails after 64 levels, so the list ends at that field with the failure and doesn't drop it quietly.
    [Test]
    public void PropertiesOf_FieldClassLoopingSuper_FailsAtField()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_functionClass + structLayout.SuperStruct, _functionClass);
        memory.WritePointer(_property2 + objectLayout.Class, _functionClass);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Does.StartWith($"Can't read the class of field 2 of the struct at {_pawn}. The super chain of the struct at {_functionClass} goes on past 64 levels."));
        }
    }

    // Calling PropertiesOf without enumerating it reads nothing, because the enumeration is lazy.
    [Test]
    public void PropertiesOf_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithChain();

        _ = CreateReader(memory).PropertiesOf(_pawn);

        Assert.That(memory.Reads, Is.Zero);
    }

    // A 2nd enumeration of the same result reads the memory again, because the game can free a property and reuse its address.
    [Test]
    public void PropertiesOf_EnumeratedTwice_ReadsAgain()
    {
        var memory = MemoryWithChain();
        var properties = CreateReader(memory).PropertiesOf(_pawn);

        var first = properties.Select(static item => item.Unwrap()).ToList();
        memory.WritePointer(_property1 + fieldNext, Address.Zero);

        Address[] expectedFirst = [_property1, _property2, _property3];
        Address[] expectedSecond = [_property1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(expectedFirst));
            Assert.That(properties.Select(static item => item.Unwrap()), Is.EqualTo(expectedSecond));
        }
    }

    // The class name is the base name of the class object, which doesn't have a U in front.
    [Test]
    public void ClassNameOf_IntProperty_ReturnsClassNameWithoutPrefix()
    {
        Assert.That(CreateReader(MemoryWithChain()).ClassNameOf(_property1).Unwrap(), Is.EqualTo("IntProperty"));
    }

    // A class that doesn't read fails with the property and the class offset.
    [Test]
    public void ClassNameOf_UnreadableClass_FailsWithPropertyAndClassOffset()
    {
        var address = _property1 + PastBlock;
        var reader = CreateReader(MemoryWithChain(), objectLayout with { Class = PastBlock });

        Assert.That(
            reader.ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the class name of the property at {_property1}. "
                + $"Can't read the class of the object at {_property1}: reading {address} failed. No block contains {address}. "
                + $"Check ObjectLayout.Class (0x{PastBlock:X})."));
    }

    // A class pointer of 0 fails, because a property always has a class.
    [Test]
    public void ClassNameOf_ZeroClass_FailsWithNullObject()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + objectLayout.Class, Address.Zero);

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo($"Can't read the class name of the property at {_property1}. The object pointer is 0."));
    }

    // A class whose name doesn't read fails with the property and the name offset.
    [Test]
    public void ClassNameOf_UnreadableClassName_FailsWithPropertyAndNameOffset()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + objectLayout.Class, _unreadable);

        var address = _unreadable + objectLayout.Name;

        Assert.That(
            CreateReader(memory).ClassNameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the class name of the property at {_property1}. "
                + $"Can't read the name of the object at {_unreadable}: reading {address} failed. No block contains {address}. "
                + $"Check ObjectLayout.Name (0x{objectLayout.Name:X})."));
    }

    // A failed read comes through as the failure of the object reader, with the name offset.
    [Test]
    public void NameOf_UnreadableProperty_FailsWithNameOffset()
    {
        var address = _property1 + objectLayout.Name;

        Assert.That(
            CreateReader(MemoryWithNames()).NameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_property1}: reading {address} failed. No block contains {address}. "
                + $"Check ObjectLayout.Name (0x{objectLayout.Name:X})."));
    }

    // A number on the property adds _ and the number minus 1, because NameOf returns the name as the engine prints it.
    [Test]
    public void NameOf_PropertyWithNumber_ReturnsFullName()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + objectLayout.Name + sizeof(int), 5);

        Assert.That(CreateReader(memory).NameOf(_property1).Unwrap(), Is.EqualTo("IntValue_4"));
    }

    // Each member comes from its own offset, so swapped offsets return another value.
    [Test]
    public void OffsetOf_ElementSizeOf_ArrayDimOf_ReadableProperty_ReturnValues()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + layout.ArrayDim, 3);
        memory.WriteInt32(_property1 + layout.ElementSize, 4);
        memory.WriteInt32(_property1 + layout.Offset, 0x2A8);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.ArrayDimOf(_property1).Unwrap(), Is.EqualTo(3));
            Assert.That(reader.ElementSizeOf(_property1).Unwrap(), Is.EqualTo(4));
            Assert.That(reader.OffsetOf(_property1).Unwrap(), Is.EqualTo(0x2A8));
        }
    }

    // A failed read fails with the property, the address of the read, the failed read and the layout field to check.
    [Test]
    public void OffsetOf_ElementSizeOf_ArrayDimOf_UnreadableProperty_FailWithLayoutField()
    {
        var reader = CreateReader(MemoryWithNames());
        var arrayDim = _property1 + layout.ArrayDim;
        var elementSize = _property1 + layout.ElementSize;
        var offset = _property1 + layout.Offset;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                reader.ArrayDimOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the array size of the property at {_property1}: reading {arrayDim} failed. No block contains {arrayDim}. "
                    + $"Check PropertyLayout.ArrayDim (0x{layout.ArrayDim:X})."));
            Assert.That(
                reader.ElementSizeOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the element size of the property at {_property1}: reading {elementSize} failed. No block contains {elementSize}. "
                    + $"Check PropertyLayout.ElementSize (0x{layout.ElementSize:X})."));
            Assert.That(
                reader.OffsetOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the offset of the property at {_property1}: reading {offset} failed. No block contains {offset}. "
                    + $"Check PropertyLayout.Offset (0x{layout.Offset:X})."));
        }
    }

    // A null argument throws with its parameter name.
    [Test]
    public void Constructor_NullArguments_ThrowWithParamName()
    {
        FakeMemory memory = new();
        var objects = CreateObjects(memory, objectLayout);
        var structs = CreateStructs(memory, objectLayout, fieldNext);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => new ObjectPropertyReader(null!, objects, structs, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
            Assert.That(
                () => new ObjectPropertyReader(memory, null!, structs, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("objectReader"));
            Assert.That(
                () => new ObjectPropertyReader(memory, objects, null!, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("structReader"));
            Assert.That(
                () => new ObjectPropertyReader(memory, objects, structs, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("propertyLayout"));
        }
    }

    private static List<Address> Properties(ObjectPropertyReader reader, Address type)
    {
        return [.. reader.PropertiesOf(type).Select(static item => item.Unwrap())];
    }

    private FakeMemory MemoryWithNames()
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
            "Tick");

        return memory;
    }

    // Lays out Pawn with its super structs and the class chains of IntProperty and Function down to Object.
    private FakeMemory MemoryWithClasses()
    {
        var memory = MemoryWithNames();
        AddStruct(memory, _pawn, PawnId, _actor);
        AddStruct(memory, _actor, ActorId, _object);
        AddStruct(memory, _object, ObjectId, Address.Zero);
        AddStruct(memory, _intProperty, IntPropertyId, _numericProperty);
        AddStruct(memory, _numericProperty, NumericPropertyId, _propertyClass);
        AddStruct(memory, _propertyClass, PropertyId, _fieldClass);
        AddStruct(memory, _functionClass, FunctionId, _structClass);
        AddStruct(memory, _structClass, StructId, _fieldClass);
        AddStruct(memory, _fieldClass, FieldId, _objectClass);
        AddStruct(memory, _objectClass, ObjectId, Address.Zero);

        return memory;
    }

    // Gives Pawn a chain of 3 properties that ends in a null pointer.
    private FakeMemory MemoryWithChain()
    {
        var memory = MemoryWithClasses();
        memory.WritePointer(_pawn + structLayout.Children, _property1);
        memory.AddObject(_property1, objectLayout, IntValueId, outer: _pawn, klass: _intProperty);
        memory.WritePointer(_property1 + fieldNext, _property2);
        memory.AddObject(_property2, objectLayout, IntValueId, outer: _pawn, klass: _intProperty);
        memory.WritePointer(_property2 + fieldNext, _property3);
        memory.AddObject(_property3, objectLayout, IntValueId, outer: _pawn, klass: _intProperty);

        return memory;
    }

    private void AddStruct(FakeMemory memory, Address type, int nameId, Address super)
    {
        memory.AddObject(type, objectLayout, nameId);
        memory.WritePointer(type + structLayout.SuperStruct, super);
    }

    private ObjectReader CreateObjects(FakeMemory memory, ObjectLayout objects)
    {
        return new ObjectReader(memory, new FlatNameTable(memory, _nameArray, _entryLayout), objects);
    }

    private StructReader CreateStructs(FakeMemory memory, ObjectLayout objects, int next)
    {
        return new StructReader(memory, CreateObjects(memory, objects), structLayout, new ChildLayout(next));
    }

    private ObjectPropertyReader CreateReader(FakeMemory memory)
    {
        return CreateReader(memory, objectLayout);
    }

    private ObjectPropertyReader CreateReader(FakeMemory memory, ObjectLayout objects)
    {
        return new ObjectPropertyReader(memory, CreateObjects(memory, objects), CreateStructs(memory, objects, fieldNext), layout);
    }
}
