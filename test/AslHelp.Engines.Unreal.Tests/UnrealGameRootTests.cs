using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameRootTests(
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

    // GEngine points at an object of GameEngine and its base Engine declares Data.
    [Test]
    public void GEngine_Path_ReadsAMemberThroughTheEngineObject()
    {
        var hp = _game.GEngine.Member("Data").Member<int>("Hp");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Read().Unwrap(), Is.EqualTo(FakeGame.Hp));
            Assert.That(hp.ToString(), Is.EqualTo("GEngine.Data.Hp"));
        }
    }

    // A chain from GEngine is a watcher source like a chain from GWorld.
    [Test]
    public void GEngineChain_Member_WatchesTheValue()
    {
        IWatcherSource hp = _game.GEngineChain.Data.Hp;

        var watcher = (Watcher<object?>)hp.CreateWatcher(new ManualTicks());

        Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
    }

    // A slot of 0 fails like a null object and the same path reads once the game sets the slot.
    [Test]
    public void GEngine_NullSlot_FailsThenReadsOnceTheSlotIsSet()
    {
        var hp = _game.GEngine.Member("Data").Member<int>("Hp");
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, Address.Zero);

        var failure = hp.Read();
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, _fakeGame.Engine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Is.EqualTo("Can't read GEngine.Data.Hp: GEngine is null."));
            Assert.That(hp.Read().Unwrap(), Is.EqualTo(FakeGame.Hp));
        }
    }

    // A document without the subsystem collection is of a version before 4.22, so the lookup fails without a read.
    [Test]
    public void FindSubsystem_ProfileBefore422_FailsWithTheVersionAndTheOffset()
    {
        int reads = _fakeGame.Memory.Reads;

        var result = _game.FindSubsystem("FixtureSubsystem");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.UnwrapErr().Message, Is.EqualTo("Subsystems need 4.22 or later and UGameInstance.SubsystemCollection in the profile."));
            Assert.That(_fakeGame.Memory.Reads, Is.EqualTo(reads));
        }
    }

    // The root finds the live object of the class and its text is the class.
    [Test]
    public void FindSubsystem_LiveSubsystem_ReadsItsValue()
    {
        _fakeGame.AddObject(_fakeGame.Subsystem);

        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member<int>("SubsystemValue");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Read().Unwrap(), Is.EqualTo(FakeGame.SubsystemValue));
            Assert.That(value.ToString(), Is.EqualTo("FixtureSubsystem.SubsystemValue"));
        }
    }

    // A missing subsystem fails without reading a class pointer again on the same array and reads once the game creates it.
    [Test]
    public void FindSubsystem_SubsystemThatAppearsLater_FailsThenReads()
    {
        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member<int>("SubsystemValue");

        var failure = value.Read();
        int classReads = _fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Class);
        _ = value.Read();
        int secondReads = _fakeGame.Memory.ReadsAt(_fakeGame.World + FakeGame.ObjectLayout.Class) - classReads;
        _fakeGame.AddObject(_fakeGame.Subsystem);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                failure.UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read FixtureSubsystem.SubsystemValue at FixtureSubsystem. "
                    + "Can't find a live object of the class FixtureSubsystem."));
            Assert.That(secondReads, Is.Zero);
            Assert.That(value.Read().Unwrap(), Is.EqualTo(FakeGame.SubsystemValue));
        }
    }

    // The game counts the array up to its highest slot, so a subsystem in the slot of a freed object leaves the count as it was.
    [Test]
    public void FindSubsystem_SubsystemInTheSlotOfAFreedObject_FailsThenReads()
    {
        _ = _fakeGame.CreateObject("Default__FixtureSubsystem", _fakeGame.SubsystemClass);
        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member<int>("SubsystemValue");

        var failure = value.Read();
        _fakeGame.ReplaceLastObject(_fakeGame.Subsystem);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.UnwrapErr().Message, Does.EndWith("Can't find a live object of the class FixtureSubsystem."));
            Assert.That(value.Read().Unwrap(), Is.EqualTo(FakeGame.SubsystemValue));
        }
    }

    // The kept object got another class, so the root searches again and reads the new subsystem.
    [Test]
    public void FindSubsystem_SubsystemReplaced_FollowsTheNewObject()
    {
        _fakeGame.AddObject(_fakeGame.Subsystem);
        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member<int>("SubsystemValue");
        _ = value.Read().Unwrap();

        _fakeGame.WriteClass(_fakeGame.Subsystem, _fakeGame.FixtureGameModeClass);
        var replacement = _fakeGame.CreateObject("FixtureSubsystem", _fakeGame.SubsystemClass);
        _fakeGame.Memory.WriteInt32(replacement + FakeGame.SubsystemValueOffset, 17);

        Assert.That(value.Read().Unwrap(), Is.EqualTo(17));
    }

    // Once found, a read costs the class pointer of the subsystem, its class with the name and the value, without a pass over the array.
    [Test]
    public void FindSubsystem_SteadyRead_ReadsOnlyTheClassPointerAndTheValue()
    {
        _fakeGame.AddObject(_fakeGame.Subsystem);
        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member<int>("SubsystemValue");
        _ = value.Read().Unwrap();
        int reads = _fakeGame.Memory.Reads;

        _ = value.Read().Unwrap();

        Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(4));
    }

    // The default object of the subsystem class isn't the subsystem.
    [Test]
    public void FindSubsystem_DefaultObjectOnly_FailsWithoutALiveObject()
    {
        _ = _fakeGame.CreateObject("Default__FixtureSubsystem", _fakeGame.SubsystemClass);

        var value = SubsystemGame().FindSubsystem("FixtureSubsystem").Unwrap().Path.Member<int>("SubsystemValue");

        Assert.That(value.Read().UnwrapErr().Message, Does.EndWith("Can't find a live object of the class FixtureSubsystem."));
    }

    // Attaches with a document of 4.22 or later, which has the subsystem collection.
    private UnrealGame SubsystemGame()
    {
        string document = FakeGame.Document(shape, "indirect", "flat");

        return _fakeGame.Attach(Set(document, """{ "SubsystemCollection": "0x100" }""", "offsets", "UGameInstance"));
    }
}
