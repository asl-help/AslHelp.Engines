using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealValuePathEdgeTests(
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

    // Hp is an IntProperty in the middle of the path, so the hop through it fails and not the Int32 end.
    [Test]
    public void Read_WrongKindInTheMiddle_FailsAtThatHop()
    {
        Assert.That(
            _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp").Member("Speed").Read<int>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.Speed at GWorld.GameMode.Data.Hp. Hp is IntProperty, which doesn't have members."));
    }

    // Hp is an IntProperty, so a float read fails on the step with both types.
    [Test]
    public void Value_WrongTypeOnALiveObject_FailsOnTheStep()
    {
        Assert.That(
            _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp").Value<float>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp. Hp is IntProperty, which reads as Int32, not Single."));
    }

    // A typed step after a null game mode can't check the kind, so the watcher fails with both types once the game mode is there.
    [Test]
    public void Value_WrongTypeAfterANullObject_FailsWhenTheWatcherReads()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        ManualTicks ticks = new();
        var watcher = _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp").Value<float>().Unwrap().CreateWatcher(ticks);

        _ = watcher.Current;
        string? nullObject = watcher.Error?.Message;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullObject, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode is null."));
            Assert.That(watcher.Current, Is.Zero);
            Assert.That(
                watcher.Error?.Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Hp. Hp is IntProperty, which reads as Int32, not Single."));
        }
    }

    // The path keeps 1 null error per hop, so a null that moves to a later hop fails with that hop.
    [Test]
    public void Read_NullMovesToALaterHop_FailsWithTheLaterHop()
    {
        var hp = _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp");
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        string gameModeNull = hp.Read<int>().UnwrapErr().Message;

        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        string dataNull = hp.Read<int>().UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gameModeNull, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode is null."));
            Assert.That(dataNull, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode.Data is null."));
        }
    }

    // FailAction.KeepOld keeps both values through a null game mode and doesn't report a change.
    [Test]
    public void CreateWatcher_FailActionKeepOld_KeepsTheValues()
    {
        ManualTicks ticks = new();
        var watcher = Hp.Unwrap().CreateWatcher(ticks);
        watcher.FailAction = FailAction.KeepOld;

        _ = watcher.Current;
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);
        ticks.Value++;
        _ = watcher.Current;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        ticks.Value++;

        Assert.That((watcher.Current, watcher.Old, watcher.Changed), Is.EqualTo((990, FakeGame.Hp, false)));
    }

    // A tick after the 1st doesn't allocate.
    [Test]
    public void CreateWatcher_SteadyTicks_DoesNotAllocate()
    {
        ManualTicks ticks = new();
        var watcher = Hp.Unwrap().CreateWatcher(ticks);
        _ = watcher.Current;

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            ticks.Value++;
            _ = watcher.Current;
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // A watcher that fails on a null game mode each tick doesn't allocate, because a loading game fails that way on every tick.
    [Test]
    public void CreateWatcher_NullObjectTicks_DoesNotAllocate()
    {
        ManualTicks ticks = new();
        var watcher = Hp.Unwrap().CreateWatcher(ticks);
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        _ = watcher.Current;

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            ticks.Value++;
            _ = watcher.Current;
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
}
