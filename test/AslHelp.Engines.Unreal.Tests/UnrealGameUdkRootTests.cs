using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkRootTests
{
    private FakeUdkGame _fakeGame = null!;
    private UnrealGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeUdkGame();
        _game = _fakeGame.Attach();
    }

    // GWorld and GEngine are roots, whose paths start with the name of the global and read the address of its object.
    [Test]
    public void Path_GWorldAndGEngine_StartsAtTheGlobal()
    {
        var world = _game.GWorld.Path.Read<Address>();
        var engine = _game.GEngine.Path.CreateWatcher(new ManualTicks()).Current;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.GWorld.Path.ToString(), Is.EqualTo("GWorld"));
            Assert.That(_game.GEngine.ToString(), Is.EqualTo("GEngine"));
            Assert.That(world.Unwrap(), Is.EqualTo(_fakeGame.World));
            Assert.That(engine, Is.EqualTo(_fakeGame.Engine));
        }
    }

    // A root reads as the address of its object, so a typed int read fails on the step.
    [Test]
    public void Value_PathOfARootAsInt_FailsOnTheStep()
    {
        Assert.That(_game.GEngine.Path.Value<int>().UnwrapErr().Message, Is.EqualTo("Can't read GEngine as Int32. An object reads as Address."));
    }
    // GEngine points at an object of GameEngine and its base Engine declares Game.
    [Test]
    public void GEngine_Path_ReadsAMemberThroughTheEngineObject()
    {
        var intValue = _game.GEngine.Path.Member("Game").Member("FixtureData").Member("IntValue");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(intValue.Read<int>().Unwrap(), Is.EqualTo(FakeUdkGame.IntValue));
            Assert.That(intValue.Unwrap().ToString(), Is.EqualTo("GEngine.Game.FixtureData.IntValue"));
        }
    }

    // A slot of 0 fails like a null object and the same path reads once the game sets the slot.
    [Test]
    public void GEngine_NullSlot_FailsThenReadsOnceTheSlotIsSet()
    {
        var game = _game.GEngine.Path.Member("Game");
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, Address.Zero);

        var failure = game.Member("GoalScore").Read<int>();
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, _fakeGame.Engine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Is.EqualTo("Can't read GEngine.Game.GoalScore: GEngine is null."));
            Assert.That(game.Member("GoalScore").Read<int>().Unwrap(), Is.EqualTo(FakeUdkGame.GoalScore));
        }
    }

    // Unreal Engine 3 doesn't have subsystems, so the lookup fails without a read.
    [Test]
    public void FindSubsystem_Unreal3Profile_FailsWithTheVersion()
    {
        Assert.That(
            _game.FindSubsystem("FixtureSubsystem").UnwrapErr().Message,
            Is.EqualTo("Subsystems need 4.22 or later and UGameInstance.SubsystemCollection in the profile."));
    }
}
