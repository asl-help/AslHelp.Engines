using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System.Linq;
using static AslHelp.Engines.Unreal.Tests.FakeUdkGame;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkTests
{
    private const string MissingRoute = """[{ "forward": "D1 D2 D3 ????????", "offset": 3 }, { "read": "global" }]""";

    private FakeUdkGame _fakeGame = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeUdkGame();
    }

    // Attach reads the world of the class World from GWorld and the pointers and count of the object array at GObjObjects itself.
    [Test]
    public void Attach_UdkGame_ReadsTheClassNameOfGWorld()
    {
        var game = _fakeGame.Attach();

        var world = game.GWorld.Path.Read<Address>().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_fakeGame.World));
            Assert.That(game.ClassOf(world).Unwrap().Name, Is.EqualTo("World"));
            Assert.That(game.ObjectArray.ReadCount().Unwrap(), Is.EqualTo(ObjectCount));
            Assert.That(game.ObjectArray.Read(ObjectCount).Last().Unwrap(), Is.EqualTo(_fakeGame.Data));
        }
    }

    // A path reads IntValue over 2 object hops because it looks FixtureData up on FixtureGame, which is the real class of Game.
    [Test]
    public void Read_PathOverTwoObjectHops_ReadsTheInt()
    {
        var game = _fakeGame.Attach();

        var intValue = game.GWorld.Path.Member("Game").Member("FixtureData").Member("IntValue");

        Assert.That(intValue.Read<int>().Unwrap(), Is.EqualTo(IntValue));
    }

    // FindClass finds the class past the empty slot of the object array and returns the class ClassOf returns.
    [Test]
    public void FindClass_UdkGame_FindsTheClassByName()
    {
        var game = _fakeGame.Attach();

        Assert.That(game.FindClass("FixtureData").Unwrap(), Is.SameAs(game.ClassOf(_fakeGame.Data).Unwrap()));
    }

    // GoalScore is declared on GameInfo, so FindProperty on FixtureGame finds it through UStruct.SuperField.
    [Test]
    public void FindProperty_PropertyOfSuperClass_FindsItThroughSuperField()
    {
        var game = _fakeGame.Attach();

        var goalScore = game.ClassOf(_fakeGame.Game).Unwrap().FindProperty("GoalScore").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(goalScore.Offset, Is.EqualTo(GoalScoreOffset));
            Assert.That(goalScore.ElementSize, Is.EqualTo(sizeof(int)));
            Assert.That(game.Read<int>(_fakeGame.Game, goalScore).Unwrap(), Is.EqualTo(GoalScore));
        }
    }

    // The name number 1 sits 4 bytes after the name id in UObject.Name, so the data object reads as FixtureData_0.
    [Test]
    public void NameOf_NameWithNumber_AppendsTheNumberMinusOne()
    {
        var game = _fakeGame.Attach();

        Assert.That(game.ObjectReader.NameOf(_fakeGame.Data).Unwrap(), Is.EqualTo("FixtureData_0"));
    }

    // A name entry with the low index bit set reads as UTF-16, so the class with that name is found.
    [Test]
    public void FindClass_WideName_ReadsUtf16Text()
    {
        var game = _fakeGame.Attach();

        Assert.That(game.FindClass(WideClassName).Unwrap().Name, Is.EqualTo(WideClassName));
    }

    // A boxed watcher of IntValue still reads it on the 2nd tick.
    [Test]
    public void CreateWatcher_UdkGame_WatchesTheIntBoxed()
    {
        var game = _fakeGame.Attach();
        ManualTicks ticks = new();
        var watcher = game.GWorld.Path.Member("Game").Member("FixtureData").Member("IntValue").Unwrap().CreateWatcher(ticks);

        object? first = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(IntValue));
            Assert.That(watcher.Current, Is.EqualTo(IntValue));
        }
    }

    // The 3 bools share 1 4-byte word with 2 and 8 set, so each reads its own bit through the bit mask of its property.
    [Test]
    public void Read_Unreal3Bool_UsesFourByteBitMask()
    {
        var data = _fakeGame.Attach().GWorld.Path.Member("Game").Member("FixtureData");

        Assert.That(
            (
                data.Member("bFirst").Read<bool>().Unwrap(),
                data.Member("bSecond").Read<bool>().Unwrap(),
                data.Member("bThird").Read<bool>().Unwrap()),
            Is.EqualTo((true, false, true)));
    }

    // StringValue is a pointer and a count like an FString, the count with the terminator.
    [Test]
    public void Read_Unreal3String_ReadsTheText()
    {
        var data = _fakeGame.Attach().GWorld.Path.Member("Game").Member("FixtureData");

        Assert.That(data.Member("StringValue").Read<string>().Unwrap(), Is.EqualTo(StringText));
    }

    // NameValue is an id and a number in the layout of the document.
    [Test]
    public void Read_Unreal3Name_ReadsTheText()
    {
        var data = _fakeGame.Attach().GWorld.Path.Member("Game").Member("FixtureData");

        Assert.That(data.Member("NameValue").Read<string>().Unwrap(), Is.EqualTo(NameText));
    }

    // An unreal3 document without the bool mask or the name id fails with the paths of Unreal Engine 3 and not with those of 4 and 5.
    [Test]
    public void Read_Unreal3WithoutBoolAndNameOffsets_FailsWithUnreal3Paths()
    {
        string json = Remove(Remove(Document, "offsets", "UBoolProperty"), "offsets", "FName", "Index");
        var data = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, UnrealProfile.Parse(json).Unwrap()).Unwrap()
            .GWorld.Path.Member("Game").Member("FixtureData");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                data.Member("bFirst").Read<bool>().UnwrapErr().Message,
                Does.EndWith("Can't read BoolProperty: the profile doesn't have offsets.UBoolProperty.BitMask."));
            Assert.That(
                data.Member("NameValue").Read<string>().UnwrapErr().Message,
                Does.EndWith("Can't read NameProperty: the profile doesn't have offsets.FName.Index."));
        }
    }

    // Coord is a struct, so the typed and the boxed path go into it by its offset and read X and Y.
    [Test]
    public void Read_Unreal3StructMember_StepsByOffset()
    {
        var game = _fakeGame.Attach();
        var watcher = game.GWorld.Path.Member("Game").Member("FixtureData").Member("Coord").Member("Y").Unwrap().CreateWatcher(new ManualTicks());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                game.GWorld.Path.Member("Game").Member("FixtureData").Member("Coord").Member("X").Read<int>().Unwrap(),
                Is.EqualTo(CoordX));
            Assert.That(watcher.Current, Is.EqualTo(CoordY));
        }
    }

    // A GObjObjects signature that isn't in the module fails with GObjObjects and not with the global of another engine.
    [Test]
    public void Attach_GObjObjectsWithoutMatch_FailsWithGObjObjects()
    {
        string json = Set(Document, $$"""{ "2010-09": {{MissingRoute}} }""", "globals", "GObjObjects");

        Assert.That(
            UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, UnrealProfile.Parse(json).Unwrap()).UnwrapErr().Message,
            Does.StartWith($"Can't find GObjObjects in {ModuleName}. No route located the address. '2010-09': "));
    }

    // A global keyed by 2 versions attaches when only the route of the 2nd version matches.
    [Test]
    public void Attach_OnlySecondVersionMatches_Attaches()
    {
        string routes = $$"""{ "2010-08": {{MissingRoute}}, "2010-09": {{GObjObjectsRoute}} }""";
        string json = Set(Document, routes, "globals", "GObjObjects");

        var game = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, UnrealProfile.Parse(json).Unwrap()).Unwrap();

        Assert.That(game.ObjectArray.ReadCount().Unwrap(), Is.EqualTo(ObjectCount));
    }
}
