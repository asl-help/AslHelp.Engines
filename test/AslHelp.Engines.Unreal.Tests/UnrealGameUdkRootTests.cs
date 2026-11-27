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

    // GEngine points at an object of GameEngine and its base Engine declares Game.
    [Test]
    public void GEngine_Path_ReadsAMemberThroughTheEngineObject()
    {
        var intValue = _game.GEngine.Member("Game").Member("FixtureData").Member<int>("IntValue");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(intValue.Read().Unwrap(), Is.EqualTo(FakeUdkGame.IntValue));
            Assert.That(intValue.ToString(), Is.EqualTo("GEngine.Game.FixtureData.IntValue"));
        }
    }

    // A slot of 0 fails like a null object and the same path reads once the game sets the slot.
    [Test]
    public void GEngine_NullSlot_FailsThenReadsOnceTheSlotIsSet()
    {
        var game = _game.GEngine.Member("Game");
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, Address.Zero);

        var failure = game.Member<int>("GoalScore").Read();
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, _fakeGame.Engine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Is.EqualTo("Can't read GEngine.Game.GoalScore: GEngine is null."));
            Assert.That(game.Member<int>("GoalScore").Read().Unwrap(), Is.EqualTo(FakeUdkGame.GoalScore));
        }
    }

    // Unreal Engine 3 doesn't have subsystems, so the lookup fails without a read.
    [Test]
    public void FindSubsystem_Unreal3Profile_FailsWithTheVersion()
    {
        Assert.That(_game.FindSubsystem("FixtureSubsystem").UnwrapErr().Message, Is.EqualTo("Subsystems need 4.22 or later and UGameInstance.SubsystemCollection in the profile."));
    }
}
