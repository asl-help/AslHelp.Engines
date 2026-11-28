using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameLookupTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), "uproperty"),
        new(MemoryLayout.X86, new Address(0x8000_0000), "ffield"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "uproperty"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "ffield"),
    ];

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // GetPackages lists each package once in array order, as the same instance that FindPackage returns.
    [Test]
    public void GetPackages_LoadedPackages_ListsEachOnce()
    {
        var packages = _game.GetPackages().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(packages.Select(static package => package.Name), Is.EqualTo(["/Script/CoreUObject", "/Script/Engine", "/Script/Fixture"]));
            Assert.That(packages[2], Is.SameAs(_game.FindPackage("/Script/Fixture").Unwrap()));
        }
    }

    // The engine compares package names without case, so a path in another case finds the package.
    [Test]
    public void FindPackage_PathInOtherCase_ReturnsThePackage()
    {
        Assert.That(_game.FindPackage("/script/fixture").Unwrap().Name, Is.EqualTo("/Script/Fixture"));
    }

    // /Script/CoreUObject is in front of the class Package in the array, so the search reads it again once it found the class.
    [Test]
    public void FindPackage_PackageBeforeThePackageClassInTheArray_FindsIt()
    {
        Assert.That(_game.FindPackage("/Script/CoreUObject").Unwrap().Name, Is.EqualTo("/Script/CoreUObject"));
    }

    // A path that isn't the path of a loaded package fails with the path.
    [Test]
    public void FindPackage_PackageThatIsNotLoaded_FailsWithItsPath()
    {
        Assert.That(_game.FindPackage("/Script/X").UnwrapErr().Message, Is.EqualTo("Package /Script/X isn't loaded."));
    }

    // An array whose items don't read fails with the read, so a broken array doesn't look like a package that isn't loaded.
    [Test]
    public void FindPackage_ArrayThatDoesNotRead_FailsWithTheRead()
    {
        _fakeGame.WriteObjectCount(1000);

        Assert.That(
            _game.FindPackage("/Script/X").UnwrapErr().Message,
            Does.StartWith("Can't search the object array for the package /Script/X. Can't read "));
    }

    // A package created after a miss shows up on the next call, because a miss reads the array again.
    [Test]
    public void FindPackage_PackageAddedAfterAMiss_ReturnsIt()
    {
        var miss = _game.FindPackage("/Script/Late");
        var package = _fakeGame.CreateObject("/Script/Late", _fakeGame.PackageClass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(miss.IsErr, Is.True);
            Assert.That(_game.FindPackage("/Script/Late").Unwrap().Address, Is.EqualTo(package));
        }
    }

    // A search keeps the object of each slot, so a 2nd miss on the same array doesn't read an object again.
    [Test]
    public void FindPackage_SecondMissOnTheSameArray_DoesNotReadAnObjectAgain()
    {
        _ = _game.FindPackage("/Script/X");
        int reads = _fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Class);
        _ = _game.FindPackage("/Script/X");

        Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Class), Is.EqualTo(reads));
    }

    // A search doesn't trust a kept package whose address now has another name, so FindPackage returns the package at its new address.
    [Test]
    public void FindPackage_PackageUnloadedAndLoadedElsewhere_ReturnsTheNewAddress()
    {
        var old = _game.FindPackage("/Script/Fixture").Unwrap().Address;
        _fakeGame.WriteName(old, "/Script/Late");
        var moved = _fakeGame.CreateObject("/Script/Fixture", _fakeGame.PackageClass);

        Assert.That(_game.FindPackage("/Script/Fixture").Unwrap().Address, Is.EqualTo(moved));
    }

    // A package in the slot and at the address of a freed package doesn't change the array, so a miss reads the package names again.
    [Test]
    public void FindPackage_PackageAtTheAddressOfAFreedPackage_ReturnsTheNewPackage()
    {
        var package = _game.FindPackage("/Script/Fixture").Unwrap().Address;
        _fakeGame.WriteName(package, "/Script/Late");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.FindPackage("/Script/Late").Unwrap().Address, Is.EqualTo(package));
            Assert.That(_game.GetPackages().Unwrap().Select(static package => package.Name), Has.None.EqualTo("/Script/Fixture"));
        }
    }

    // The class of a package is the class of the game, so all 3 lookups give 1 instance.
    [Test]
    public void FindClass_ClassOfThePackage_ReturnsTheClassOfTheGame()
    {
        var klass = _game.FindPackage("/Script/Fixture").Unwrap().FindClass("FixtureData").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(klass, Is.SameAs(_game.FindClass("FixtureData").Unwrap()));
            Assert.That(klass, Is.SameAs(_game.ClassOf(_fakeGame.Data).Unwrap()));
        }
    }

    // The outer object decides the package, so a class of another package fails with the package and the class.
    [Test]
    public void FindClass_ClassOfAnotherPackage_FailsWithThePackageAndTheClass()
    {
        Assert.That(
            _game.FindPackage("/Script/Engine").Unwrap().FindClass("FixtureData").UnwrapErr().Message,
            Is.EqualTo("Package /Script/Engine doesn't have a class FixtureData."));
    }

    // A package lists only the classes whose outer object it is, without struct types and plain objects.
    [Test]
    public void GetClasses_Package_ListsOnlyClassesWhoseOuterIsThePackage()
    {
        var classes = _game.FindPackage("/Script/Fixture").Unwrap().GetClasses().Unwrap();

        Assert.That(
            classes.Select(static klass => klass.Name),
            Is.EqualTo(["FixtureGameMode", "OtherGameMode", "FixtureData", "FloatFixtureData", "FixtureSubsystem"]));
    }

    // A 2nd listing on the same array reads the outer object of a class only once.
    [Test]
    public void GetClasses_SecondCall_DoesNotReadAnOuterAgain()
    {
        var package = _game.FindPackage("/Script/Fixture").Unwrap();

        _ = package.GetClasses().Unwrap();
        _ = package.GetClasses().Unwrap();

        Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.FixtureDataClass + FakeGame.ObjectLayout.Outer), Is.EqualTo(1));
    }

    // A derived class lists only what it declares, each property with its kind, offset and element size.
    [Test]
    public void GetProperties_DerivedClass_ListsOnlyItsOwnProperties()
    {
        var properties = _game.FindClass("FixtureGameMode").Unwrap().GetProperties().Unwrap();

        Assert.That(
            properties.Select(static property => (property.Name, property.ClassName, property.Offset, property.ElementSize)),
            Is.EqualTo([("Data", "ObjectProperty", FakeGame.DataOffset, memoryLayout.PointerSize)]));
    }

    // The own list and the lookup through the bases don't share a list, so a member of a base still comes back.
    [Test]
    public void FindProperty_PropertyOfABase_StillFindsItAfterGetProperties()
    {
        var klass = _game.FindClass("GameEngine").Unwrap();

        var own = klass.GetProperties().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(own, Is.Empty);
            Assert.That(klass.FindProperty("Data").Unwrap().Offset, Is.EqualTo(FakeGame.DataOffset));
        }
    }

    // FixtureData has 2 live objects and a default object, so the list has the 2 objects in array order.
    [Test]
    public void GetInstances_ClassWithTwoObjects_ReturnsBothInArrayOrder()
    {
        var instances = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap();

        Assert.That(instances.Select(static instance => instance.Address), Is.EqualTo([_fakeGame.Data, _fakeGame.OtherData]));
    }

    // The default object of a class isn't an object of the game.
    [Test]
    public void GetInstances_DefaultObject_SkipsIt()
    {
        var instances = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap();

        Assert.That(instances.Select(static instance => instance.Name), Has.None.StartWith("Default__"));
    }

    // The game mode is an object of FixtureGameMode, so GameModeBase lists only its own object.
    [Test]
    public void GetInstances_ObjectOfADerivedClass_SkipsIt()
    {
        var instances = _game.FindClass("GameModeBase").Unwrap().GetInstances().Unwrap();

        Assert.That(instances.Select(static instance => instance.Address), Is.EqualTo([_fakeGame.BaseGameMode]));
    }

    // A class without objects gives an empty list.
    [Test]
    public void GetInstances_ClassWithoutObjects_ReturnsAnEmptyList()
    {
        Assert.That(_game.FindClass("FixtureSubsystem").Unwrap().GetInstances().Unwrap(), Is.Empty);
    }

    // The search keeps the class of each slot, so a call reads the class pointer and the name only of a match.
    [Test]
    public void GetInstances_SlotsThatDidNotChange_DoesNotReadTheirClassPointers()
    {
        var klass = _game.FindClass("FixtureData").Unwrap();
        int classReads = _fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Class);
        int nameReads = _fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Name);

        _ = klass.GetInstances().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Class), Is.EqualTo(classReads));
            Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Name), Is.EqualTo(nameReads));
        }
    }

    // The objects aren't kept, so an object created between 2 calls shows up in the 2nd.
    [Test]
    public void GetInstances_ObjectAddedBetweenCalls_ShowsUpInTheNextCall()
    {
        var klass = _game.FindClass("FixtureSubsystem").Unwrap();

        var before = klass.GetInstances().Unwrap();
        _fakeGame.AddObject(_fakeGame.Subsystem);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.Empty);
            Assert.That(klass.GetInstances().Unwrap().Single().Address, Is.EqualTo(_fakeGame.Subsystem));
        }
    }

    // The class of the data object changed at its address, so the list leaves it out.
    [Test]
    public void GetInstances_ObjectWhoseAddressGotAnotherClass_LeavesItOut()
    {
        var klass = _game.FindClass("FixtureData").Unwrap();
        _ = klass.GetInstances().Unwrap();
        _fakeGame.WriteClass(_fakeGame.Data, _fakeGame.FixtureGameModeClass);

        Assert.That(klass.GetInstances().Unwrap().Select(static instance => instance.Address), Is.EqualTo([_fakeGame.OtherData]));
    }

    // GameModeBase has 2 derived classes and Engine has 1. Neither list has the class itself.
    [Test]
    public void GetDerivedClasses_BaseClass_ListsTheDerivedClassesWithoutItself()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _game.FindClass("GameModeBase").Unwrap().GetDerivedClasses().Unwrap().Select(static klass => klass.Name),
                Is.EqualTo(["FixtureGameMode", "OtherGameMode"]));
            Assert.That(
                _game.FindClass("Engine").Unwrap().GetDerivedClasses().Unwrap().Select(static klass => klass.Name),
                Is.EqualTo(["GameEngine"]));
        }
    }

    // A class that nothing derives from gives an empty list.
    [Test]
    public void GetDerivedClasses_LeafClass_ReturnsAnEmptyList()
    {
        Assert.That(_game.FindClass("FixtureData").Unwrap().GetDerivedClasses().Unwrap(), Is.Empty);
    }

    // A package, a class and an object print their name and a property prints its kind, name, offset and size.
    [Test]
    public void ToString_PackageClassPropertyAndObject_ReturnsTheNameOrThePropertyText()
    {
        var klass = _game.FindClass("FixtureData").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.FindPackage("/Script/Fixture").Unwrap().ToString(), Is.EqualTo("/Script/Fixture"));
            Assert.That(klass.ToString(), Is.EqualTo("FixtureData"));
            Assert.That(klass.FindProperty("Hp").Unwrap().ToString(), Is.EqualTo("IntProperty Hp @ 0x88 (4 bytes)"));
            Assert.That(_game.FindClass("GameEngine").Unwrap().GetInstances().Unwrap().Single().ToString(), Is.EqualTo("GameEngine_0"));
        }
    }

    // A path from an object reads its member and its text starts with the class.
    [Test]
    public void Path_Instance_ReadsAMemberOfTheObject()
    {
        var hp = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap()[0].Path.Member("Hp").Value<int>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Read().Unwrap(), Is.EqualTo(FakeGame.Hp));
            Assert.That(hp.Unwrap().ToString(), Is.EqualTo("FixtureData.Hp"));
        }
    }

    // A boxed watcher of a path from an object gives the int.
    [Test]
    public void CreateWatcher_InstanceMember_WatchesAMemberOfTheObject()
    {
        var hp = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap()[0].Path.Member("Hp").Unwrap();

        var watcher = hp.CreateWatcher(new ManualTicks());

        Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
    }

    // The address of the object has an object of another class now, so the path fails instead of reading the other object.
    [Test]
    public void Path_InstanceWhoseAddressGotAnotherClass_FailsAsFreed()
    {
        var hp = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap()[0].Path.Member("Hp").Value<int>();
        _fakeGame.WriteClass(_fakeGame.Data, _fakeGame.FixtureGameModeClass);

        Assert.That(
            hp.Read().UnwrapErr().Message,
            Is.EqualTo($"Can't read FixtureData.Hp at FixtureData. The FixtureData object at {_fakeGame.Data} was freed."));
    }

    // The game cleared the slot of the object but its memory still has the class and the values, so the path fails instead of
    // reading the last values.
    [Test]
    public void Path_InstanceWhoseSlotGotCleared_FailsAsFreed()
    {
        var hp = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap()[0].Path.Member("Hp").Value<int>();
        _fakeGame.FreeObject(_fakeGame.Data);

        Assert.That(
            hp.Read().UnwrapErr().Message,
            Is.EqualTo($"Can't read FixtureData.Hp at FixtureData. The FixtureData object at {_fakeGame.Data} was freed."));
    }

    // GameMode is declared as an object property and only FixtureGameMode, the class of the object, declares Data.
    [Test]
    public void Path_InstanceOfABaseMember_ReadsAMemberOnlyTheDerivedClassDeclares()
    {
        var world = _game.FindClass("World").Unwrap().GetInstances().Unwrap().Single();

        Assert.That(world.Path.Member("GameMode").Member("Data").Member("Hp").Value<int>().Read().Unwrap(), Is.EqualTo(FakeGame.Hp));
    }
}
