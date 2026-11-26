using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealInt32PathEdgeTests(
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

    private UnrealInt32Path Hp => _game.GWorld.ObjectMember("GameMode").ObjectMember("Data").Int32Member("Hp");

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
            _game.GWorld.ObjectMember("GameMode").ObjectMember("Data").ObjectMember("Hp").Int32Member("Speed").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.Speed at GWorld.GameMode.Data.Hp. Hp is IntProperty, not ObjectProperty."));
    }

    // The path keeps 1 null error per hop, so a null that moves to a later hop fails with that hop.
    [Test]
    public void Read_NullMovesToALaterHop_FailsWithTheLaterHop()
    {
        var hp = Hp;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        string gameModeNull = hp.Read().UnwrapErr().Message;

        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        string dataNull = hp.Read().UnwrapErr().Message;

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
        var watcher = Hp.CreateWatcher(ticks);
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
        var watcher = Hp.CreateWatcher(ticks);
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
        var watcher = Hp.CreateWatcher(ticks);
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
