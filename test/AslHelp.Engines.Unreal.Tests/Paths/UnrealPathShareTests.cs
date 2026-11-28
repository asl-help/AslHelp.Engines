using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPathShareTests(
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

    // Lists members of FixtureData whose value is 1 read, so a watcher costs the same for each.
    private static readonly string[] _numberMembers =
        ["Hp", "Speed", "Int8Value", "Int16Value", "UInt16Value", "UInt32Value", "Int64Value", "UInt64Value", "DoubleValue", "ByteValue"];

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // Each watcher builds GWorld.GameMode.Data again like a script line. 1 tick costs the slot, 3 reads per hop, 2 for the class of Data
    // and 1 value read per watcher.
    [TestCase(1, 10)]
    [TestCase(10, 19)]
    [TestCase(30, 39)]
    [TestCase(100, 109)]
    public void CreateWatcher_WatchersWithSharedSteps_ReadTheSharedStepsOnce(int watcherCount, int readsPerTick)
    {
        ManualTicks ticks = new();
        var watchers = WatchNumbers(watcherCount, ticks);
        Tick(watchers);
        ticks.Value++;

        int reads = _fakeGame.Memory.Reads;
        Tick(watchers);

        Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(readsPerTick));
    }

    // The world points at itself, so GWorld and 5 GameMode hops end at the world and 30 watchers of 1 more GameMode read it.
    [Test]
    public void CreateWatcher_FiveSharedSteps_ReadsEachHopOnce()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.World);
        ManualTicks ticks = new();
        var watchers = Enumerable.Range(0, 30).Select(_ => GameModeHops(6).Unwrap().CreateWatcher(ticks)).ToArray();
        Tick(watchers);
        ticks.Value++;

        int reads = _fakeGame.Memory.Reads;
        Tick(watchers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (3 * 5) + 2 + 30));
            Assert.That(watchers.Select(static watcher => watcher.Current), Is.All.EqualTo(_fakeGame.World));
        }
    }

    // Each shared watcher gives what a watcher of its own counter reads after a written value, a null game mode and another game mode.
    [Test]
    public void CreateWatcher_SharedStepsAcrossTicks_GivesTheValuesOfSingleReads()
    {
        ManualTicks ticks = new();
        var watchers = WatchNumbers(_numberMembers.Length, ticks);
        var gameModeSlot = _fakeGame.World + FakeGame.GameModeOffset;

        CheckAgainstSingleReads(watchers);
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);
        ticks.Value++;
        CheckAgainstSingleReads(watchers);
        _fakeGame.Memory.WritePointer(gameModeSlot, Address.Zero);
        ticks.Value++;
        CheckAgainstSingleReads(watchers);
        _fakeGame.Memory.WritePointer(gameModeSlot, _fakeGame.OtherGameMode);
        ticks.Value++;
        CheckAgainstSingleReads(watchers);
    }

    // The game mode changes between 2 ticks, so every watcher follows it and a steady tick after it costs the same reads.
    [Test]
    public void CreateWatcher_NextTick_ReadsTheSharedStepsAgain()
    {
        ManualTicks ticks = new();
        var watchers = WatchNumbers(10, ticks);
        Tick(watchers);
        ticks.Value++;

        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.OtherGameMode);
        Tick(watchers);
        ticks.Value++;
        int reads = _fakeGame.Memory.Reads;
        Tick(watchers);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watchers[0].Current, Is.EqualTo(500));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(9 + 10));
        }
    }

    // 2 watchers of 2 counters read the whole path each, so the 2nd one gets a game mode written after the 1st read.
    [Test]
    public void CreateWatcher_TwoTickCounters_ReadTheSharedStepsEach()
    {
        ManualTicks firstTicks = new();
        ManualTicks secondTicks = new();
        var first = Hp().CreateWatcher(firstTicks);
        var second = Hp().CreateWatcher(secondTicks);
        (_, _) = (first.Current, second.Current);
        firstTicks.Value++;
        secondTicks.Value++;

        int reads = _fakeGame.Memory.Reads;
        object? firstHp = first.Current;
        int readsOfFirst = _fakeGame.Memory.Reads - reads;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.OtherGameMode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstHp, Is.EqualTo(FakeGame.Hp));
            Assert.That(readsOfFirst, Is.EqualTo(1 + (3 * 3)));
            Assert.That(second.Current, Is.EqualTo(500));
        }
    }

    // A read without a watcher and a step check in the tick of a watcher read the whole path and see a write made inside the tick.
    [Test]
    public void Read_WithoutAWatcherInTheSameTick_ReadsThePathAgain()
    {
        var watcher = Hp().CreateWatcher(new ManualTicks());
        var hp = _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp");
        _ = hp.Read<int>().Unwrap();
        _ = watcher.Current;

        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);
        int reads = _fakeGame.Memory.Reads;
        int value = hp.Read<int>().Unwrap();
        int readsOfTheRead = _fakeGame.Memory.Reads - reads;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(990));
            Assert.That(readsOfTheRead, Is.EqualTo(1 + (3 * 3)));
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
            Assert.That(hp.Read<int>().UnwrapErr().Message, Is.EqualTo("Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode is null."));
        }
    }

    // Read each watcher next to a watcher of its own counter, so both read the same memory.
    private void CheckAgainstSingleReads(Watcher<object?>[] watchers)
    {
        using (Assert.EnterMultipleScope())
        {
            for (int index = 0; index < watchers.Length; index++)
            {
                var single = Number(index).CreateWatcher(new ManualTicks());

                Assert.That(watchers[index].Current, Is.EqualTo(single.Current), watchers[index].ToString());
                Assert.That(watchers[index].Error?.Message, Is.EqualTo(single.Error?.Message), watchers[index].ToString());
            }
        }
    }

    private Result<IMemberPath> GameModeHops(int hopCount)
    {
        var path = Result.Ok<IMemberPath>(_game.GWorld.Path);

        for (int hop = 0; hop < hopCount; hop++)
        {
            path = path.Member("GameMode");
        }

        return path;
    }

    private IMemberPath Hp()
    {
        return _game.GWorld.Path.Member("GameMode").Member("Data").Member("Hp").Unwrap();
    }

    private IMemberPath Number(int index)
    {
        return _game.GWorld.Path.Member("GameMode").Member("Data").Member(_numberMembers[index % _numberMembers.Length]).Unwrap();
    }

    // Read every watcher once, like a script does on each tick.
    private static void Tick(Watcher<object?>[] watchers)
    {
        foreach (var watcher in watchers)
        {
            _ = watcher.Current;
        }
    }

    private Watcher<object?>[] WatchNumbers(int watcherCount, ManualTicks ticks)
    {
        return [.. Enumerable.Range(0, watcherCount).Select(index => Number(index).CreateWatcher(ticks))];
    }
}
