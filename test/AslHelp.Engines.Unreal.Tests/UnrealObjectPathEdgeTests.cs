using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealObjectPathEdgeTests(
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

    private UnrealObjectPath Data => _game.GWorld.ObjectMember("GameMode").ObjectMember("Data");

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
            Data.Read().UnwrapErr().Message,
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
            Data.Read().UnwrapErr().Message,
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
            Data.Read().UnwrapErr().Message,
            Does.StartWith($"Can't read GWorld.GameMode.Data. Can't read the class of the object at {garbage}: "));
    }

    // A class at a freed address is another class, so the hop looks Data up again and doesn't use the offset of the old class.
    [Test]
    public void Read_ClassReplacedAtTheSameAddress_UsesTheOffsetOfTheNewClass()
    {
        var data = Data;
        var before = data.Read();

        _fakeGame.ReplaceFixtureGameModeClass();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(data.Read().Unwrap(), Is.EqualTo(_fakeGame.OtherData));
        }
    }

    // The world points at itself, so 100 hops end at the world and a steady state read costs 1 slot read and 3 reads per hop.
    [Test]
    public void Read_HundredHopsThroughASelfPointer_ReadsThreePerHop()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.World);
        var path = Enumerable.Range(0, HopCount).Aggregate(_game.GWorld, (prefix, _) => prefix.ObjectMember("GameMode"));
        _ = path.Read().Unwrap();

        int reads = _fakeGame.Memory.Reads;
        var world = path.Read().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_fakeGame.World));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (3 * HopCount)));
            Assert.That(path.ToString(), Has.Length.EqualTo("GWorld".Length + (HopCount * ".GameMode".Length)));
        }
    }

    // Reading a path with a null game mode doesn't allocate after the 1st read.
    [Test]
    public void Read_NullObjectSteadyState_DoesNotAllocate()
    {
        var data = Data;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        _ = data.Read().UnwrapErr();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            _ = data.Read();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // Reading Data from a game mode of GameModeBase doesn't allocate after the 1st read.
    [Test]
    public void Read_MemberNotOnTheClassSteadyState_DoesNotAllocate()
    {
        var data = Data;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.BaseGameMode);
        _ = data.Read().UnwrapErr();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            _ = data.Read();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    // 2 paths from 1 prefix share its hop, so a failed read of 1 doesn't break the other.
    [Test]
    public void Read_SiblingPathFailsFirst_OtherPathStillReads()
    {
        var gameMode = _game.GWorld.ObjectMember("GameMode");
        var mana = gameMode.ObjectMember("Mana");
        var data = gameMode.ObjectMember("Data");

        var failure = mana.Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                failure.UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Mana. Class FixtureGameMode doesn't have a property Mana."));
            Assert.That(data.Read().Unwrap(), Is.EqualTo(_fakeGame.Data));
        }
    }

    // A member name outside ASCII prints as is in the path and in the cause.
    [Test]
    public void Read_NonAsciiMember_FailsWithTheNameAsIs()
    {
        Assert.That(
            _game.GWorld.ObjectMember("ゲーム ü").Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.ゲーム ü. Class World doesn't have a property ゲーム ü."));
    }
}
