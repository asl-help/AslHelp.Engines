using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Linq;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameLookupEdgeTests(
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

    // A count of 6 ends the array after /Script/Engine, so the list leaves out /Script/Fixture in slot 6.
    [Test]
    public void GetPackages_ArrayShrankPastAPackage_LeavesItOut()
    {
        _ = _game.GetPackages().Unwrap();
        _fakeGame.WriteObjectCount(6);

        var packages = _game.GetPackages().Unwrap();

        Assert.That(packages.Select(static package => (package.Name, package.Address != Address.Zero)), Is.EqualTo([
            ("/Script/CoreUObject", true),
            ("/Script/Engine", true),
        ]));
    }

    // A count of 9 ends the array after FixtureGameMode, so the list leaves out the classes after it.
    [Test]
    public void GetClasses_ArrayShrankPastClasses_LeavesThemOut()
    {
        var package = _game.FindPackage("/Script/Fixture").Unwrap();
        _ = package.GetClasses().Unwrap();
        _fakeGame.WriteObjectCount(9);

        Assert.That(package.GetClasses().Unwrap().Select(static klass => klass.Name), Is.EqualTo(["FixtureGameMode"]));
    }

    // A package with an outer object is a group inside another package, so neither the list nor the lookup has it.
    [Test]
    public void GetPackages_PackageWithAnOuter_LeavesItOut()
    {
        var fixture = _game.FindPackage("/Script/Fixture").Unwrap();
        var group = _fakeGame.CreateObject("/Script/Late", _fakeGame.PackageClass);
        _fakeGame.Memory.WritePointer(group + FakeGame.ObjectLayout.Outer, fixture.Address);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.GetPackages().Unwrap().Select(static package => package.Name), Has.None.EqualTo("/Script/Late"));
            Assert.That(_game.FindPackage("/Script/Late").UnwrapErr().Message, Is.EqualTo("Package /Script/Late isn't loaded."));
        }
    }

    // An empty name doesn't match any package and fails like any other miss.
    [Test]
    public void FindPackage_EmptyName_FailsAsNotLoaded()
    {
        Assert.That(_game.FindPackage("").UnwrapErr().Message, Is.EqualTo("Package  isn't loaded."));
    }

    // The 2nd lookup in another case takes the kept package, so it still gives the real name and the same instance.
    [Test]
    public void FindPackage_PathInOtherCaseTwice_KeepsTheRealName()
    {
        var first = _game.FindPackage("/script/fixture").Unwrap();
        var second = _game.FindPackage("/SCRIPT/FIXTURE").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Name, Is.EqualTo("/Script/Fixture"));
            Assert.That(second, Is.SameAs(first));
        }
    }

    // A class lookup in another case gives the instance of the exact lookup, so it doesn't replace the kept class.
    [Test]
    public void FindClass_NameInOtherCaseThenExact_ReturnsTheSameInstance()
    {
        var other = _game.FindClass("FIXTUREDATA").Unwrap();
        var exact = _game.FindClass("FixtureData").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exact, Is.SameAs(other));
            Assert.That(_game.FindClass("fixtureData").Unwrap(), Is.SameAs(exact));
        }
    }

    // The class of a subsystem ignores case like FindClass, so a lowercase name reads the live subsystem.
    [Test]
    public void FindSubsystem_NameInOtherCase_ReadsTheSubsystem()
    {
        _fakeGame.AddObject(_fakeGame.Subsystem);

        var value = SubsystemGame().FindSubsystem("fixturesubsystem").Unwrap().Path.Member("subsystemvalue");

        Assert.That(value.Read<int>().Unwrap(), Is.EqualTo(FakeGame.SubsystemValue));
    }

    // The 1st FixtureData class got another name, so the name gives the 2nd class with that name.
    [Test]
    public void FindClass_FirstOfTwoClassesWithOneNameRenamed_ReturnsTheSecond()
    {
        var metaclass = _game.FindClass("Class").Unwrap().Address;
        var second = _fakeGame.CreateObject("FixtureData", metaclass);
        var first = _game.FindClass("FixtureData").Unwrap().Address;

        _fakeGame.WriteName(_fakeGame.FixtureDataClass, "/Script/Late");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(_fakeGame.FixtureDataClass));
            Assert.That(_game.FindClass("FixtureData").Unwrap().Address, Is.EqualTo(second));
        }
    }

    // A class that loads after a listing derives from GameModeBase, so the next listing has it.
    [Test]
    public void GetDerivedClasses_ClassLoadedAfterACall_ShowsUpInTheNextCall()
    {
        var gameModeBase = _game.FindClass("GameModeBase").Unwrap();
        _ = gameModeBase.GetDerivedClasses().Unwrap();

        var late = _fakeGame.CreateObject("GameMode", _game.FindClass("Class").Unwrap().Address);
        _fakeGame.Memory.WritePointer(late + FakeGame.SuperStructOffset, _fakeGame.GameModeBaseClass);

        Assert.That(
            gameModeBase.GetDerivedClasses().Unwrap().Select(static klass => klass.Name),
            Is.EqualTo(["FixtureGameMode", "OtherGameMode", "GameMode"]));
    }

    // An array whose items don't read fails with the read, so a broken array doesn't look like a class without objects.
    [Test]
    public void GetInstances_ArrayThatDoesNotRead_FailsWithTheRead()
    {
        var klass = _game.FindClass("FixtureSubsystem").Unwrap();
        _fakeGame.WriteObjectCount(1000);

        Assert.That(klass.GetInstances().UnwrapErr().Message, Does.StartWith("Can't read "));
    }

    // 2 live subsystems of 1 class give the 1st in array order.
    [Test]
    public void FindSubsystem_TwoLiveObjects_ReadsTheFirstInArrayOrder()
    {
        _fakeGame.AddObject(_fakeGame.Subsystem);
        var other = _fakeGame.CreateObject("FixtureSubsystem", _fakeGame.SubsystemClass);
        _fakeGame.Memory.WriteInt32(other + FakeGame.SubsystemValueOffset, 17);

        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member("SubsystemValue");

        Assert.That(value.Read<int>().Unwrap(), Is.EqualTo(FakeGame.SubsystemValue));
    }

    // A class that isn't loaded fails the read with the class lookup and not with a missing object.
    [Test]
    public void FindSubsystem_ClassNotLoaded_FailsWithTheClassLookup()
    {
        var value = SubsystemGame().FindSubsystem("Missing").Unwrap().Path.Member("SubsystemValue");

        Assert.That(value.Read<int>().UnwrapErr().Message, Does.EndWith("Can't find a loaded class called Missing."));
    }

    // A null class name is a programmer error and throws on every profile.
    [Test]
    public void FindSubsystem_NullName_Throws()
    {
        Assert.That(() => SubsystemGame().FindSubsystem(null!), Throws.TypeOf<ArgumentNullException>());
    }

    // Attaches with a document of 4.22 or later, which has the subsystem collection.
    private UnrealGame SubsystemGame()
    {
        string document = FakeGame.Document(shape, "indirect", "flat");

        return _fakeGame.Attach(Set(document, """{ "SubsystemCollection": "0x100" }""", "offsets", "UGameInstance"));
    }
}
