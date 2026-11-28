using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPathShareEdgeTests(
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

    private Address GameModeSlot => _fakeGame.World + FakeGame.GameModeOffset;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // A null game mode fails both watchers with their own path. The next tick reads the shared steps once and each watcher keeps its error.
    [Test]
    public void CreateWatcher_NullHopOnTheSharedSteps_EachWatcherFailsWithItsOwnPath()
    {
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var speed = Data("Speed").CreateWatcher(ticks);
        _fakeGame.Memory.WritePointer(GameModeSlot, Address.Zero);
        (_, _) = (hp.Current, speed.Current);
        var errors = (hp.Error, speed.Error);
        ticks.Value++;

        int reads = _fakeGame.Memory.Reads;
        (_, _) = (hp.Current, speed.Current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Error?.Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode is null."));
            Assert.That(speed.Error?.Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Speed: GWorld.GameMode is null."));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + 3));
            Assert.That((hp.Error, speed.Error), Is.EqualTo(errors));
        }
    }

    // A game mode pointer with every bit set fails the class read of the shared hop and both watchers get their path and the cause.
    [Test]
    public void CreateWatcher_UnreadableHopOnTheSharedSteps_EachWatcherHasItsPathAndTheCause()
    {
        Address garbage = new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var speed = Data("Speed").CreateWatcher(ticks);
        _fakeGame.Memory.WritePointer(GameModeSlot, garbage);

        (_, _) = (hp.Current, speed.Current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                hp.Error?.Message,
                Does.StartWith($"Can't read GWorld.GameMode.Data.Hp at GWorld.GameMode.Data. Can't read the class of the object at {garbage}: "));
            Assert.That(
                speed.Error?.Message,
                Does.StartWith($"Can't read GWorld.GameMode.Data.Speed at GWorld.GameMode.Data. Can't read the class of the object at {garbage}: "));
        }
    }

    // OtherGameMode keeps Data at another offset, so both watchers read through the offset of the new class on the next tick.
    [Test]
    public void CreateWatcher_ClassOfTheObjectChangesBetweenTicks_UsesTheOffsetsOfTheNewClass()
    {
        ManualTicks ticks = new();
        var data = Data().CreateWatcher(ticks);
        var hp = Data("Hp").CreateWatcher(ticks);
        (_, _) = (data.Current, hp.Current);

        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.OtherGameMode);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.Current, Is.EqualTo(_fakeGame.OtherData));
            Assert.That(hp.Current, Is.EqualTo(500));
        }
    }

    // A class at a freed address is another class, so the class of the game mode is read again on the next tick and Data uses its offset.
    [Test]
    public void CreateWatcher_ClassReplacedAtTheSameAddressBetweenTicks_UsesTheOffsetOfTheNewClass()
    {
        ManualTicks ticks = new();
        var data = Data().CreateWatcher(ticks);
        var hp = Data("Hp").CreateWatcher(ticks);
        (_, _) = (data.Current, hp.Current);

        _fakeGame.ReplaceFixtureGameModeClass();
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.Current, Is.EqualTo(_fakeGame.OtherData));
            Assert.That(hp.Current, Is.EqualTo(500));
        }
    }

    // The data object got another class at its address, so every watcher from its path fails with the freed object and its own path.
    [Test]
    public void CreateWatcher_ObjectRootFreed_EveryWatcherFailsWithObjectGone()
    {
        ManualTicks ticks = new();
        var obj = _game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single(instance => instance.Address == _fakeGame.Data);
        var hp = obj.Path.Member("Hp").Unwrap().CreateWatcher(ticks);
        var speed = obj.Path.Member("Speed").Unwrap().CreateWatcher(ticks);
        (_, _) = (hp.Current, speed.Current);

        _fakeGame.WriteClass(_fakeGame.Data, _fakeGame.FixtureGameModeClass);
        ticks.Value++;
        (_, _) = (hp.Current, speed.Current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                hp.Error?.Message,
                Is.EqualTo($"Can't read FixtureData.Hp at FixtureData. The FixtureData object at {_fakeGame.Data} was freed."));
            Assert.That(
                speed.Error?.Message,
                Is.EqualTo($"Can't read FixtureData.Speed at FixtureData. The FixtureData object at {_fakeGame.Data} was freed."));
        }
    }

    // Items[1].Speed after Items[1].Hp only reads its value and an index past the count fails both watchers with their own path.
    [Test]
    public void CreateWatcher_CollectionItemOnTheSharedSteps_FindsTheItemOnce()
    {
        ManualTicks ticks = new();
        var hp = Item(1, "Hp").CreateWatcher(ticks);
        var speed = Item(1, "Speed").CreateWatcher(ticks);
        var missingHp = Item(5, "Hp").CreateWatcher(ticks);
        var missingSpeed = Item(5, "Speed").CreateWatcher(ticks);
        (_, _, _, _) = (hp.Current, speed.Current, missingHp.Current, missingSpeed.Current);
        ticks.Value++;

        (_, _) = (hp.Current, missingHp.Current);
        int reads = _fakeGame.Memory.Reads;
        (_, _) = (speed.Current, missingSpeed.Current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Current, Is.EqualTo(500));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1));
            Assert.That(missingHp.Error?.Message, Does.StartWith("Can't read GWorld.GameMode.Data.Items[5].Hp at GWorld.GameMode.Data.Items[5]. "));
            Assert.That(
                missingSpeed.Error?.Message,
                Does.StartWith("Can't read GWorld.GameMode.Data.Items[5].Speed at GWorld.GameMode.Data.Items[5]. "));
        }
    }

    // Watchers of 1 counter read the shared steps once per tick, so a game mode written between 2 watcher reads shows up on the next tick.
    [Test]
    public void CreateWatcher_PointerWrittenInsideTheTick_NextWatcherKeepsTheSharedStepsOfTheTick()
    {
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var data = Data().CreateWatcher(ticks);

        _ = hp.Current;
        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.OtherGameMode);
        object? sameTick = data.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameTick, Is.EqualTo(_fakeGame.Data));
            Assert.That(data.Current, Is.EqualTo(_fakeGame.OtherData));
            Assert.That(hp.Current, Is.EqualTo(500));
        }
    }

    // 30 watchers with shared steps don't allocate on a steady tick.
    [Test]
    public void CreateWatcher_SharedStepsSteadyTicks_DoesNotAllocate()
    {
        Assert.That(SteadyTickBytes(), Is.Zero);
    }

    // 30 watchers that fail on a null game mode on every tick don't allocate either.
    [Test]
    public void CreateWatcher_SharedNullStepTicks_DoesNotAllocate()
    {
        _fakeGame.Memory.WritePointer(GameModeSlot, Address.Zero);

        Assert.That(SteadyTickBytes(), Is.Zero);
    }

    // A null GWorld fails every watcher with its own path at 1 read per tick and the watcher of the root alone gets address 0.
    [Test]
    public void CreateWatcher_NullRootUnderARootWatcher_ReadsTheRootOnce()
    {
        ManualTicks ticks = new();
        var root = _game.GWorld.Path.CreateWatcher(ticks);
        var hp = Data("Hp").CreateWatcher(ticks);
        var speed = Data("Speed").CreateWatcher(ticks);
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, Address.Zero);
        (_, _, _) = (root.Current, hp.Current, speed.Current);
        ticks.Value++;

        int reads = _fakeGame.Memory.Reads;
        (_, _, _) = (root.Current, hp.Current, speed.Current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1));
            Assert.That(root.Current, Is.EqualTo(Address.Zero));
            Assert.That(hp.Error?.Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld is null."));
            Assert.That(speed.Error?.Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Speed: GWorld is null."));
        }
    }

    // The watcher of the root alone reads the root of the tick the others already read, so it doesn't add a read.
    [Test]
    public void CreateWatcher_RootWatcherNextToSharedWatchers_DoesNotAddARead()
    {
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var speed = Data("Speed").CreateWatcher(ticks);
        var root = _game.GWorld.Path.CreateWatcher(ticks);
        (_, _, _) = (hp.Current, speed.Current, root.Current);
        ticks.Value++;

        int reads = _fakeGame.Memory.Reads;
        (_, _, _) = (hp.Current, speed.Current, root.Current);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(9 + 2));
            Assert.That(root.Current, Is.EqualTo(_fakeGame.World));
        }
    }

    // A class read that fails on 1 tick isn't kept past it, so the watchers read again once the game mode is back.
    [Test]
    public void CreateWatcher_UnreadableHopThenBack_EveryWatcherReadsAgain()
    {
        Address garbage = new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var speed = Data("Speed").CreateWatcher(ticks);
        _fakeGame.Memory.WritePointer(GameModeSlot, garbage);
        (_, _) = (hp.Current, speed.Current);

        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.GameMode);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Current, Is.EqualTo(FakeGame.Hp));
            Assert.That(speed.Current, Is.EqualTo(FakeGame.Speed));
            Assert.That(hp.Error, Is.Null);
        }
    }

    // GameModeBase doesn't have Data, so both watchers fail with their own path.
    // They keep the error on the next tick and read again once the game mode is back.
    [Test]
    public void CreateWatcher_MemberMissingOnTheNewClass_EachWatcherFailsThenReadsAgain()
    {
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var speed = Data("Speed").CreateWatcher(ticks);
        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.BaseGameMode);
        (_, _) = (hp.Current, speed.Current);
        var errors = (hp.Error, speed.Error);
        ticks.Value++;
        (_, _) = (hp.Current, speed.Current);
        var keptErrors = (hp.Error, speed.Error);

        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.GameMode);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                errors.Item1?.Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Hp at GWorld.GameMode.Data. Class GameModeBase doesn't have a property Data."));
            Assert.That(
                errors.Item2?.Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Speed at GWorld.GameMode.Data. Class GameModeBase doesn't have a property Data."));
            Assert.That(keptErrors, Is.EqualTo(errors));
            Assert.That(hp.Current, Is.EqualTo(FakeGame.Hp));
            Assert.That(speed.Current, Is.EqualTo(FakeGame.Speed));
        }
    }

    // Watchers of 2 counters take turns inside every tick, so each read replaces the other's kept steps and the values match single reads.
    [Test]
    public void CreateWatcher_TwoCountersTakingTurns_GiveTheValuesOfSingleReads()
    {
        ManualTicks firstTicks = new();
        ManualTicks secondTicks = new();
        string[] names = ["Hp", "Speed", "Hp", "Speed"];
        var watchers = names.Select((name, index) => Data(name).CreateWatcher(index % 2 == 0 ? firstTicks : secondTicks)).ToArray();
        var singleTicks = names.Select(_ => new ManualTicks()).ToArray();
        var singles = names.Select((name, index) => Data(name).CreateWatcher(singleTicks[index])).ToArray();
        Address[] gameModes = [_fakeGame.GameMode, Address.Zero, _fakeGame.OtherGameMode, _fakeGame.BaseGameMode, _fakeGame.GameMode];

        using (Assert.EnterMultipleScope())
        {
            foreach (var gameMode in gameModes)
            {
                _fakeGame.Memory.WritePointer(GameModeSlot, gameMode);
                firstTicks.Value++;
                secondTicks.Value += 2;

                for (int index = 0; index < watchers.Length; index++)
                {
                    singleTicks[index].Value++;

                    Assert.That(watchers[index].Current, Is.EqualTo(singles[index].Current), $"{gameMode} {watchers[index]}");
                    Assert.That(watchers[index].Error?.Message, Is.EqualTo(singles[index].Error?.Message), $"{gameMode} {watchers[index]}");
                }
            }
        }
    }

    // A pointer written between 2 watchers of 1 counter shows up for the 2nd one after a watcher of another counter read in between.
    [Test]
    public void CreateWatcher_AnotherCounterReadsInsideTheTick_NextWatcherReadsThePathAgain()
    {
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        var data = Data().CreateWatcher(ticks);
        var other = Data("Speed").CreateWatcher(new ManualTicks());

        _ = hp.Current;
        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.OtherGameMode);
        _ = other.Current;

        Assert.That(data.Current, Is.EqualTo(_fakeGame.OtherData));
    }

    // IntArray.Count() and a member called Count are 2 paths in either order, so the member fails on the step and Count reads.
    [Test]
    public void Count_BeforeAMemberCalledCount_StaysAnotherPath()
    {
        var intArray = _game.GWorld.Path.Member("GameMode").Member("Data").Member("IntArray").Unwrap();
        ManualTicks ticks = new();
        var count = intArray.Count().Unwrap().CreateWatcher(ticks);
        var member = intArray.Member("Count");
        var countAgain = intArray.Count().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count.Current, Is.EqualTo(3));
            Assert.That(member.UnwrapErr().Message, Does.EndWith("IntArray is a collection, so it takes an index or Count, not the member Count."));
            Assert.That(countAgain.CreateWatcher(ticks).Current, Is.EqualTo(3));
        }
    }

    // The index 1 and the key "1" are 2 paths in either order, so the key fails on the step and the index reads.
    [Test]
    public void Item_TextKeyOfAnIndex_StaysAnotherPath()
    {
        var intArray = _game.GWorld.Path.Member("GameMode").Member("Data").Member("IntArray").Unwrap();
        ManualTicks ticks = new();
        var index = intArray.Item(1).Unwrap().CreateWatcher(ticks);
        var key = intArray.Item("1");
        var indexAgain = intArray.Item(1L).Unwrap().CreateWatcher(ticks);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(key.UnwrapErr().Message, Does.EndWith("The keys of IntArray are Int32, which the key \"1\" can't match."));
            Assert.That(index.Current, Is.Not.Null);
            Assert.That(indexAgain.Current, Is.EqualTo(index.Current));
        }
    }

    // A member with an accent or an empty member is its own path and fails with its own text.
    [TestCase("Hpé")]
    [TestCase("")]
    public void Member_NameThatOnlyLooksEqual_FailsWithItsOwnText(string name)
    {
        var data = _game.GWorld.Path.Member("GameMode").Member("Data").Unwrap();
        var hp = data.Member("Hp").Unwrap().CreateWatcher(new ManualTicks());

        var other = data.Member(name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(other.UnwrapErr().Message, Does.StartWith($"Can't read GWorld.GameMode.Data.{name}. "));
            Assert.That(hp.Current, Is.EqualTo(FakeGame.Hp));
        }
    }

    // The engine ignores case in names, so hp reads Hp and the path prints the name as written.
    [Test]
    public void Member_NameInOtherCase_ReadsTheSameValue()
    {
        var hp = _game.GWorld.Path.Member("GameMode").Member("Data").Member("hp").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Read<int>().Unwrap(), Is.EqualTo(FakeGame.Hp));
            Assert.That(hp.ToString(), Is.EqualTo("GWorld.GameMode.Data.hp"));
        }
    }

    // A failed step doesn't keep its path under the root, so the GC frees its error while the root lives.
    [Test]
    public void Member_FailedStep_DoesNotKeepThePath()
    {
        var data = Data();
        var error = FailStep(data);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.That(error.IsAlive, Is.False);
        GC.KeepAlive(data);
    }

    // A watcher enabled again in the tick reads its value again at the Data its counter already reached in that tick.
    [Test]
    public void CreateWatcher_EnabledAgainInsideTheTick_KeepsTheSharedStepsOfTheTick()
    {
        ManualTicks ticks = new();
        var hp = Data("Hp").CreateWatcher(ticks);
        _ = hp.Current;

        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);
        _fakeGame.Memory.WritePointer(GameModeSlot, _fakeGame.OtherGameMode);
        hp.Enabled = false;
        hp.Enabled = true;
        object? sameTick = hp.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameTick, Is.EqualTo(990));
            Assert.That(hp.Current, Is.EqualTo(500));
        }
    }

    // Step to a member that FixtureData doesn't have in its own method, so the result doesn't stay alive on the stack of the test.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FailStep(IMemberPath path)
    {
        return new WeakReference(path.Member("Missing").UnwrapErr());
    }

    private IMemberPath Data(params string[] names)
    {
        var path = _game.GWorld.Path.Member("GameMode").Member("Data");

        foreach (string name in names)
        {
            path = path.Member(name);
        }

        return path.Unwrap();
    }

    private IMemberPath Item(long index, string name)
    {
        return _game.GWorld.Path.Member("GameMode").Member("Data").Member("Items").Item(index).Member(name).Unwrap();
    }

    // Read 30 watchers over many ticks after 2 ticks that resolve the classes and return the bytes the ticks allocated.
    private long SteadyTickBytes()
    {
        ManualTicks ticks = new();
        string[] names = ["Hp", "Speed", "Int8Value"];
        var watchers = Enumerable.Range(0, 30).Select(index => Data(names[index % names.Length]).CreateWatcher(ticks)).ToArray();

        for (int tick = 0; tick < 2; tick++)
        {
            Tick(watchers);
            ticks.Value++;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int tick = 0; tick < ProbeCount; tick++)
        {
            Tick(watchers);
            ticks.Value++;
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Tick(Watcher<object?>[] watchers)
    {
        foreach (var watcher in watchers)
        {
            _ = watcher.Current;
        }
    }
}
