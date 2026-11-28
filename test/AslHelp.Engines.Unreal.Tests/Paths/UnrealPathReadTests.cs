using AslHelp.Engines.Errors;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPathReadTests(
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

    private Result<IMemberPath> Data => _game.GWorld.Path.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // A direct read boxes the value in the type of its kind.
    [Test]
    public void Read_IntMember_ReturnsTheBoxedInt()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("Hp").Read().Unwrap(), Is.EqualTo(FakeGame.Hp));
            Assert.That(Data.Member("StringValue").Read().Unwrap(), Is.EqualTo(FakeGame.StringText));
        }
    }

    // The root alone doesn't have a property, so it reads as the address of its object.
    [Test]
    public void Read_Root_ReturnsItsAddress()
    {
        Assert.That(_game.GWorld.Path.Read().Unwrap(), Is.EqualTo(_fakeGame.World));
    }

    // A typed read gives the same value as the watcher of the same type.
    [Test]
    public void Read_Typed_EqualsTheWatcherValue()
    {
        var speed = Data.Member("Speed");

        Assert.That(speed.Read<float>().Unwrap(), Is.EqualTo(speed.Value<float>().Unwrap().CreateWatcher(new ManualTicks()).Current));
    }

    // A direct read in the tick of a watcher reads every segment again, so it sees a write that the watcher doesn't see until the next tick.
    [Test]
    public void Read_AfterAWatcherReadInTheTick_ReadsEverySegmentAgain()
    {
        var hp = Data.Member("Hp");
        int coldReads = _fakeGame.Memory.Reads;
        _ = hp.Read<int>().Unwrap();
        coldReads = _fakeGame.Memory.Reads - coldReads;

        var watcher = hp.Value<int>().Unwrap().CreateWatcher(new ManualTicks());
        _ = watcher.Current;
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 7);
        int reads = _fakeGame.Memory.Reads;
        int value = hp.Read<int>().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(7));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(coldReads));
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
        }
    }

    // A direct read between 2 watchers of 1 tick doesn't take the shared steps from the 2nd watcher.
    [Test]
    public void Read_Between2WatchersOfATick_DoesNotChangeTheSharedSteps()
    {
        ManualTicks ticks = new();
        var hp = Data.Member("Hp").Value<int>().Unwrap().CreateWatcher(ticks);
        var speed = Data.Member("Speed").Value<float>().Unwrap().CreateWatcher(ticks);
        _ = hp.Current;
        int reads = _fakeGame.Memory.Reads;
        _ = speed.Current;
        int withoutRead = _fakeGame.Memory.Reads - reads;

        ticks.Value++;
        _ = hp.Current;
        _ = Data.Member("Speed").Read<float>().Unwrap();
        reads = _fakeGame.Memory.Reads;
        _ = speed.Current;

        Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(withoutRead));
    }

    // A null object on the way is a read that can work later, not a shape mismatch.
    [Test]
    public void Read_NullObjectOnTheWay_FailsWithoutAShapeMismatch()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        var error = Data.Member("Hp").Read<int>().UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode.Data is null."));
            Assert.That(error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).False);
        }
    }

    // Hp is an IntProperty, so a string read fails as a shape mismatch with the text of the failed Value<string> step.
    [Test]
    public void Read_WrongType_FailsWithAShapeMismatch()
    {
        var hp = Data.Member("Hp");

        var error = hp.Read<string>().UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Message, Is.EqualTo(hp.Value<string>().UnwrapErr().Message));
            Assert.That(error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
        }
    }

    // A root doesn't have a step to check.
    [Test]
    public void IsChecked_Root_IsTrue()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, Address.Zero);

        Assert.That(_game.GWorld.Path.IsChecked, Is.True);
    }

    // A step on a live object checks the member on its real class.
    [Test]
    public void IsChecked_StepOnALiveObject_IsTrue()
    {
        Assert.That(Data.Member("Hp").Unwrap().IsChecked, Is.True);
    }

    // Data is null, so Hp gets checked on FixtureData, the class that Data declares.
    [Test]
    public void Member_NullObjectMemberOnTheDeclaredClass_IsChecked()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(Data.Member("Hp").Unwrap().IsChecked, Is.True);
    }

    // GameMode declares GameModeBase and only the derived FixtureGameMode has Data, so the step stays unchecked until a read.
    [Test]
    public void Member_NullObjectMemberOnlyOnADerivedClass_IsUnchecked()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        var data = Data.Unwrap();
        bool before = data.IsChecked;

        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        var read = data.Read<Address>().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.False);
            Assert.That(read, Is.EqualTo(_fakeGame.Data));
            Assert.That(data.IsChecked, Is.True);
        }
    }

    // Neither GameModeBase nor a loaded class derived from it has Dataa, so the typo fails on the step while GameMode is null.
    [Test]
    public void Member_NullObjectMemberOnNoLoadedClass_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        var error = _game.GWorld.Path.Member("GameMode").Member("Dataa").UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                error.Message,
                Is.EqualTo("Can't read GWorld.GameMode.Dataa. Class GameModeBase and its loaded derived classes don't have a property Dataa."));
            Assert.That(error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
        }
    }

    // The step keeps that no derived class has the member only until the game loads a class, so a class loaded later still counts.
    [Test]
    public void Member_NullObjectMemberOnClassLoadedAfterAFailedStep_Succeeds()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        var gameMode = _game.GWorld.Path.Member("GameMode");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gameMode.Member("Hp").IsOk, Is.False);
            Assert.That(gameMode.Member("Hp").IsOk, Is.False);

            _fakeGame.CreateClass("FIXTUREDATA", _fakeGame.GameModeBaseClass, "HP");

            Assert.That(gameMode.Member("Hp").IsOk, Is.True);
        }
    }

    // A struct type doesn't have derived types, so a typo after Coord fails on the step while Data is null.
    [Test]
    public void Member_NullObjectTypoAfterAStruct_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(
            Data.Member("Coord").Member("Z").UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Coord.Z. Class FixtureCoord doesn't have a property Z."));
    }

    // Hp is an IntProperty on FixtureData, so a member after it fails on the step while Data is null.
    [Test]
    public void Member_NullObjectAfterADeclaredValue_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(
            Data.Member("Hp").Member("X").UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.X at GWorld.GameMode.Data.Hp. Hp is IntProperty, which doesn't have members."));
    }

    // IntArray is a collection on FixtureData, so Count gets checked and a member called Count fails while Data is null.
    [Test]
    public void Count_NullObjectOnADeclaredCollection_IsChecked()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("IntArray").Count().Unwrap().IsChecked, Is.True);
            Assert.That(Data.Member("IntArray").Member("Count").IsErr, Is.True);
        }
    }

    // GWorld is 0, so the steps after it get checked on the class World.
    [Test]
    public void Member_NullRoot_ChecksAgainstTheWorldClass()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.GWorld.Path.Member("GameMode").Unwrap().IsChecked, Is.True);
            Assert.That(
                _game.GWorld.Path.Member("GameMod").UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMod. Class World and its loaded derived classes don't have a property GameMod."));
        }
    }

    // A document without PropertyClass can't check after a null object, so a typo only fails at the 1st read with the object there.
    [Test]
    public void Member_ProfileWithoutPropertyClass_StaysUnchecked()
    {
        string document = Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FObjectProperty");
        var game = _fakeGame.Attach(Remove(document, "offsets", "UObjectProperty"));
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        var typo = game.GWorld.Path.Member("GameMode").Member("Data").Member("Hpp").Unwrap();

        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, _fakeGame.Data);
        var error = typo.Read().UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(typo.IsChecked, Is.False);
            Assert.That(error.Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Hpp. Class FixtureData doesn't have a property Hpp."));
            Assert.That(error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
        }
    }

    // A declared class that doesn't read leaves the step unchecked and doesn't fail it.
    [Test]
    public void Member_DeclaredClassUnreadable_StaysUnchecked()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.PropertyAddress("Data") + FakeGame.PropertyClassOffset, _fakeGame.UnreadableAddress);
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        var typo = Data.Member("Hpp");

        Assert.That(typo.Unwrap().IsChecked, Is.False);
    }

    // The not-found error of a class stays, so every read of the typo fails with the same error instance.
    [Test]
    public void Read_TypoOnAnUncheckedStep_FailsWithTheSameShapeErrorOnEveryRead()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.PropertyAddress("Data") + FakeGame.PropertyClassOffset, _fakeGame.UnreadableAddress);
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        var typo = Data.Member("Hpp").Unwrap();
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, _fakeGame.Data);

        var first = typo.Read().UnwrapErr();
        var second = typo.Read().UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(first, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
        }
    }

    // Hp is declared as an IntProperty, so a string source fails on the step while Data is null.
    [Test]
    public void Value_NullObjectDeclaredKindOfAnotherType_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(
            Data.Member("Hp").Value<string>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp. Hp is IntProperty, which reads as Int32, not String."));
    }
}
