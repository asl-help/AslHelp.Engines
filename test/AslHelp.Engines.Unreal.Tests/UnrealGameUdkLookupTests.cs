using NUnit.Framework;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkLookupTests
{
    private FakeUdkGame _fakeGame = null!;
    private UnrealGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeUdkGame();
        _game = _fakeGame.Attach();
    }

    // Unreal Engine 3 packages have bare names and the group inside Engine has an outer object, so the list has the 3 packages.
    [Test]
    public void GetPackages_LoadedPackages_ListsEachOnce()
    {
        Assert.That(_game.GetPackages().Unwrap().Select(static package => package.Name), Is.EqualTo(["Core", "Engine", "Fixture"]));
    }

    // A bare name finds the package in any case.
    [Test]
    public void FindPackage_BareName_ReturnsThePackage()
    {
        Assert.That(_game.FindPackage("engine").Unwrap().Name, Is.EqualTo("Engine"));
    }

    // The class of a package is the class of the game, so both lookups give 1 instance.
    [Test]
    public void FindClass_ClassOfThePackage_ReturnsTheClassOfTheGame()
    {
        Assert.That(
            _game.FindPackage("Fixture").Unwrap().FindClass("FixtureData").Unwrap(),
            Is.SameAs(_game.FindClass("FixtureData").Unwrap()));
    }

    // FixtureData has 2 live objects and a default object, so the list has the 2 objects in array order with their name numbers.
    [Test]
    public void GetInstances_ClassWithTwoObjects_ReturnsBothInArrayOrder()
    {
        var instances = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap();

        Assert.That(instances.Select(static instance => instance.Name), Is.EqualTo(["FixtureData_1", "FixtureData_0"]));
    }

    // A path from an object doesn't need the world, which has 0 reflected properties on a real UDK.
    [Test]
    public void GetInstances_FixtureData_ReachesIntValueWithoutTheWorld()
    {
        var data = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single(static instance => instance.Name == "FixtureData_0");

        Assert.That(data.Path.Member<int>("IntValue").Read().Unwrap(), Is.EqualTo(FakeUdkGame.IntValue));
    }

    // Engine has GameEngine below it and Object has the property classes 2 levels below it. Neither list has the class itself.
    [Test]
    public void GetDerivedClasses_BaseClass_ListsDirectAndIndirectClassesWithoutItself()
    {
        var below = _game.FindClass("Object").Unwrap().GetDerivedClasses().Unwrap().Select(static klass => klass.Name).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _game.FindClass("Engine").Unwrap().GetDerivedClasses().Unwrap().Select(static klass => klass.Name),
                Is.EqualTo(["GameEngine"]));
            Assert.That(below, Does.Contain("IntProperty").And.Contain("GameEngine").And.Not.Contain("Object"));
        }
    }
}
