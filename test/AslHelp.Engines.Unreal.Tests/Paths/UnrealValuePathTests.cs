using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealValuePathTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    private const int ProbeCount = 1000;

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

    private Result<IWatcherSource<int>> Hp => _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp").Value<int>();

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // Hp is read from the data object at the end of 2 object hops.
    [Test]
    public void Read_TwoObjectHopsToAnInt_ReadsTheValue()
    {
        Assert.That(Hp.Read().Unwrap(), Is.EqualTo(FakeGame.Hp));
    }

    // A read after the 1st costs 1 read of the slot, then the class pointer, the class name and the value per hop.
    [Test]
    public void Read_SteadyState_ReadsTheSlotAndThreeReadsPerHop()
    {
        var hp = Hp;
        _ = hp.Read().Unwrap();

        int reads = _fakeGame.Memory.Reads;
        int value = hp.Read().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(FakeGame.Hp));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (3 * 3)));
        }
    }

    // A read after the 1st doesn't allocate, because a watcher reads the path on each tick.
    [Test]
    public void Read_SteadyState_DoesNotAllocate()
    {
        var hp = Hp;
        _ = hp.Read().Unwrap();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            _ = hp.Read();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // GameMode is an ObjectProperty, so an Int32 end on it fails with its kind.
    [Test]
    public void Read_ObjectMemberAsInt_FailsWithWrongKind()
    {
        Assert.That(
            _game.GWorld.Path.Member("GameMode").Value<int>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode. GameMode is ObjectProperty, which reads as Address, not Int32."));
    }

    // Speed is a FloatProperty, which the library doesn't read.
    [Test]
    public void Read_FloatMemberAsInt_FailsWithWrongKind()
    {
        Assert.That(
            _game.GWorld.Path.Member("GameMode").Member("Data").Member("Speed").Value<int>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Speed. Speed is FloatProperty, which reads as Single, not Int32."));
    }

    // A data object of 0 fails the value with the path up to the data object.
    [Test]
    public void Read_NullLastObject_FailsWithTheNullObject()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(Hp.Read().UnwrapErr().Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode.Data is null."));
    }

    // An offset past the block of the data object fails with the address of the value.
    [Test]
    public void Read_UnreadableValue_FailsWithTheAddressAndTheCause()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.HpProperty + FakeGame.PropertyOffsetOffset, 0x400);
        var value = _fakeGame.Data + 0x400;

        Assert.That(
            Hp.Read().UnwrapErr().Message,
            Does.StartWith($"Can't read GWorld.GameMode.Data.Hp. Can't read Hp of the object at {_fakeGame.Data}: reading {value} failed. "));
    }

    // The 1st read only fills the values.
    [Test]
    public void CreateWatcher_ReadableValue_CurrentIsTheValue()
    {
        var watcher = Hp.Unwrap().CreateWatcher(new ManualTicks());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A value written between 2 ticks shows up as a change on the 2nd tick.
    [Test]
    public void CreateWatcher_ValueChangesBetweenTicks_ReportsOldAndCurrent()
    {
        ManualTicks ticks = new();
        var watcher = Hp.Unwrap().CreateWatcher(ticks);

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

    // Every value of 1 tick comes from 1 read of the path, so a value written inside the tick waits for the next one.
    [Test]
    public void CreateWatcher_SameTick_ReadsOnce()
    {
        ManualTicks ticks = new();
        var hp = Hp;
        _ = hp.Read().Unwrap();
        var watcher = hp.Unwrap().CreateWatcher(ticks);

        int reads = _fakeGame.Memory.Reads;
        _ = watcher.Current;
        _ = watcher.Old;
        _ = watcher.Changed;
        int readsInTick = _fakeGame.Memory.Reads - reads;
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readsInTick, Is.EqualTo(1 + (3 * 3)));
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
        }

        ticks.Value++;

        Assert.That(watcher.Current, Is.EqualTo(990));
    }

    // With the default FailAction.Clear a failed read sets both values to 0 and the next read fills them without a change.
    [Test]
    public void CreateWatcher_FailedRead_ClearsTheValuesAndFillsAgain()
    {
        ManualTicks ticks = new();
        var watcher = Hp.Unwrap().CreateWatcher(ticks);

        _ = watcher.Current;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        ticks.Value++;
        (int current, int old, bool changed) = (watcher.Current, watcher.Old, watcher.Changed);
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That((current, old, changed), Is.EqualTo((0, 0, false)));
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // FailAction.Throw throws with the path in the text of the watcher and in the error.
    [Test]
    public void CreateWatcher_FailActionThrow_ThrowsWithThePath()
    {
        var watcher = Hp.Unwrap().CreateWatcher(new ManualTicks());
        watcher.FailAction = FailAction.Throw;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.StartsWith(
                "Couldn't read Int32 at GWorld.GameMode.Data.Hp. Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode is null."));
    }

    // Each call through IWatcherSource creates a new watcher.
    [Test]
    public void CreateWatcher_ThroughIWatcherSource_ReturnsANewWatcherPerCall()
    {
        IWatcherSource source = Hp.Unwrap();
        ManualTicks ticks = new();

        var first = source.CreateWatcher(ticks);
        var second = source.CreateWatcher(ticks);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.InstanceOf<Watcher<int>>());
            Assert.That(second, Is.Not.SameAs(first));
        }
    }

    // CreateWatcher throws on a null tick counter.
    [Test]
    public void CreateWatcher_NullTicks_Throws()
    {
        Assert.That(() => Hp.Unwrap().CreateWatcher(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("ticks"));
    }
}
