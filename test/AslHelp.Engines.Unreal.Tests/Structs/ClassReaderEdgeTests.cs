using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Structs;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ClassReaderEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    private const int ActorId = 3;
    private const int BlueprintClassId = 9;
    private const int BlueprintPawnId = 11;
    private const int ClassId = 1;
    private const int EntrySpacing = 0x100;
    private const string FieldShape = "ffield";
    private const int HpId = 8;
    private const int IntPropertyId = 6;
    private const int ObjectId = 2;
    private const int PawnId = 4;
    private const int PropertyId = 5;
    private const int WideId = 10;
    private const string WideName = "Größe";

    private static readonly NameEntryLayout _entryLayout = new(0, 12);
    private static readonly FieldClassLayout _fieldClassLayout = new(0x00);

    // Uses the same profiles as ClassReaderTests.
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
    private readonly Address _blueprintClass = start + 0x7000;
    private readonly Address _blueprintInstance = start + 0x7100;
    private readonly Address _blueprintMetaclass = start + 0x7200;
    private readonly Address _chain = start + 0x10_0000;
    private readonly ChildLayout _childLayout = new(memoryLayout.PointerSize == sizeof(long) ? 0x28 : 0x1C);
    private readonly FieldPropertyLayout _fieldLayout = memoryLayout.PointerSize == sizeof(long) ? new(0x08, 0x20, 0x28) : new(0x04, 0x10, 0x14);
    private readonly FieldStructLayout _fieldStructLayout = new(memoryLayout.PointerSize == sizeof(long) ? 0x50 : 0x30);
    private readonly Address _hp = start + 0x9000;
    private readonly Address _intPropertyClass = start + 0x6100;
    private readonly Address _loopA = start + 0x8000;
    private readonly Address _loopB = start + 0x8100;
    private readonly Address _loopC = start + 0x8200;
    private readonly Address _metaclass = start + 0x5000;
    private readonly Address _nameArray = start;
    private readonly Address _nameData = start + 0x100;
    private readonly Address _nameEntries = start + 0x1000;
    private readonly Address _objectClass = start + 0x5100;
    private readonly ObjectLayout _objectLayout = memoryLayout.PointerSize == sizeof(long) ? new(0x20, 0x18, 0x10) : new(0x18, 0x10, 0x0C);
    private readonly Address _orphan = start + 0x5600;
    private readonly Address _pawn = start + 0x5300;
    private readonly Address _pawnHp = start + 0x9100;
    private readonly Address _pawnInstance = start + 0x5400;
    private readonly Address _propertyClass = start + 0x6000;
    private readonly PropertyLayout _propertyLayout = memoryLayout.PointerSize == sizeof(long)
        ? shape == FieldShape ? new(0x38, 0x3C, 0x4C) : new(0x30, 0x34, 0x44)
        : shape == FieldShape ? new(0x20, 0x24, 0x34) : new(0x20, 0x24, 0x3C);
    private readonly Address _secondPawn = start + 0x5500;
    private readonly StructLayout _structLayout = memoryLayout.PointerSize == sizeof(long) ? new(0x30, 0x38) : new(0x20, 0x24);
    private readonly Address _wideClass = start + 0x5700;
    private readonly Address _widgetMetaclass = start + 0x7300;

    // An array with a count of 0 is a miss with the name and not a failure of the search.
    [Test]
    public void Find_EmptyArray_FailsWithNotFound()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 0);

        Assert.That(CreateReader(memory).Find("Pawn").UnwrapErr().Message, Is.EqualTo("Can't find a loaded class called Pawn."));
    }

    // A count larger than the pointers that can be read fails the search, so a cut-off array doesn't look like a miss.
    [Test]
    public void Find_CountPastTheData_FailsWithTheName()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 4, _metaclass, _objectClass);

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Does.StartWith("Can't search the object array for the class Pawn. ").And.Not.Contain("Can't find a loaded class"));
    }

    // A negative count fails the search with the name before any slot gets read.
    [Test]
    public void Find_NegativeCount_FailsWithTheName()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, -1, _metaclass, _pawn);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CreateReader(memory).Find("Pawn").UnwrapErr().Message, Does.StartWith("Can't search the object array for the class Pawn. "));
            Assert.That(memory.ReadsAt(_arrayData), Is.Zero);
        }
    }

    // An object whose class pointer is 0 in front of the classes gets skipped, so the search finds Class from the next object.
    [Test]
    public void Find_ObjectWithoutClassFirst_SkipsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_orphan, _objectLayout, ObjectId);
        memory.AddNameArray(_array, _arrayData, 3, _orphan, _metaclass, _pawn);

        Assert.That(CreateReader(memory).Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // The 1st object is an instance of a Blueprint class 3 class steps from Class. The search still finds Class.
    [Test]
    public void Find_BlueprintInstanceFirst_FindsTheClass()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_blueprintMetaclass, _objectLayout, BlueprintClassId, klass: _metaclass);
        memory.AddObject(_blueprintClass, _objectLayout, ActorId, klass: _blueprintMetaclass);
        memory.AddObject(_blueprintInstance, _objectLayout, ActorId, klass: _blueprintClass);
        memory.AddNameArray(_array, _arrayData, 3, _blueprintInstance, _metaclass, _actor);

        Assert.That(CreateReader(memory).Find("Actor").Unwrap().Address, Is.EqualTo(_actor));
    }

    // A class chain that loops between 2 classes never reaches a class that is its own class, so the search fails with the class offset.
    [Test]
    public void Find_ClassChainLoopsBetweenTwoClasses_FailsWithTheClassOffset()
    {
        var memory = MemoryWithClasses();
        AddLoop(memory);
        memory.AddNameArray(_array, _arrayData, 1, _loopA);

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Is.EqualTo(
                $"Can't search the object array for the class Pawn. The class chain of the object at {_loopA} doesn't end at Class. "
                + $"Check ObjectLayout.Class (0x{_objectLayout.Class:X})."));
    }

    // An object with a looping class chain in front of the classes gets skipped, so the search still finds Pawn.
    [Test]
    public void Find_LoopingClassChainFirst_SkipsIt()
    {
        var memory = MemoryWithClasses();
        AddLoop(memory);
        memory.AddNameArray(_array, _arrayData, 3, _loopA, _metaclass, _pawn);

        Assert.That(CreateReader(memory).Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // ClassOf of a Blueprint instance keeps its class called Actor, but the search returns the Actor of the array.
    [Test]
    public void Find_AfterClassOfABlueprintInstance_ReturnsTheClassFromTheArray()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_blueprintMetaclass, _objectLayout, BlueprintClassId, klass: _metaclass);
        memory.AddObject(_blueprintClass, _objectLayout, ActorId, klass: _blueprintMetaclass);
        memory.AddObject(_blueprintInstance, _objectLayout, ActorId, klass: _blueprintClass);
        memory.AddNameArray(_array, _arrayData, 3, _blueprintInstance, _metaclass, _actor);
        var reader = CreateReader(memory);

        _ = reader.ClassOf(_blueprintInstance).Unwrap();

        Assert.That(reader.Find("Actor").Unwrap().Address, Is.EqualTo(_actor));
    }

    // The class of a Blueprint class derives from Class over 2 super classes, so the search finds the Blueprint class too.
    [Test]
    public void Find_BlueprintClass_ReturnsIt()
    {
        var memory = MemoryWithBlueprintClass();

        Assert.That(CreateReader(memory).Find("BP_Pawn_C").Unwrap().Address, Is.EqualTo(_blueprintClass));
    }

    // A kept Blueprint class whose address now has an object of another class gets searched again, so a freed class doesn't come back.
    [Test]
    public void Find_KeptBlueprintClassIsNoClassAnymore_FailsWithNotFound()
    {
        var memory = MemoryWithBlueprintClass();
        var reader = CreateReader(memory);

        _ = reader.Find("BP_Pawn_C").Unwrap();
        memory.AddObject(_blueprintClass, _objectLayout, BlueprintPawnId, klass: _pawn);

        Assert.That(reader.Find("BP_Pawn_C").UnwrapErr().Message, Is.EqualTo("Can't find a loaded class called BP_Pawn_C."));
    }

    // The search keeps whether a class derives from Class, so a 2nd instance of Pawn doesn't read the super pointer of Pawn again.
    [Test]
    public void Find_TwoInstancesOfOneClass_ReadsItsSuperOnce()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_orphan, _objectLayout, PawnId, klass: _pawn);
        memory.AddNameArray(_array, _arrayData, 4, _metaclass, _pawnInstance, _orphan, _actor);

        _ = CreateReader(memory).Find("Actor").Unwrap();

        Assert.That(memory.ReadsAt(_pawn + _structLayout.SuperStruct), Is.EqualTo(1));
    }

    // A class whose super pointer doesn't read fails the search with that read when nothing matches.
    [Test]
    public void Find_UnreadableSuperOfTheClassOfAnObject_FailsWithTheCause()
    {
        var memory = MemoryWithNames();
        AddClass(memory, _metaclass, ClassId, Address.Zero);
        memory.AddObject(_orphan, _objectLayout, ObjectId, klass: _loopA);
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _orphan);

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Does.StartWith("Can't search the object array for the class Pawn. Can't read the super struct of the struct at " + _loopA));
    }

    // A class whose super chain loops fails the search with the limit when nothing matches.
    [Test]
    public void Find_LoopingSuperChainOfTheClassOfAnObject_FailsWithTheLimit()
    {
        var memory = MemoryWithNames();
        AddClass(memory, _metaclass, ClassId, Address.Zero);
        AddClass(memory, _loopA, ActorId, _loopA);
        memory.AddObject(_orphan, _objectLayout, ObjectId, klass: _loopA);
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _orphan);

        Assert.That(
            CreateReader(memory).Find("Pawn").UnwrapErr().Message,
            Is.EqualTo(
                $"Can't search the object array for the class Pawn. The super chain of the struct at {_loopA} goes on past 64 levels. "
                + $"Check StructLayout.SuperStruct (0x{_structLayout.SuperStruct:X})."));
    }

    // A search keeps the object of each slot, so a 2nd miss on the same array doesn't read an object or a class again.
    [Test]
    public void Find_SecondMissOnTheSameArray_ReadsNoObjectAgain()
    {
        var memory = MemoryWithArray();
        var reader = CreateReader(memory);
        Address[] objects = [_metaclass, _objectClass, _actor, _pawnInstance, _pawn];

        _ = reader.Find("Mana").UnwrapErr();
        int[] reads = [.. objects.Select(obj => memory.ReadsAt(obj + _objectLayout.Class) + memory.ReadsAt(obj + _objectLayout.Name))];
        _ = reader.Find("Mana").UnwrapErr();

        Assert.That(
            objects.Select(obj => memory.ReadsAt(obj + _objectLayout.Class) + memory.ReadsAt(obj + _objectLayout.Name)),
            Is.EqualTo(reads));
    }

    // A search keeps every class of the array by name, so the 1st call for another class only checks its name and its class pointer.
    [Test]
    public void Find_OtherClassAfterASearch_ReadsOnlyItsNameAndClassPointer()
    {
        var memory = MemoryWithArray();
        var reader = CreateReader(memory);

        _ = reader.Find("Pawn").Unwrap();
        int reads = memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find("Actor").Unwrap().Address, Is.EqualTo(_actor));
            Assert.That(memory.Reads, Is.EqualTo(reads + 2));
        }
    }

    // A class in a slot that was empty at the last search gets read and the objects of the other slots don't get read again.
    [Test]
    public void Find_ClassInAChangedSlotAfterAMiss_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _pawnInstance, Address.Zero);
        var reader = CreateReader(memory);

        _ = reader.Find("Actor").UnwrapErr();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _pawnInstance, _actor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find("Actor").Unwrap().Address, Is.EqualTo(_actor));
            Assert.That(memory.ReadsAt(_pawnInstance + _objectLayout.Class), Is.EqualTo(1));
        }
    }

    // An object that didn't read doesn't count as read, so the next search reads it again in the same slot.
    [Test]
    public void Find_UnreadableObjectReadsLater_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _orphan);
        var reader = CreateReader(memory);

        _ = reader.Find("Object").UnwrapErr();
        AddClass(memory, _orphan, ObjectId, Address.Zero);

        Assert.That(reader.Find("Object").Unwrap().Address, Is.EqualTo(_orphan));
    }

    // A kept class that stopped being a class makes the search read every slot again, so the 2nd class with the name comes back.
    [Test]
    public void Find_KeptClassGoneAndSecondClassWithTheName_ReturnsTheSecond()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _secondPawn, PawnId, _actor);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _secondPawn, _pawn);
        var reader = CreateReader(memory);

        _ = reader.Find("Pawn").Unwrap();
        memory.AddObject(_secondPawn, _objectLayout, PawnId, klass: _pawn);

        Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // A slot past a smaller count gets cleared, so a class at the address of the old object gets read when the array grows again.
    [Test]
    public void Find_ArrayShrinksAndGrowsWithAClassAtTheOldAddress_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_orphan, _objectLayout, PawnId, klass: _pawn);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _actor, _orphan);
        var reader = CreateReader(memory);

        _ = reader.Find("Object").UnwrapErr();
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _actor);
        _ = reader.Find("Object").UnwrapErr();
        AddClass(memory, _orphan, ObjectId, Address.Zero);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _actor, _orphan);

        Assert.That(reader.Find("Object").Unwrap().Address, Is.EqualTo(_orphan));
    }

    // A slot whose object changed to a class gets read, so the class comes back after a miss.
    [Test]
    public void Find_SlotChangesFromAnInstanceToAClass_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _pawnInstance);
        var reader = CreateReader(memory);

        _ = reader.Find("Actor").UnwrapErr();
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _actor);

        Assert.That(reader.Find("Actor").Unwrap().Address, Is.EqualTo(_actor));
    }

    // A class that loads into a slot in front of a kept class with the same name doesn't replace it,
    // so the name gives the same class while that class is loaded.
    [Test]
    public void Find_ClassLoadsInFrontOfAKeptClassWithTheName_ReturnsTheKept()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _secondPawn, PawnId, _actor);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, Address.Zero, _pawn);
        var reader = CreateReader(memory);

        _ = reader.Find("Mana").UnwrapErr();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _secondPawn, _pawn);
        _ = reader.Find("Mana").UnwrapErr();

        Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // A class in the part of the array that reads comes back on the 1st call, although a later part of the array doesn't read.
    [Test]
    public void Find_ClassInFrontOfAnUnreadablePartOfTheArray_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        var objects = new Address[1024];
        objects[0] = _metaclass;
        objects[1] = _pawn;
        memory.AddNameArray(_array, _arrayData, 2000, objects);

        Assert.That(CreateReader(memory).Find("Pawn").Unwrap().Address, Is.EqualTo(_pawn));
    }

    // A kept class still comes back after a search for another kept class found that class gone and read every slot again.
    [Test]
    public void Find_KeptClassAfterAnotherKeptClassIsGone_ReturnsIt()
    {
        var memory = MemoryWithArray();
        var reader = CreateReader(memory);

        _ = reader.Find("Pawn").Unwrap();
        memory.AddObject(_pawn, _objectLayout, PawnId, klass: _actor);
        _ = reader.Find("Pawn").UnwrapErr();

        Assert.That(reader.Find("Actor").Unwrap().Address, Is.EqualTo(_actor));
    }

    // A Blueprint class that takes both the slot and the address of a freed Blueprint class comes back,
    // because a search reads the names of the kept Blueprint classes again.
    [Test]
    public void Find_BlueprintClassAtTheSlotAndAddressOfAFreedOne_ReturnsIt()
    {
        var memory = MemoryWithBlueprintClass();
        var reader = CreateReader(memory);

        _ = reader.Find("Mana").UnwrapErr();
        memory.AddObject(_blueprintClass, _objectLayout, WideId, klass: _widgetMetaclass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find(WideName).Unwrap().Address, Is.EqualTo(_blueprintClass));
            Assert.That(reader.Find("BP_Pawn_C").UnwrapErr().Message, Is.EqualTo("Can't find a loaded class called BP_Pawn_C."));
        }
    }

    // A slot that had a class of C++ code and now has another object takes the name out of the kept names,
    // so a class with that name in another slot comes back.
    [Test]
    public void Find_SlotOfAKeptClassChangesAndTheClassIsInAnotherSlot_ReturnsTheNewOne()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _secondPawn, PawnId, _actor);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _pawn, Address.Zero);
        var reader = CreateReader(memory);

        _ = reader.Find("Mana").UnwrapErr();
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _pawnInstance, _secondPawn);
        _ = reader.Find("Mana").UnwrapErr();

        Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_secondPawn));
    }

    // 2 Blueprint classes have 1 name and only the 1st is kept, so a search that finds the 1st gone reads every slot again
    // and the 2nd comes back.
    [Test]
    public void Find_FirstOfTwoBlueprintClassesWithOneNameIsGone_ReturnsTheSecond()
    {
        var memory = MemoryWithBlueprintClass();
        memory.AddObject(_blueprintInstance, _objectLayout, BlueprintPawnId, klass: _widgetMetaclass);
        memory.AddNameArray(_array, _arrayData, 5, _metaclass, _blueprintMetaclass, _widgetMetaclass, _blueprintClass, _blueprintInstance);
        var reader = CreateReader(memory);

        _ = reader.Find("Mana").UnwrapErr();
        memory.AddObject(_blueprintClass, _objectLayout, WideId, klass: _widgetMetaclass);
        _ = reader.Find("Mana").UnwrapErr();

        Assert.That(reader.Find("BP_Pawn_C").Unwrap().Address, Is.EqualTo(_blueprintInstance));
    }

    // A class that takes both the slot and the address of an instance isn't found until a search reads every slot again.
    [Test]
    public void Find_ClassAtTheSlotAndAddressOfAFreedInstance_IsNotFoundUntilAKeptClassIsGone()
    {
        var memory = MemoryWithClasses();
        memory.AddObject(_orphan, _objectLayout, PawnId, klass: _pawn);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _actor, _orphan);
        var reader = CreateReader(memory);

        _ = reader.Find("Actor").Unwrap();
        AddClass(memory, _orphan, ObjectId, Address.Zero);
        var miss = reader.Find("Object");
        memory.AddObject(_actor, _objectLayout, ActorId, klass: _pawn);
        _ = reader.Find("Actor").UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(miss.IsOk, Is.False);
            Assert.That(reader.Find("Object").Unwrap().Address, Is.EqualTo(_orphan));
        }
    }

    // 2 classes with 1 name return the 1st in array order, also after ClassOf kept the 2nd.
    [Test]
    public void Find_TwoClassesWithOneNameAfterClassOfTheSecond_ReturnsTheFirst()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _secondPawn, PawnId, _actor);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _secondPawn, _pawn);
        var reader = CreateReader(memory);

        _ = reader.ClassOf(_pawnInstance).Unwrap();

        Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_secondPawn));
    }

    // 2 classes with 1 name return the 1st in array order, also on the 2nd call when the classes are kept.
    [Test]
    public void Find_TwoClassesWithOneName_ReturnsTheFirstInArrayOrder()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _secondPawn, PawnId, _actor);
        memory.AddNameArray(_array, _arrayData, 3, _metaclass, _secondPawn, _pawn);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_secondPawn));
            Assert.That(reader.Find("Pawn").Unwrap().Address, Is.EqualTo(_secondPawn));
        }
    }

    // A class the search found comes back from ClassOf as the same instance.
    [Test]
    public void Find_ThenClassOf_ReturnsTheSameClass()
    {
        var reader = CreateReader(MemoryWithArray());

        var found = reader.Find("Pawn").Unwrap();

        Assert.That(reader.ClassOf(_pawnInstance).Unwrap(), Is.SameAs(found));
    }

    // The search reads the name of a class only, so an instance in the array costs 1 class pointer read.
    [Test]
    public void Find_InstanceInTheArray_DoesNotReadItsName()
    {
        var memory = MemoryWithArray();

        _ = CreateReader(memory).Find("Pawn").Unwrap();

        Assert.That(memory.ReadsAt(_pawnInstance + _objectLayout.Name), Is.Zero);
    }

    // A class name in UTF-16 compares equal to the same text.
    [Test]
    public void Find_WideClassName_ReturnsIt()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _wideClass, WideId, _objectClass);
        memory.AddNameArray(_array, _arrayData, 2, _metaclass, _wideClass);

        var klass = CreateReader(memory).Find(WideName).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(klass.Address, Is.EqualTo(_wideClass));
            Assert.That(klass.Name, Is.EqualTo(WideName));
        }
    }

    // A super chain of exactly 64 classes ends without a failure and still returns the property of the last class.
    [Test]
    public void PropertiesOf_SuperChainOf64Classes_ReturnsThePropertyOfTheLast()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, 64);

        var items = CreateReader(memory).PropertiesOf(_chain).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Unwrap().Name, Is.EqualTo("Hp"));
        }
    }

    // A super chain of 65 classes fails at the limit before it reads the property of the last class.
    [Test]
    public void PropertiesOf_SuperChainOf65Classes_FailsAtTheLimit()
    {
        var memory = MemoryWithClasses();
        AddChain(memory, 65);

        var items = CreateReader(memory).PropertiesOf(_chain).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"The super chain of the struct at {_chain} goes on past 64 levels. "
                    + $"Check StructLayout.SuperStruct (0x{_structLayout.SuperStruct:X})."));
        }
    }

    // Pawn declares its own Hp over Actor's Hp, so Pawn returns its own offset and Actor keeps its offset.
    [Test]
    public void FindProperty_NameOnDerivedAndBase_ReturnsTheDerived()
    {
        var memory = MemoryWithArray();
        AddProperty(memory, _pawn, _pawnHp, HpId, 0x2C0, Address.Zero);
        memory.WritePointer(_pawn + FirstPropertyOffset(), _pawnHp);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Find("Pawn").Unwrap().FindProperty("Hp").Unwrap().Offset, Is.EqualTo(0x2C0));
            Assert.That(reader.Find("Actor").Unwrap().FindProperty("Hp").Unwrap().Offset, Is.EqualTo(0x2A8));
        }
    }

    // A class without properties or a super class fails the lookup with the class and the name.
    [Test]
    public void FindProperty_ClassWithoutProperties_FailsWithTheClassAndTheName()
    {
        Assert.That(
            CreateReader(MemoryWithArray()).Find("Object").Unwrap().FindProperty("Hp").UnwrapErr().Message,
            Is.EqualTo("Class Object doesn't have a property Hp."));
    }

    // Writes 1 entry per text with the id as the position, the last one in UTF-16.
    private FakeMemory MemoryWithNames()
    {
        string[] names =
        [
            "None",
            "Class",
            "Object",
            "Actor",
            "Pawn",
            "Property",
            "IntProperty",
            "ObjectProperty",
            "Hp",
            "BlueprintGeneratedClass",
            WideName,
            "BP_Pawn_C",
        ];
        var entries = new Address[names.Length];

        FakeMemory memory = new() { Layout = memoryLayout };

        for (int id = 0; id < names.Length; id++)
        {
            entries[id] = _nameEntries + (id * EntrySpacing);
            memory.AddNameEntry(entries[id], _entryLayout, (id << 1) | (id == WideId ? 1 : 0), names[id]);
        }

        memory.AddNameArray(_nameArray, _nameData, names.Length, entries);

        return memory;
    }

    // Lays out Class, Object, Actor with Hp, Pawn, the property classes of the shape and an instance of Pawn.
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
        }
        else
        {
            AddClass(memory, _propertyClass, PropertyId, Address.Zero);
            AddClass(memory, _intPropertyClass, IntPropertyId, _propertyClass);
        }

        AddProperty(memory, _actor, _hp, HpId, 0x2A8, Address.Zero);
        memory.WritePointer(_actor + FirstPropertyOffset(), _hp);

        return memory;
    }

    // Adds an array of Class, Object, Actor, the instance of Pawn and Pawn.
    private FakeMemory MemoryWithArray()
    {
        var memory = MemoryWithClasses();
        memory.AddNameArray(_array, _arrayData, 5, _metaclass, _objectClass, _actor, _pawnInstance, _pawn);

        return memory;
    }

    // Adds a Blueprint class called BP_Pawn_C whose class derives from Class over BlueprintGeneratedClass and adds an array with it.
    private FakeMemory MemoryWithBlueprintClass()
    {
        var memory = MemoryWithClasses();
        AddClass(memory, _blueprintMetaclass, BlueprintClassId, _metaclass);
        AddClass(memory, _widgetMetaclass, BlueprintClassId, _blueprintMetaclass);
        memory.AddObject(_blueprintClass, _objectLayout, BlueprintPawnId, klass: _widgetMetaclass);
        memory.AddNameArray(_array, _arrayData, 4, _metaclass, _blueprintMetaclass, _widgetMetaclass, _blueprintClass);

        return memory;
    }

    // Adds a super chain of classes from _chain on, with Hp on the last class.
    private void AddChain(FakeMemory memory, int classes)
    {
        for (int i = 0; i < classes; i++)
        {
            var type = _chain + (i * EntrySpacing);
            AddClass(memory, type, ObjectId, i + 1 < classes ? type + EntrySpacing : Address.Zero);
        }

        var last = _chain + ((classes - 1) * EntrySpacing);
        AddProperty(memory, last, _pawnHp, HpId, 0x2A8, Address.Zero);
        memory.WritePointer(last + FirstPropertyOffset(), _pawnHp);
    }

    private void AddClass(FakeMemory memory, Address type, int nameId, Address super)
    {
        memory.AddObject(type, _objectLayout, nameId, klass: _metaclass);
        memory.WritePointer(type + _structLayout.SuperStruct, super);
    }

    // Adds an object whose class chain loops between 2 classes.
    private void AddLoop(FakeMemory memory)
    {
        memory.AddObject(_loopA, _objectLayout, ActorId, klass: _loopB);
        memory.AddObject(_loopB, _objectLayout, ActorId, klass: _loopC);
        memory.AddObject(_loopC, _objectLayout, ActorId, klass: _loopB);
    }

    // Adds an int property in the shape of the fixture with its next property.
    private void AddProperty(FakeMemory memory, Address owner, Address property, int nameId, int offset, Address next)
    {
        if (shape == FieldShape)
        {
            memory.AddField(property, _fieldLayout, nameId, _intPropertyClass, next);
        }
        else
        {
            memory.AddObject(property, _objectLayout, nameId, outer: owner, klass: _intPropertyClass);
            memory.WritePointer(property + _childLayout.Next, next);
        }

        memory.WriteInt32(property + _propertyLayout.ElementSize, sizeof(int));
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

        var kindLayouts = KindLayouts.Read(new KindProfile("U", "ComparisonIndex", bitMaskBools: false, static (_, _) => null));

        return new ClassReader(memory, names, objectReader, structReader, propertyReader, new PointerObjectArray(memory, _array), kindLayouts);
    }
}
