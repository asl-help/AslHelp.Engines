using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameChainTests(
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

    // A chain prints the same text as the path with the same members.
    [Test]
    public void GWorldChain_MembersAfterTheChain_PrintsThePath()
    {
        object chain = _game.GWorldChain.GameMode.Data.Hp;

        Assert.That(chain.ToString(), Is.EqualTo(_game.GWorld.ObjectMember("GameMode").ObjectMember("Data").Int32Member("Hp").ToString()));
    }

    // A chain that ends on an IntProperty creates a typed watcher that reads the value.
    [Test]
    public void CreateWatcher_ChainToAnInt_CurrentIsTheValue()
    {
        IWatcherSource hp = _game.GWorldChain.GameMode.Data.Hp;

        var watcher = (Watcher<int>)hp.CreateWatcher(new ManualTicks());

        Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
    }

    // A value written between 2 ticks shows up as a change on the 2nd tick.
    [Test]
    public void CreateWatcher_ValueChangesBetweenTicks_ReportsOldAndCurrent()
    {
        IWatcherSource hp = _game.GWorldChain.GameMode.Data.Hp;
        ManualTicks ticks = new();
        var watcher = (Watcher<int>)hp.CreateWatcher(ticks);

        _ = watcher.Current;
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.EqualTo(FakeGame.Hp));
            Assert.That(watcher.Current, Is.EqualTo(990));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A chain that ends on an ObjectProperty still creates a watcher and fails with the kind when the watcher reads.
    [Test]
    public void CreateWatcher_ChainToAnObject_FailsWhenTheWatcherReads()
    {
        IWatcherSource gameMode = _game.GWorldChain.GameMode;
        var watcher = (Watcher<int>)gameMode.CreateWatcher(new ManualTicks());
        watcher.FailAction = FailAction.Throw;

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.StartsWith(
                "Couldn't read Int32 at GWorld.GameMode. Can't read GWorld.GameMode. GameMode is ObjectProperty, not IntProperty."));
    }

    // A misspelled member doesn't throw on the script line. It fails when the watcher reads, with the class and the name.
    [Test]
    public void CreateWatcher_MisspelledMember_FailsWhenTheWatcherReads()
    {
        IWatcherSource hp = _game.GWorldChain.GameMode.Data.Hpp;
        var watcher = (Watcher<int>)hp.CreateWatcher(new ManualTicks());
        watcher.FailAction = FailAction.Throw;

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.StartsWith(
                "Couldn't read Int32 at GWorld.GameMode.Data.Hpp. "
                + "Can't read GWorld.GameMode.Data.Hpp. Class FixtureData doesn't have a property Hpp."));
    }

    // CreateWatcher throws for the chain of the global alone, because it doesn't end on a member.
    [Test]
    public void CreateWatcher_ChainOfTheGlobalAlone_Throws()
    {
        IWatcherSource world = _game.GWorldChain;

        Assert.That(
            () => world.CreateWatcher(new ManualTicks()),
            Throws.InvalidOperationException.With.Message.EqualTo("Can't watch GWorld: it isn't an IntProperty."));
    }
}
