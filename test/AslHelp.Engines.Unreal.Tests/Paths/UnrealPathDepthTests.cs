using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests.Paths;

// Runs on 1 layout only, because each step checks the whole path and 1000 steps take seconds on fake memory.
public sealed class UnrealPathDepthTests
{
    private const int HopCount = 1000;

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "ffield");
        _game = _fakeGame.Attach();
    }

    // The world points at itself, so a watcher of a path with 1000 GameMode hops reads without overflowing the stack.
    [Test]
    public void CreateWatcher_ThousandHops_ReadsWithoutOverflow()
    {
        var gameModeSlot = _fakeGame.World + FakeGame.GameModeOffset;
        _fakeGame.Memory.WritePointer(gameModeSlot, _fakeGame.World);
        var path = Result.Ok<IMemberPath>(_game.GWorld.Path);

        for (int hop = 0; hop < HopCount; hop++)
        {
            path = path.Member("GameMode");
        }

        ManualTicks ticks = new();
        var watcher = path.Unwrap().CreateWatcher(ticks);
        object? world = watcher.Current;
        _fakeGame.Memory.WritePointer(gameModeSlot, Address.Zero);
        ticks.Value++;
        _ = watcher.Current;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_fakeGame.World));
            Assert.That(watcher.Error?.Message, Does.EndWith(": GWorld.GameMode is null."));
        }
    }
}
