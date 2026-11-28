using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Paths;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPathEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    private const int ClassOffset = 0x10;
    private const int HopCount = 100;
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

    private Result<IMemberPath> Data => _game.GWorld.Path.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // A game mode whose block ends before Data reads its class and fails the pointer read with the hop and the address.
    [Test]
    public void Read_ObjectEndsBeforeTheMember_FailsWithTheHopAndTheAddress()
    {
        var gameMode = start + 0x26_0000;
        _fakeGame.Memory.Add(gameMode, new byte[0x20]);
        _fakeGame.Memory.WritePointer(gameMode + ClassOffset, _fakeGame.FixtureGameModeClass);
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, gameMode);
        var pointer = gameMode + FakeGame.DataOffset;

        Assert.That(
            Data.Read<Address>().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data. Can't read Data of the object at {gameMode}: reading {pointer} failed. "
                + $"No block contains {pointer}."));
    }

    // An object the engine is still creating has a class pointer of 0, which fails the hop that reads from it.
    [Test]
    public void Read_ClassPointerZeroInTheMiddle_FailsWithTheHop()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + ClassOffset, Address.Zero);

        Assert.That(
            Data.Read<Address>().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data. The class pointer of the object at {_fakeGame.GameMode} is 0. "
                + "Check ObjectLayout.Class (0x10)."));
    }

    // A freed game mode can leave any bits in the pointer, so the highest address fails with a result and doesn't throw.
    [Test]
    public void Read_HighestAddressInTheMiddle_FailsWithTheClassRead()
    {
        Address garbage = new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, garbage);

        Assert.That(
            Data.Read<Address>().UnwrapErr().Message,
            Does.StartWith($"Can't read GWorld.GameMode.Data. Can't read the class of the object at {garbage}: "));
    }

    // A failed memory read while the step checks the path doesn't fail the step, so the read reports it.
    [Test]
    public void Member_MemoryFailsDuringTheCheck_ReturnsThePath()
    {
        Address garbage = new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, garbage);

        var data = Data;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.IsOk, Is.True);
            Assert.That(
                data.Read<Address>().UnwrapErr().Message,
                Does.StartWith($"Can't read GWorld.GameMode.Data. Can't read the class of the object at {garbage}: "));
        }
    }

    // Hp is an IntProperty, so a member after it fails on the step.
    [Test]
    public void Member_AfterAValueMember_FailsOnTheStep()
    {
        Assert.That(
            Data.Member("Hp").Member("X").UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.X at GWorld.GameMode.Data.Hp. Hp is IntProperty, which doesn't have members."));
    }

    // IntArray is a collection, so a member after it fails on the step, also 1 called Count.
    [Test]
    public void Member_AfterACollection_FailsOnTheStep()
    {
        var intArray = Data.Member("IntArray");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                intArray.Member("Length").UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read GWorld.GameMode.Data.IntArray.Length. "
                    + "IntArray is a collection, so it takes an index or Count, not the member Length."));
            Assert.That(
                intArray.Member("Count").UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read GWorld.GameMode.Data.IntArray.Count. "
                    + "IntArray is a collection, so it takes an index or Count, not the member Count."));
            Assert.That(intArray.Count.Read<int>().Unwrap(), Is.EqualTo(3));
        }
    }

    // IntArray takes an int index and IntDict int keys, so a string key fails on the step and an index past the count only fails the read.
    [Test]
    public void Item_KeyOfTheWrongType_FailsOnTheStep()
    {
        var intArray = Data.Member("IntArray");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                intArray.Item("two").UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read GWorld.GameMode.Data.IntArray[\"two\"]. "
                    + "The keys of IntArray are Int32, which the key \"two\" can't match."));
            Assert.That(Data.Member("IntDict").Item("two").IsErr, Is.True);
            Assert.That(intArray.Item(3).IsOk, Is.True);
        }
    }

    // Data is an object and Hp an int, so an index after Data and Count after Hp fail on the step.
    [Test]
    public void Item_OnANonCollection_FailsOnTheStep()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Data.Item(1).UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data[1]. Data isn't a collection, so it can't take the index 1."));
            Assert.That(
                Data.Member("Hp").Count.UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Hp.Count at GWorld.GameMode.Data.Hp. Hp is IntProperty, which doesn't have members."));
        }
    }

    // Position is a struct and not an array, so an index and Count after it fail on the step.
    [Test]
    public void Item_OnANumberStruct_FailsOnTheStep()
    {
        var position = Data.Member("Position");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                position.Item(0).UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Position[0]. Position isn't a collection, so it can't take the index 0."));
            Assert.That(
                position.Count.UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.Position.Count. Position isn't a collection, so it doesn't have a Count."));
            Assert.That(position.Member("X").Read<float>().Unwrap(), Is.EqualTo(1f));
        }
    }

    // A quote in a map key prints escaped in the path, so the text shows 1 key.
    [Test]
    public void ToString_AKeyWithAQuote_PrintsTheKeyEscaped()
    {
        Assert.That(Data.Member("Dict").Item("a\"][\"b").Unwrap().ToString(), Is.EqualTo("GWorld.GameMode.Data.Dict[\"a\\\"][\\\"b\"]"));
    }

    // 2 watchers of 1 struct share its bound property, so a read of the 2nd doesn't change the value the 1st returned.
    [Test]
    public void CreateWatcher_TwoWatchersOfOneStruct_KeepTheirOwnValues()
    {
        ManualTicks secondTicks = new();
        var first = Data.Member("Position").Unwrap().CreateWatcher(new ManualTicks());
        var second = Data.Member("Position").Unwrap().CreateWatcher(secondTicks);
        var before = (StructValue)first.Current!;
        _ = second.Current;

        _fakeGame.Memory.Write(_fakeGame.Data + FakeGame.PositionOffset + 4, 5f);
        secondTicks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((StructValue)second.Current!)["Y"], Is.EqualTo(5f));
            Assert.That(before["Y"], Is.EqualTo(2f));
            Assert.That(first.Current, Is.SameAs(before));
        }
    }

    // A typed struct watcher keeps its value while no member changes, so Changed stays false.
    [Test]
    public void CreateWatcher_TypedStructSteadyState_KeepsTheValue()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member("Mixed").Value<StructValue>().Unwrap().CreateWatcher(ticks);
        var first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A class at a freed address is another class, so the hop looks Data up again and doesn't use the offset of the old class.
    [Test]
    public void Read_ClassReplacedAtTheSameAddress_UsesTheOffsetOfTheNewClass()
    {
        var data = Data;
        var before = data.Read<Address>();

        _fakeGame.ReplaceFixtureGameModeClass();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(data.Read<Address>().Unwrap(), Is.EqualTo(_fakeGame.OtherData));
        }
    }

    // The world points at itself, so 100 hops end at the world and a steady state read costs 1 slot read and 3 reads per hop.
    [Test]
    public void Read_HundredHopsThroughASelfPointer_ReadsThreePerHop()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.World);
        var path = Enumerable.Range(0, HopCount).Aggregate(Result.Ok<IMemberPath>(_game.GWorld.Path), (prefix, _) => prefix.Member("GameMode"));
        _ = path.Read<Address>().Unwrap();

        int reads = _fakeGame.Memory.Reads;
        var world = path.Read<Address>().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_fakeGame.World));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (3 * HopCount)));
            Assert.That(path.Unwrap().ToString(), Has.Length.EqualTo("GWorld".Length + (HopCount * ".GameMode".Length)));
        }
    }

    // Reading a path with a null game mode doesn't allocate after the 1st read.
    [Test]
    public void Read_NullObjectSteadyState_DoesNotAllocate()
    {
        var data = Data;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        _ = data.Read<Address>().UnwrapErr();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            _ = data.Read<Address>();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // Reading Data from a game mode of GameModeBase doesn't allocate after the 1st read.
    [Test]
    public void Read_MemberNotOnTheClassSteadyState_DoesNotAllocate()
    {
        var data = Data;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.BaseGameMode);
        _ = data.Read<Address>().UnwrapErr();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            _ = data.Read<Address>();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // 2 paths from 1 prefix share its hop, so a failed read of 1 doesn't break the other.
    [Test]
    public void Read_SiblingPathFailsFirst_OtherPathStillReads()
    {
        var gameMode = _game.GWorld.Path.Member("GameMode");
        var mana = gameMode.Member("Mana");
        var data = gameMode.Member("Data");

        var failure = mana.Read<Address>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                failure.UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Mana. Class FixtureGameMode doesn't have a property Mana."));
            Assert.That(data.Read<Address>().Unwrap(), Is.EqualTo(_fakeGame.Data));
        }
    }

    // A member name outside ASCII prints as is in the path and in the cause.
    [Test]
    public void Read_NonAsciiMember_FailsWithTheNameAsIs()
    {
        Assert.That(
            _game.GWorld.Path.Member("ゲーム ü").Read<Address>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.ゲーム ü. Class World doesn't have a property ゲーム ü."));
    }

    // A watcher of a member of another kind fails the same way on every tick, so the read doesn't allocate after the 1st.
    [Test]
    public void Read_WrongKindSteadyState_DoesNotAllocate()
    {
        var speed = _game.GWorld.Path.Member("GameMode").Member("Data").Member("Speed");
        _ = speed.Read<int>().UnwrapErr();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < 1000; count++)
        {
            _ = speed.Read<int>();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }
}
