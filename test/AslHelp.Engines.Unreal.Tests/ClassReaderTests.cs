using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ClassReaderTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    private const int ActorId = 3;
    private const int ClassId = 1;
    private const string FieldShape = "ffield";
    private const int FloatPropertyId = 7;
    private const int HpId = 9;
    private const int IntPropertyId = 6;
    private const int ObjectId = 2;
    private const int ObjectPropertyId = 8;
    private const int PawnId = 4;
    private const int PropertyId = 5;
    private const int SpeedId = 10;
    private const int TargetId = 11;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);
    private static readonly FieldClassLayout _fieldClassLayout = new(0x00);

    // Uses the 4.0 x86 profile and the 4.19 x64 Shipping PDB for uproperty and the 4.25 x86 and x64 profiles for ffield.
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), "uproperty"),
        new(MemoryLayout.X86, new Address(0x8000_0000), FieldShape),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "uproperty"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), FieldShape),
    ];

    private readonly Address _actor = start + 0x5200;
    private readonly Address _array = start + 0xC000;
    private readonly Address _arrayData = start + 0xC100;
    private readonly ChildLayout _childLayout = new(memoryLayout.PointerSize == sizeof(long) ? 0x28 : 0x1C);
    private readonly FieldPropertyLayout _fieldLayout = memoryLayout.PointerSize == sizeof(long) ? new(0x08, 0x20, 0x28) : new(0x04, 0x10, 0x14);
    private readonly FieldStructLayout _fieldStructLayout = new(memoryLayout.PointerSize == sizeof(long) ? 0x50 : 0x30);
    private readonly Address _floatPropertyClass = start + 0x6200;
    private readonly Address _hp = start + 0x9000;
    private readonly Address _intPropertyClass = start + 0x6100;
    private readonly Address _metaclass = start + 0x5000;
    private readonly Address _nameArray = start;
    private readonly Address _objectClass = start + 0x5100;
    private readonly ObjectLayout _objectLayout = memoryLayout.PointerSize == sizeof(long) ? new(0x20, 0x18, 0x10) : new(0x18, 0x10, 0x0C);
    private readonly Address _objectPropertyClass = start + 0x6300;
    private readonly Address _pawn = start + 0x5300;
    private readonly Address _pawnInstance = start + 0x5400;
    private readonly Address _propertyClass = start + 0x6000;
    private readonly PropertyLayout _propertyLayout = memoryLayout.PointerSize == sizeof(long)
        ? shape == FieldShape ? new(0x38, 0x3C, 0x4C) : new(0x30, 0x34, 0x44)
        : shape == FieldShape ? new(0x20, 0x24, 0x34) : new(0x20, 0x24, 0x3C);
    private readonly Address _speed = start + 0xA000;
    private readonly StructLayout _structLayout = memoryLayout.PointerSize == sizeof(long) ? new(0x30, 0x38) : new(0x20, 0x24);
    private readonly Address _target = start + 0xB000;
    private readonly Address _unreadable = start + 0xF000;

    private Address[] ArrayObjects => [_metaclass, _objectClass, _actor, _pawnInstance, _pawn];

    // An object whose class is Class and whose name matches comes back with both values.
    [Test]
    public void Find_LoadedClass_ReturnsItsAddressAndName()
    {
        var klass = CreateReader(MemoryWithGame()).Find("Pawn").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(klass.Address, Is.EqualTo(_pawn));
            Assert.That(klass.Name, Is.EqualTo("Pawn"));
        }
    }

    // A name that no class has fails with the name.
    [Test]
    public void Find_NoClassWithTheName_FailsWithTheName()
    {
        Assert.That(CreateReader(MemoryWithGame()).Find("Mana").UnwrapErr().Message, Is.EqualTo("Can't find a loaded class called Mana."));
    }

    // The instance is called Pawn too but its class doesn't derive from Class, so the search compares the class pointer and not the name alone.
    [Test]
    public void Find_InstanceWithTheNameOfAClass_IsNotAClass()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _actor, _pawnInstance);

        Assert.That(CreateReader(memory).Find("Pawn").UnwrapErr().Message, Is.EqualTo("Can't find a loaded class called Pawn."));
    }

    // Find compares ordinally, like StructReader.DerivesFrom, so a name in another case fails.
    [Test]
    public void Find_NameInOtherCase_Fails()
    {
        Assert.That(CreateReader(MemoryWithGame()).Find("pawn").IsErr, Is.True);
    }

    // A miss doesn't get kept, so a class added to the array after the 1st call shows up on the 2nd.
    [Test]
    public void Find_ClassLoadedAfterAMiss_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _objectClass, _actor, _pawn);
        var reader = CreateReader(memory);

        var miss = reader.Find("Pawn");
        memory.WriteInt32(_array + memoryLayout.PointerSize, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(miss.IsErr, Is.True);
            Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
        }
    }

    // A found class gets kept, so the 2nd call only reads the name and the class pointer of the class and not the array.
    [Test]
    public void Find_SecondCall_ReadsTheNameAndTheClassPointer()
    {
        var memory = MemoryWithGame();
        var reader = CreateReader(memory);

        var first = reader.Find("Pawn").Unwrap();
        int reads = memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find("Pawn").Unwrap(), Is.SameAs(first));
            Assert.That(memory.Reads, Is.EqualTo(reads + 2));
        }
    }

    // A kept class whose address now has another name gets searched again, so a freed class doesn't come back.
    [Test]
    public void Find_KeptClassAddressReused_SearchesAgain()
    {
        var memory = MemoryWithGame();
        var reader = CreateReader(memory);

        _ = reader.Find("Pawn").Unwrap();
        memory.WriteInt32(_pawn + _objectLayout.Name, TargetId);

        Assert.That(reader.Find("Pawn").UnwrapErr().Message, Is.EqualTo("Can't find a loaded class called Pawn."));
    }

    // An object that doesn't read gets skipped, so the class after it is found.
    [Test]
    public void Find_UnreadableObjectInTheArray_SkipsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 3, _unreadable, _metaclass, _pawn);

        Assert.That(CreateReader(memory).Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // An object that doesn't read fails a search without a match with that read, so a broken array doesn't look like a miss.
    [Test]
    public void Find_UnreadableObjectAndNoMatch_FailsWithTheCause()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _unreadable, _actor);

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Does.StartWith("Can't search the object array for the class Pawn. ").And.Contain($"of the object at {_unreadable}"));
    }

    // The search and the class of an object keep their classes in 1 place, so both return the same instance.
    [Test]
    public void Find_ThenClassOfAnInstance_ReturnsTheSameClass()
    {
        var reader = CreateReader(MemoryWithGame());

        Assert.That(reader.ClassOf(_pawnInstance).Unwrap(), Is.SameAs(reader.Find("Pawn").Unwrap()));
    }

    // Empty positions of the array don't fail the search and the class after them is found.
    [Test]
    public void Find_EmptyPositions_SkipsThem()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 4, Address.Zero, _metaclass, Address.Zero, _pawn);

        Assert.That(CreateReader(memory).Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // An array whose items don't read fails with the name and the failure of the array.
    [Test]
    public void Find_UnreadableArray_FailsWithTheNameAndTheCause()
    {
        var memory = MemoryWithClasses();
        memory.AddPointers(_array, _unreadable, new Address(3));

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Does.StartWith("Can't search the object array for the class Pawn. ").And.Contain($"No block contains {_unreadable}."));
    }

    // A wrong class offset gives a chain that never ends at Class, which fails with the offset instead of not found.
    [Test]
    public void Find_ClassChainWithoutClass_FailsWithTheClassOffset()
    {
        var memory = MemoryWithGame();
        memory.WriteInt32(_metaclass + _objectLayout.Name, ObjectId);

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Is.EqualTo(
                $"Can't search the object array for the class Pawn. The class chain of the object at {_metaclass} doesn't end at Class. "
                + $"Check ObjectLayout.Class (0x{_objectLayout.Class:X})."));
    }

    // An instance of Pawn returns Pawn and not the class that Pawn derives from.
    [Test]
    public void ClassOf_Instance_ReturnsItsRealClass()
    {
        var klass = CreateReader(MemoryWithGame()).ClassOf(_pawnInstance).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(klass.Address, Is.EqualTo(_pawn));
            Assert.That(klass.Name, Is.EqualTo("Pawn"));
        }
    }

    // An object of 0 fails with the failure of the object reader.
    [Test]
    public void ClassOf_NullObject_FailsWithObjectNull()
    {
        Assert.That(CreateReader(MemoryWithGame()).ClassOf(Address.Zero).UnwrapErr().Message, Is.EqualTo("The object pointer is 0."));
    }

    // A class pointer of 0 fails with the class offset, because every object has a class.
    [Test]
    public void ClassOf_ClassPointerZero_FailsWithTheClassOffset()
    {
        var memory = MemoryWithGame();
        memory.WritePointer(_pawnInstance + _objectLayout.Class, Address.Zero);

        Assert.That(
            CreateReader(memory).ClassOf(_pawnInstance).UnwrapErr().Message,
            Is.EqualTo($"The class pointer of the object at {_pawnInstance} is 0. Check ObjectLayout.Class (0x{_objectLayout.Class:X})."));
    }

    // A class whose name doesn't read fails with that read and doesn't get kept, so the call works once the name reads.
    [Test]
    public void ClassOf_UnreadableClassName_FailsAndIsNotCached()
    {
        var memory = MemoryWithGame();
        memory.WritePointer(_pawnInstance + _objectLayout.Class, _unreadable);
        var reader = CreateReader(memory);

        var failure = reader.ClassOf(_pawnInstance);
        memory.AddObject(_unreadable, _objectLayout, PawnId, klass: _metaclass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Does.StartWith($"Can't read the name of the object at {_unreadable}: "));
            Assert.That(reader.ClassOf(_pawnInstance).Unwrap().Address, Is.EqualTo(_unreadable));
        }
    }

    // A kept class whose address now has another name comes back as a new class with that name.
    [Test]
    public void ClassOf_ClassAddressReused_ReturnsTheNewClass()
    {
        var memory = MemoryWithGame();
        var reader = CreateReader(memory);

        var first = reader.ClassOf(_pawnInstance).Unwrap();
        memory.WriteInt32(_pawn + _objectLayout.Name, ActorId);
        var second = reader.ClassOf(_pawnInstance).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Name, Is.EqualTo("Actor"));
        }
    }

    // The properties of the class come 1st, then the properties of each super class, each with the values of its shape.
    [Test]
    public void PropertiesOf_DerivedClass_ReturnsOwnThenInherited()
    {
        var propertyReader = CreateReader(MemoryWithGame()).PropertiesOf(_pawn).Select(static item => item.Unwrap());

        (string, string, int, int)[] expected =
        [
            ("Speed", "FloatProperty", 0x2B0, sizeof(float)),
            ("Target", "ObjectProperty", 0x2B8, memoryLayout.PointerSize),
            ("Hp", "IntProperty", 0x2A8, sizeof(int)),
        ];

        Assert.That(
            propertyReader.Select(static property => (property.Name, property.ClassName, property.Offset, property.ElementSize)),
            Is.EqualTo(expected));
    }

    // A super class that doesn't read ends the list with its failure after the properties of the class.
    [Test]
    public void PropertiesOf_UnreadableSuperClass_EndsWithTheFailure()
    {
        var memory = MemoryWithGame();
        memory.WritePointer(_pawn + _structLayout.SuperStruct, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[1].Unwrap().Name, Is.EqualTo("Target"));
            Assert.That(items[2].UnwrapErr().Message, Does.Contain($"of the struct at {_unreadable}: reading"));
        }
    }

    // A class that is its own super ends with the limit failure after 64 levels instead of running on.
    [Test]
    public void PropertiesOf_LoopingSuperChain_FailsAfter64Levels()
    {
        var memory = MemoryWithGame();
        memory.WritePointer(_pawn + _structLayout.SuperStruct, _pawn);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo((64 * 2) + 1));
            Assert.That(
                items[^1].UnwrapErr().Message,
                Is.EqualTo(
                    $"The super chain of the struct at {_pawn} goes on past 64 levels. "
                    + $"Check StructLayout.SuperStruct (0x{_structLayout.SuperStruct:X})."));
        }
    }

    // Calling PropertiesOf without enumerating it reads nothing, because the enumeration is lazy.
    [Test]
    public void PropertiesOf_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithGame();

        _ = CreateReader(memory).PropertiesOf(_pawn);

        Assert.That(memory.Reads, Is.Zero);
    }

    // Hp is declared on Actor and comes back through Pawn with the offset of the declaration.
    [Test]
    public void FindProperty_InheritedProperty_ReturnsItFromTheDerivedClass()
    {
        var reader = CreateReader(MemoryWithGame());

        var property = reader.ClassOf(_pawnInstance).Unwrap().FindProperty("Hp").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.Name, Is.EqualTo("Hp"));
            Assert.That(property.Offset, Is.EqualTo(0x2A8));
        }
    }

    // Target is declared on Pawn only, so Actor doesn't find it.
    [Test]
    public void FindProperty_DeclaredOnDerivedOnly_MissesOnTheBase()
    {
        var reader = CreateReader(MemoryWithGame());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find("Pawn").Unwrap().FindProperty("Target").Unwrap().Offset, Is.EqualTo(0x2B8));
            Assert.That(
                reader.Find("Actor").Unwrap().FindProperty("Target").UnwrapErr().Message,
                Is.EqualTo("Class Actor doesn't have a property Target."));
        }
    }

    // A name that no class on the chain declares fails with the class and the name.
    [Test]
    public void FindProperty_UnknownName_FailsWithTheClassAndTheName()
    {
        Assert.That(
            CreateReader(MemoryWithGame()).Find("Pawn").Unwrap().FindProperty("Mana").UnwrapErr().Message,
            Is.EqualTo("Class Pawn doesn't have a property Mana."));
    }

    // The properties of a class get kept, so a 2nd lookup doesn't read, also for another name.
    [Test]
    public void FindProperty_SecondCall_ReadsNoMemory()
    {
        var memory = MemoryWithGame();
        var klass = CreateReader(memory).Find("Pawn").Unwrap();

        _ = klass.FindProperty("Hp").Unwrap();
        int reads = memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(klass.FindProperty("Speed").Unwrap().Offset, Is.EqualTo(0x2B0));
            Assert.That(memory.Reads, Is.EqualTo(reads));
        }
    }

    // A failed read of the properties fails the lookup with its cause and doesn't get kept, so the lookup works once the memory reads.
    [Test]
    public void FindProperty_FailedEnumeration_IsNotCached()
    {
        var memory = MemoryWithGame();
        var klass = CreateReader(memory).Find("Pawn").Unwrap();
        memory.WritePointer(_pawn + _structLayout.SuperStruct, _unreadable);

        var failure = klass.FindProperty("Hp");
        memory.WritePointer(_pawn + _structLayout.SuperStruct, _actor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Does.Contain($"of the struct at {_unreadable}: reading"));
            Assert.That(klass.FindProperty("Hp").Unwrap().Offset, Is.EqualTo(0x2A8));
        }
    }

    // A property read through the shape of the fixture reaches the visit method of its kind, FloatProperty included.
    [TestCase("Hp", nameof(IPropertyVisitor<>.VisitInt32))]
    [TestCase("Target", nameof(IPropertyVisitor<>.VisitObject))]
    [TestCase("Speed", nameof(IPropertyVisitor<>.VisitUnsupported))]
    public void Accept_PropertyFromFindProperty_VisitsTheMethodOfItsKind(string name, string expected)
    {
        var property = CreateReader(MemoryWithGame()).Find("Pawn").Unwrap().FindProperty(name).Unwrap();

        Assert.That(property.Accept(new KindVisitor()).Kind, Is.EqualTo(expected));
    }

    private FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(
            _nameArray,
            _entryLayout,
            "None",
            "Class",
            "Object",
            "Actor",
            "Pawn",
            "Property",
            "IntProperty",
            "FloatProperty",
            "ObjectProperty",
            "Hp",
            "Speed",
            "Target");

        return memory;
    }

    // Lays out Class, Pawn and its super classes with their properties, the property classes of the shape and an instance of Pawn.
    private FakeMemory MemoryWithClasses()
    {
        var memory = MemoryWithNames();
        AddClass(memory, _metaclass, ClassId, Address.Zero);
        AddClass(memory, _objectClass, ObjectId, Address.Zero);
        AddClass(memory, _actor, ActorId, _objectClass);
        AddClass(memory, _pawn, PawnId, _actor);
        memory.AddObject(_pawnInstance, _objectLayout, PawnId, klass: _pawn);

        if (shape == FieldShape)
        {
            memory.AddFieldClass(_intPropertyClass, _fieldClassLayout, IntPropertyId);
            memory.AddFieldClass(_floatPropertyClass, _fieldClassLayout, FloatPropertyId);
            memory.AddFieldClass(_objectPropertyClass, _fieldClassLayout, ObjectPropertyId);
        }
        else
        {
            AddClass(memory, _propertyClass, PropertyId, Address.Zero);
            AddClass(memory, _intPropertyClass, IntPropertyId, _propertyClass);
            AddClass(memory, _floatPropertyClass, FloatPropertyId, _propertyClass);
            AddClass(memory, _objectPropertyClass, ObjectPropertyId, _propertyClass);
        }

        AddProperty(memory, _actor, _hp, HpId, _intPropertyClass, 0x2A8, sizeof(int), Address.Zero);
        AddProperty(memory, _pawn, _speed, SpeedId, _floatPropertyClass, 0x2B0, sizeof(float), _target);
        AddProperty(memory, _pawn, _target, TargetId, _objectPropertyClass, 0x2B8, memoryLayout.PointerSize, Address.Zero);
        memory.WritePointer(_actor + FirstPropertyOffset(), _hp);
        memory.WritePointer(_pawn + FirstPropertyOffset(), _speed);

        return memory;
    }

    // Adds an array of Class, the classes and the instance of Pawn.
    private FakeMemory MemoryWithGame()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, ArrayObjects.Length, ArrayObjects);

        return memory;
    }

    private void AddClass(FakeMemory memory, Address type, int nameId, Address super)
    {
        memory.AddObject(type, _objectLayout, nameId, klass: _metaclass);
        memory.WritePointer(type + _structLayout.SuperStruct, super);
    }

    // Adds a property in the shape of the fixture, with the class of its kind and its next property.
    private void AddProperty(
        FakeMemory memory,
        Address owner,
        Address property,
        int nameId,
        Address kind,
        int offset,
        int elementSize,
        Address next)
    {
        if (shape == FieldShape)
        {
            memory.AddField(property, _fieldLayout, nameId, kind, next);
        }
        else
        {
            memory.AddObject(property, _objectLayout, nameId, outer: owner, klass: kind);
            memory.WritePointer(property + _childLayout.Next, next);
        }

        memory.WriteInt32(property + _propertyLayout.ElementSize, elementSize);
        memory.WriteInt32(property + _propertyLayout.Offset, offset);
    }

    private int FirstPropertyOffset()
    {
        return shape == FieldShape ? _fieldStructLayout.ChildProperties : _structLayout.Children;
    }

    private ClassReader CreateReader(FakeMemory memory)
    {
        FlatNameTable names = new(memory, _nameArray, _entryLayout);
        ObjectReader objectReader = new(memory, names, _objectLayout);
        StructReader structReader = new(memory, objectReader, _structLayout, _childLayout);
        IPropertyReader propertyReader = shape == FieldShape
            ? new FieldPropertyReader(memory, names, _fieldStructLayout, _fieldLayout, _fieldClassLayout, _propertyLayout)
            : new ObjectPropertyReader(memory, objectReader, structReader, _propertyLayout);

        return new ClassReader(objectReader, structReader, propertyReader, new PointerObjectArray(memory, _array));
    }
}
