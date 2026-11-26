using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealObjectPathTests(
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

    // Getting GWorld doesn't read and reading it returns the pointer in the GWorld slot.
    [Test]
    public void Read_GWorld_ReadsTheWorld()
    {
        int reads = _fakeGame.Memory.Reads;
        var gWorld = _game.GWorld;
        int readsAfterGet = _fakeGame.Memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readsAfterGet, Is.EqualTo(reads));
            Assert.That(gWorld.Read().Unwrap(), Is.EqualTo(_fakeGame.World));
        }
    }

    // A pointer of 0 at the end of an object path is a value, like a ReadObject of 0.
    [Test]
    public void Read_GWorldNull_ReadsZero()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, Address.Zero);

        Assert.That(_game.GWorld.Read().Unwrap(), Is.EqualTo(Address.Zero));
    }

    // GameMode is read from the world and Data from the game mode.
    [Test]
    public void Read_TwoObjectHops_ReadsTheDataObject()
    {
        Assert.That(_game.GWorld.ObjectMember("GameMode").ObjectMember("Data").Read().Unwrap(), Is.EqualTo(_fakeGame.Data));
    }

    // Data is a property of FixtureGameMode and not of GameModeBase, so the hop reads the class of the object and not a declared class.
    [Test]
    public void Read_MemberOfTheRealClassOnly_ResolvesByTheClassOfTheObject()
    {
        var data = _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");

        var fromFixtureGameMode = data.Read();
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.BaseGameMode);
        var fromGameModeBase = data.Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromFixtureGameMode.Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(
                fromGameModeBase.UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data. Class GameModeBase doesn't have a property Data."));
        }
    }

    // OtherGameMode has Data at another offset. Going back to the 1st class reuses its property, so the read costs 3 reads per hop.
    [Test]
    public void Read_ClassOfTheObjectChanges_UsesTheOffsetOfTheNewClass()
    {
        var data = _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");

        var first = data.Read();
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.OtherGameMode);
        var other = data.Read();
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        int reads = _fakeGame.Memory.Reads;
        var back = data.Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(other.Unwrap(), Is.EqualTo(_fakeGame.OtherData));
            Assert.That(back.Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (2 * 3)));
        }
    }

    // A game mode of 0 fails the hop after it with the start of the path that's null.
    [Test]
    public void Read_NullInTheMiddle_FailsWithThePathAndTheNullObject()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        Assert.That(
            _game.GWorld.ObjectMember("GameMode").ObjectMember("Data").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data: GWorld.GameMode is null."));
    }

    // A world of 0 fails the 1st hop with the global.
    [Test]
    public void Read_NullAtTheStart_FailsWithGWorldIsNull()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, Address.Zero);

        Assert.That(_game.GWorld.ObjectMember("GameMode").Read().UnwrapErr().Message, Is.EqualTo("Can't read GWorld.GameMode: GWorld is null."));
    }

    // A path returns the same error for a null object on each read, because a loading game fails that way on every tick.
    [Test]
    public void Read_NullObjectTwice_ReturnsTheSameError()
    {
        var data = _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        var first = data.Read().UnwrapErr();
        var second = data.Read().UnwrapErr();

        Assert.That(second, Is.SameAs(first));
    }

    // A member that the class doesn't have fails with the same error on each read and only costs the reads of the classes on the way.
    [Test]
    public void Read_MemberNotOnTheClassTwice_ReturnsTheSameError()
    {
        var data = _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.BaseGameMode);

        var first = data.Read().UnwrapErr();
        int reads = _fakeGame.Memory.Reads;
        var second = data.Read().UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + 3 + 2));
        }
    }

    // A path that failed on a null object reads once the pointer is set.
    [Test]
    public void Read_NullThenObject_ReadsOnceTheObjectIsThere()
    {
        var data = _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        var failure = data.Read();
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.IsErr, Is.True);
            Assert.That(data.Read().Unwrap(), Is.EqualTo(_fakeGame.Data));
        }
    }

    // A game mode outside every block fails reading its class, with the hop that reads from it.
    [Test]
    public void Read_UnreadableObjectInTheMiddle_FailsWithThePathTheHopAndTheCause()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.UnreadableAddress);

        Assert.That(
            _game.GWorld.ObjectMember("GameMode").ObjectMember("Data").Read().UnwrapErr().Message,
            Does.StartWith($"Can't read GWorld.GameMode.Data. Can't read the class of the object at {_fakeGame.UnreadableAddress}: "));
    }

    // A member that the class of the object doesn't have fails with the class and the member.
    [Test]
    public void Read_MemberNotFound_FailsWithTheClassAndTheMember()
    {
        Assert.That(
            _game.GWorld.ObjectMember("Mana").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.Mana. Class World doesn't have a property Mana."));
    }

    // A hop doesn't keep a failed lookup, so the path reads once the properties of the class read.
    [Test]
    public void Read_PropertiesReadableAfterAFailure_ReadsTheMember()
    {
        var data = _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");
        _fakeGame.Memory.WritePointer(_fakeGame.FixtureGameModeClass + FakeGame.SuperStructOffset, _fakeGame.UnreadableAddress);

        var failure = data.Read();
        _fakeGame.Memory.WritePointer(_fakeGame.FixtureGameModeClass + FakeGame.SuperStructOffset, _fakeGame.GameModeBaseClass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Does.StartWith("Can't read GWorld.GameMode.Data. "));
            Assert.That(data.Read().Unwrap(), Is.EqualTo(_fakeGame.Data));
        }
    }

    // Hp is an IntProperty, so an object hop through it fails with its kind.
    [Test]
    public void Read_IntMemberAsObjectHop_FailsWithWrongKind()
    {
        Assert.That(
            _game.GWorld.ObjectMember("GameMode").ObjectMember("Data").ObjectMember("Hp").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp. Hp is IntProperty, not ObjectProperty."));
    }

    // ObjectMember throws on a null name.
    [Test]
    public void ObjectMember_NullName_Throws()
    {
        Assert.That(() => _game.GWorld.ObjectMember(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("name"));
    }

    // Int32Member throws on a null name.
    [Test]
    public void Int32Member_NullName_Throws()
    {
        Assert.That(() => _game.GWorld.Int32Member(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("name"));
    }

    // A path prints its global and members joined by periods and a longer path doesn't change the path it starts from.
    [Test]
    public void ToString_TwoHops_PrintsTheGlobalAndTheMembers()
    {
        var gameMode = _game.GWorld.ObjectMember("GameMode");
        var data = gameMode.ObjectMember("Data");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.ToString(), Is.EqualTo("GWorld.GameMode.Data"));
            Assert.That(gameMode.ToString(), Is.EqualTo("GWorld.GameMode"));
            Assert.That(_game.GWorld.ToString(), Is.EqualTo("GWorld"));
        }
    }
}
