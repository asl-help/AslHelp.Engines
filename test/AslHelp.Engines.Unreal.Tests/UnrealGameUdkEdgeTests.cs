using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.FakeUdkGame;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkEdgeTests
{
    private const string MissingRoute = """[{ "forward": "D1 D2 D3 ????????", "offset": 3 }, { "read": "global" }]""";

    private FakeUdkGame _fakeGame = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeUdkGame();
    }

    // Attach locates the name table 1st, so it fails with GNames when no global matches.
    [Test]
    public void Attach_EveryGlobalWithoutMatch_FailsWithGNames()
    {
        FakeMemory memory = new() { Layout = MemoryLayout.X86 };
        memory.Add(_fakeGame.Module.Range.Start, new byte[0x1000]);

        Assert.That(
            UnrealGame.Attach(memory, _fakeGame.Module, UnrealProfile.Parse(Document).Unwrap()).UnwrapErr().Message,
            Does.StartWith($"Can't find GNames in {ModuleName}. "));
    }

    // A GWorld signature that isn't in the module fails attach with GWorld.
    [Test]
    public void Attach_GWorldWithoutMatch_FailsWithGWorld()
    {
        string json = Set(Document, $$"""{ "2010-09": {{MissingRoute}} }""", "globals", "GWorld");

        Assert.That(
            UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, UnrealProfile.Parse(json).Unwrap()).UnwrapErr().Message,
            Does.StartWith($"Can't find GWorld in {ModuleName}. "));
    }

    // A version without a route of its own doesn't fail, because attach tries the routes each global has.
    [Test]
    public void Attach_VersionWithoutRoutes_ReadsThroughTheOtherVersion()
    {
        string json = Set(Document, """["2010-09", "2011-01"]""", "versions");

        var game = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, UnrealProfile.Parse(json).Unwrap()).Unwrap();

        var intValue = game.GWorld.Path.Member("Game").Member("FixtureData").Member("IntValue");

        Assert.That(intValue.Read<int>().Unwrap(), Is.EqualTo(IntValue));
    }
}
