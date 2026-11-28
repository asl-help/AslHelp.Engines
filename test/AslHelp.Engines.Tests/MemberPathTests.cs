using AslHelp.Memory.Watching;
using NUnit.Framework;
using System.Collections.Generic;

namespace AslHelp.Engines.Tests;

public sealed class MemberPathTests
{
    private FakeObject _player = null!;
    private FakeStruct _position = null!;
    private FakeObject _stats = null!;

    [SetUp]
    public void SetUp()
    {
        _stats = new FakeObject("stats", "Stats");
        _stats.Members["Health"] = 100;
        _position = new FakeStruct("Vector", ["X", "Y", "Z"], [1f, 2f, 3f]);

        _player = new FakeObject("player", "Player");
        _player.Members["Stats"] = _stats;
        _player.Members["Name"] = "hero";
        _player.Members["Items"] = new List<object?> { 10, 20, 30 };
        _player.Members["Scores"] = new Dictionary<string, object?> { ["one"] = 1, ["two"] = 2 };
        _player.Members["Position"] = _position;
        _player.Members["Target"] = null;
    }

    // A step returns a longer path and the shorter path stays as it was, so 2 paths can share a start.
    [Test]
    public void Member_ANameTheEngineHas_ReturnsTheLongerPath()
    {
        var stats = _player.Path.Member("Stats");

        var health = stats.Member("Health");
        var other = stats.Member("Health");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(health.Unwrap().ToString(), Is.EqualTo("player.Stats.Health"));
            Assert.That(other.Unwrap(), Is.Not.SameAs(health.Unwrap()));
            Assert.That(stats.Unwrap().ToString(), Is.EqualTo("player.Stats"));
        }
    }

    // A typo fails on the step with the message of the engine, before any watcher reads.
    [Test]
    public void Member_AMisspelledName_FailsOnTheStep()
    {
        Assert.That(_player.Path.Member("Stats").Member("Helth").UnwrapErr().Message, Is.EqualTo("Stats doesn't have a member Helth."));
    }

    // An index and a key go through PathKey and the text shows each key as written.
    [Test]
    public void Item_AnIndexAndAKey_ReturnsTheItemPath()
    {
        var third = _player.Path.Member("Items").Item(2).Unwrap();
        var two = _player.Path.Member("Scores").Item("two").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(third.ToString(), Is.EqualTo("player.Items[2]"));
            Assert.That(two.ToString(), Is.EqualTo("player.Scores[\"two\"]"));
            Assert.That(third.CreateWatcher(new ManualTicks()).Current, Is.EqualTo(30));
            Assert.That(two.CreateWatcher(new ManualTicks()).Current, Is.EqualTo(2));
        }
    }

    // Name is a string, so an index after it fails on the step.
    [Test]
    public void Item_OnAMemberThatIsNotACollection_FailsOnTheStep()
    {
        Assert.That(_player.Path.Member("Name").Item(0).UnwrapErr().Message, Is.EqualTo("player.Name doesn't take [0]."));
    }

    // Count after a collection watches its number of items.
    [Test]
    public void Count_OnACollection_WatchesTheNumberOfItems()
    {
        var count = _player.Path.Member("Items").Count().Unwrap();

        Assert.That(count.CreateWatcher(new ManualTicks()).Current, Is.EqualTo(3));
    }

    // Name is a string, so Count after it fails on the step.
    [Test]
    public void Count_OnAString_FailsOnTheStep()
    {
        Assert.That(_player.Path.Member("Name").Count().UnwrapErr().Message, Is.EqualTo("player.Name doesn't take .Count."));
    }

    // A value written between 2 ticks shows up as a change and each call gives a new watcher.
    [Test]
    public void CreateWatcher_ValueChangesBetweenTicks_ReportsOldAndCurrent()
    {
        ManualTicks ticks = new();
        var health = _player.Path.Member("Stats").Member("Health").Unwrap();
        var watcher = health.CreateWatcher(ticks);

        _ = watcher.Current;
        _stats.Members["Health"] = 90;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.EqualTo(100));
            Assert.That(watcher.Current, Is.EqualTo(90));
            Assert.That(watcher.Changed, Is.True);
            Assert.That(health.CreateWatcher(ticks), Is.Not.SameAs(watcher));
        }
    }

    // Value<int> on an int member gives a typed watcher.
    [Test]
    public void Value_TheTypeOfTheMember_GivesATypedWatcher()
    {
        var watcher = _player.Path.Member("Stats").Member("Health").Value<int>().Unwrap().CreateWatcher(new ManualTicks());

        Assert.That(watcher.Current, Is.EqualTo(100));
    }

    // Health is an int, so Value<string> on it fails on the step.
    [Test]
    public void Value_AnotherTypeThanTheMember_FailsOnTheStep()
    {
        Assert.That(
            _player.Path.Member("Stats").Member("Health").Value<string>().UnwrapErr().Message,
            Is.EqualTo("player.Stats.Health isn't a String."));
    }

    // A null object on the way doesn't fail the step. Each read fails until the object is there.
    [Test]
    public void CreateWatcher_ANullObjectOnTheWay_FailsEachReadUntilItIsSet()
    {
        ManualTicks ticks = new();
        var watcher = _player.Path.Member("Target").Member("Health").Unwrap().CreateWatcher(ticks);

        (object? current, string? error) = (watcher.Current, watcher.Error?.Message);
        _player.Members["Target"] = _stats;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(current, Is.Null);
            Assert.That(error, Is.EqualTo("Can't read player.Target.Health: player.Target is null."));
            Assert.That(watcher.Current, Is.EqualTo(100));
            Assert.That(watcher.Error, Is.Null);
        }
    }

    // A member of a struct is a step like a member of an object.
    [Test]
    public void Member_AStructMember_ReadsByName()
    {
        var y = _player.Path.Member("Position").Member("Y").Value<float>().Unwrap();

        Assert.That(y.CreateWatcher(new ManualTicks()).Current, Is.EqualTo(2f));
    }

    // A struct reads as a StructValue with its members by name and changes when 1 member changes.
    [Test]
    public void CreateWatcher_AStruct_GivesAStructValue()
    {
        ManualTicks ticks = new();
        var watcher = _player.Path.Member("Position").Value<StructValue>().Unwrap().CreateWatcher(ticks);

        _ = watcher.Current;
        _position.Values[1] = 5f;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.True);
            Assert.That(watcher.Old!["Y"], Is.EqualTo(2f));
            Assert.That(watcher.Current!["Y"], Is.EqualTo(5f));
            Assert.That(watcher.Current.Names, Is.EqualTo(["X", "Y", "Z"]));
        }
    }

    // The steps on a failed step pass its failure on, so the 1st typo shows up at the end.
    [Test]
    public void Member_AfterAFailedStep_KeepsTheFirstError()
    {
        var typo = _player.Path.Member("Stats").Member("Helth");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(typo.Member("X").UnwrapErr(), Is.SameAs(typo.UnwrapErr()));
            Assert.That(typo.Item(1).UnwrapErr(), Is.SameAs(typo.UnwrapErr()));
            Assert.That(typo.Count().UnwrapErr(), Is.SameAs(typo.UnwrapErr()));
            Assert.That(typo.Value<int>().UnwrapErr(), Is.SameAs(typo.UnwrapErr()));
        }
    }
}
