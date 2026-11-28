using AslHelp.Engines.Errors;
using AslHelp.Engines.Profiles;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using AslHelp.Memory.Remote;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Unreal.Tests;

// Starts each player once for all its tests, because a start takes up to a minute and every test only reads.
[TestFixtureSource(typeof(UnrealLivePlayer), nameof(UnrealLivePlayer.Fixtures))]
public sealed class UnrealGameLiveTests(
    UnrealLivePlayer? player)
{
    // Set FIXTURES_UNREAL_PROFILE to the profile document of the player of FIXTURES_UNREAL_PLAYER.
    private const string ProfileVariable = "FIXTURES_UNREAL_PROFILE";

    // Set FIXTURES_UNREAL_PROFILES to the folder of just measure --profiles, so each player attaches with the document that lists it.
    private const string ProfilesVariable = "FIXTURES_UNREAL_PROFILES";

    private static readonly ProfileVersion _firstGameModeBase = UnrealTarget.ParseVersion("4.14")!;
    private static readonly ProfileVersion _firstSet = UnrealTarget.ParseVersion("4.13")!;
    private static readonly ProfileVersion _firstSubsystem = UnrealTarget.ParseVersion("4.22")!;
    private static readonly Lazy<ProfileCatalog<UnrealProfile>> _profilesCatalog = new(ReadProfilesCatalog);

    private StartedPlayer? _started;

    private UnrealLivePlayer Player => RequirePlayer(player);

    [OneTimeSetUp]
    public void StartPlayer()
    {
        Assume.That(Environment.Is64BitProcess || !Player.Is64Bit, "Run these tests in a 64-bit process, because the player is x64.");

        _started = new StartedPlayer(Player.ExecutablePath);
    }

    [OneTimeTearDown]
    public void StopPlayer()
    {
        _started?.Dispose();
    }

    // The real player's GWorld points at an object of the class World once the fixture writes its ready file.
    [Test]
    public void Attach_RealPlayer_ReadsWorldAsTheClassOfGWorld()
    {
        WithAttachedPlayer(game =>
        {
            var world = game.GWorld.Path.Read<Address>().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(world, Is.Not.EqualTo(Address.Zero));
                Assert.That(game.ObjectReader.NameOf(game.ObjectReader.ClassOf(world).Unwrap()).Unwrap(), Is.EqualTo("World"));
                Assert.That(game.ObjectArray.ReadCount().Unwrap(), Is.GreaterThan(0));
            }
        });
    }

    // AuthorityGameMode is declared as AGameModeBase, so FixtureData only shows up on the real class of the game mode.
    [Test]
    public void Read_RealPlayer_ReadsIntValueAndHpThroughTheGameMode()
    {
        WithAttachedPlayer(game =>
        {
            var world = game.GWorld.Path.Read<Address>().Unwrap();
            var gameMode = game.Read<Address>(world, game.ClassOf(world).Unwrap().FindProperty("AuthorityGameMode").Unwrap()).Unwrap();
            var gameModeClass = game.ClassOf(gameMode).Unwrap();
            var data = game.Read<Address>(gameMode, gameModeClass.FindProperty("FixtureData").Unwrap()).Unwrap();
            var dataClass = game.ClassOf(data).Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gameModeClass.Name, Is.EqualTo("FixtureGameMode"));
                Assert.That(game.Read<int>(data, dataClass.FindProperty("IntValue").Unwrap()).Unwrap(), Is.EqualTo(1337));
                Assert.That(game.Read<int>(data, dataClass.FindProperty("Hp").Unwrap()).Unwrap(), Is.InRange(1, 1000));
                Assert.That(game.FindClass("FixtureData").Unwrap(), Is.SameAs(dataClass));
            }
        });
    }

    // Paths read Hp and IntValue of the real player and their watchers still read them on the 2nd tick.
    [Test]
    public void CreateWatcher_RealPlayer_WatchesHpAndIntValueThroughThePath()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            ManualTicks ticks = new();
            var hp = data.Member("Hp").Value<int>().Unwrap().CreateWatcher(ticks);
            var intValue = data.Member("IntValue").Value<int>().Unwrap().CreateWatcher(ticks);

            int firstHp = hp.Current;
            _ = intValue.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member("IntValue").Read<int>().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Member("Hp").Read<int>().Unwrap(), Is.InRange(1, 1000));
                Assert.That(firstHp, Is.InRange(1, 1000));
                Assert.That(hp.Current, Is.InRange(1, 1000));
                Assert.That(intValue.Current, Is.EqualTo(1337));
                Assert.That(intValue.Changed, Is.False);
            }
        });
    }

    // A direct read gives the value of a watcher, boxed or typed and with the name in any case.
    [Test]
    public void Read_RealPlayerIntValueInAnyCase_EqualsTheWatcherValue()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            var watcher = data.Member("IntValue").Value<int>().Unwrap().CreateWatcher(new ManualTicks());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member("IntValue").Read().Unwrap(), Is.EqualTo(watcher.Current));
                Assert.That(data.Member("intvalue").Read<int>().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Member("IntValue").Unwrap().IsChecked, Is.True);
            }
        });
    }

    // Neither FixtureData nor a derived class has Hpp, so the typo fails on the step as a shape mismatch.
    [Test]
    public void Member_RealPlayerTypo_FailsWithAShapeMismatch()
    {
        WithAttachedPlayer(game => Assert.That(
            game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData").Member("Hpp").UnwrapErr(),
            Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True));
    }

    // Typed paths read every value kind of FixtureData with the values BeginPlay sets, Coord through its struct offset.
    [Test]
    public void Read_RealPlayer_ReadsEveryFixtureValueThroughTypedPaths()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member("IntValue").Read<int>().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Member("LongValue").Read<long>().Unwrap(), Is.EqualTo(9_000_000_000));
                Assert.That(data.Member("FloatValue").Read<float>().Unwrap(), Is.EqualTo(3.5f));
                Assert.That(data.Member("DoubleValue").Read<double>().Unwrap(), Is.EqualTo(2.718281828));
                Assert.That(data.Member("BoolValue").Read<bool>().Unwrap(), Is.True);
                Assert.That(data.Member("bFirst").Read<bool>().Unwrap(), Is.True);
                Assert.That(data.Member("bSecond").Read<bool>().Unwrap(), Is.False);
                Assert.That(data.Member("bThird").Read<bool>().Unwrap(), Is.True);
                Assert.That(data.Member("Kind").Read<byte>().Unwrap(), Is.EqualTo(1));
                Assert.That(data.Member("OldKind").Read<byte>().Unwrap(), Is.EqualTo(1));
                Assert.That(data.Member("StringValue").Read<string>().Unwrap(), Is.EqualTo("hello-fixture"));
                Assert.That(data.Member("NameValue").Read<string>().Unwrap(), Is.EqualTo("fixture-name"));
                Assert.That(data.Member("Coord").Member("X").Read<int>().Unwrap(), Is.EqualTo(7));
                Assert.That(data.Member("Coord").Member("Y").Read<int>().Unwrap(), Is.EqualTo(9));
                Assert.That(data.Member("Hp").Read<int>().Unwrap(), Is.InRange(1, 1000));
            }
        });
    }

    // Boxed watchers give every value of FixtureData in the type of its kind, so a script gets an int, a float, a bool or a string.
    [Test]
    public void CreateWatcher_RealPlayer_WatchesEveryFixtureValueBoxed()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            ManualTicks ticks = new();
            Watcher<object?> watch(Result<IMemberPath> path)
            {
                return path.Unwrap().CreateWatcher(ticks);
            }

            var hp = watch(data.Member("Hp"));
            object? firstHp = hp.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(watch(data.Member("IntValue")).Current, Is.TypeOf<int>().And.EqualTo(1337));
                Assert.That(watch(data.Member("LongValue")).Current, Is.TypeOf<long>().And.EqualTo(9_000_000_000));
                Assert.That(watch(data.Member("FloatValue")).Current, Is.TypeOf<float>().And.EqualTo(3.5f));
                Assert.That(watch(data.Member("DoubleValue")).Current, Is.TypeOf<double>().And.EqualTo(2.718281828));
                Assert.That(watch(data.Member("BoolValue")).Current, Is.TypeOf<bool>().And.True);
                Assert.That(watch(data.Member("bFirst")).Current, Is.True);
                Assert.That(watch(data.Member("bSecond")).Current, Is.False);
                Assert.That(watch(data.Member("bThird")).Current, Is.True);
                Assert.That(watch(data.Member("Kind")).Current, Is.TypeOf<byte>().And.EqualTo(1));
                Assert.That(watch(data.Member("OldKind")).Current, Is.TypeOf<byte>().And.EqualTo(1));
                Assert.That(watch(data.Member("StringValue")).Current, Is.EqualTo("hello-fixture"));
                Assert.That(watch(data.Member("NameValue")).Current, Is.EqualTo("fixture-name"));
                Assert.That(watch(data.Member("Coord").Member("X")).Current, Is.TypeOf<int>().And.EqualTo(7));
                Assert.That(watch(data.Member("Coord").Member("Y")).Current, Is.EqualTo(9));
                Assert.That(firstHp, Is.TypeOf<int>().And.InRange(1, 1000));
                Assert.That(hp.Current, Is.InRange(1, 1000));
            }
        });
    }

    // On 2 ticks, 30 watchers of 1 counter that share the steps up to FixtureData give what a watcher of its own counter reads.
    [Test]
    public void CreateWatcher_RealPlayer_WatchersWithSharedStepsGiveTheValuesOfSingleReads()
    {
        WithAttachedPlayer(game =>
        {
            string[] names = ["IntValue", "LongValue", "FloatValue", "DoubleValue", "BoolValue", "Kind", "StringValue", "NameValue"];
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            ManualTicks ticks = new();
            var watchers = Enumerable.Range(0, 30).Select(index => data.Member(names[index % names.Length]).Unwrap().CreateWatcher(ticks)).ToArray();

            object?[] first = [.. watchers.Select(static watcher => watcher.Current)];
            ticks.Value++;
            object?[] single = [.. Enumerable.Range(0, 30).Select(index =>
                data.Member(names[index % names.Length]).Unwrap().CreateWatcher(new ManualTicks()).Current)];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.EqualTo(single));
                Assert.That(watchers.Select(static watcher => watcher.Current), Is.EqualTo(single));
                Assert.That(first[0], Is.EqualTo(1337));
            }
        });
    }

    // The weak pointer resolves to Items[1] on every version, because 4.0 to 4.10 keep the serial numbers in blocks apart from the object array.
    [Test]
    public void Read_RealPlayer_ReadsTheWeakPointer()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            var shield = data.Member("Items").Item(1).Read<Address>().Unwrap();

            Assert.That(data.Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(shield));
        });
    }

    // Typed paths read the arrays, the maps, the set and the weak pointer of FixtureData with the values BeginPlay sets.
    [Test]
    public void Read_RealPlayer_ReadsCollectionsThroughTypedPaths()
    {
        Assume.That(Player.Version, Is.GreaterThanOrEqualTo(_firstSet), "The fixture has TMap from 4.8 on and TSet from 4.13 on.");

        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            int[] intArray = [100, 200, 300];
            string[] stringArray = ["a", "b", "c"];
            int[][] coordArray = [[1, 2], [3, 4]];
            HashSet<int> intSet = [11, 22, 33];
            Dictionary<string, int> dict = new() { ["one"] = 1, ["two"] = 2, ["three"] = 3 };
            var shield = data.Member("Items").Item(1).Read<Address>().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member("IntArray").Read<int[]>().Unwrap(), Is.EqualTo(intArray));
                Assert.That(data.Member("StringArray").Read<string[]>().Unwrap(), Is.EqualTo(stringArray));
                Assert.That(data.Member("CoordArray").Read<int[][]>().Unwrap(), Is.EqualTo(coordArray));
                Assert.That(data.Member("CoordArray").Item(1).Member("X").Read<int>().Unwrap(), Is.EqualTo(3));
                Assert.That(data.Member("Items").Item(1).Member("Count").Read<int>().Unwrap(), Is.EqualTo(3));
                Assert.That(data.Member("Items").Item(1).Count().IsErr, Is.True);
                Assert.That(data.Member("Items").Count().Read<int>().Unwrap(), Is.EqualTo(2));
                Assert.That(data.Member("Dict").Read<Dictionary<string, int>>().Unwrap(), Is.EquivalentTo(dict));
                Assert.That(data.Member("Dict").Item("two").Read<int>().Unwrap(), Is.EqualTo(2));
                Assert.That(data.Member("IntDict").Item(5).Read<int>().Unwrap(), Is.EqualTo(50));
                Assert.That(data.Member("IntSet").Read<HashSet<int>>().Unwrap(), Is.EquivalentTo(intSet));
                Assert.That(data.Member("IntSet").Item(22).Read<bool>().Unwrap(), Is.True);
                Assert.That(data.Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(shield));
                Assert.That(data.Member("WeakItem").Member("ItemName").Read<string>().Unwrap(), Is.EqualTo("shield"));
            }
        });
    }

    // Boxed watchers read the same collections with index and key steps and a 2nd tick gives the same instances.
    [Test]
    public void CreateWatcher_RealPlayer_WatchesCollectionsBoxed()
    {
        Assume.That(Player.Version, Is.GreaterThanOrEqualTo(_firstSet), "The fixture has TMap from 4.8 on and TSet from 4.13 on.");

        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            double[] firstVector = [1.25, -2.5, 3.75];
            double[] secondVector = [4.5, 5.25, -6.75];
            ManualTicks ticks = new();
            Watcher<object?> watch(Result<IMemberPath> path)
            {
                return path.Unwrap().CreateWatcher(ticks);
            }

            var intArray = watch(data.Member("IntArray"));
            var intSet = watch(data.Member("IntSet"));
            object? firstArray = intArray.Current;
            object? firstSet = intSet.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(intArray.Current, Is.SameAs(firstArray));
                Assert.That(intSet.Current, Is.SameAs(firstSet));
                Assert.That(watch(data.Member("Items").Item(1).Member("ItemName")).Current, Is.EqualTo("shield"));
                Assert.That(watch(data.Member("Dict").Item("two")).Current, Is.EqualTo(2));
                Assert.That(watch(data.Member("IntDict").Item(6)).Current, Is.EqualTo(60));
                Assert.That(watch(data.Member("IntSet").Item(33)).Current, Is.True);
                Assert.That(watch(data.Member("Items").Count()).Current, Is.EqualTo(2));
                Assert.That(watch(data.Member("WeakItem").Member("Count")).Current, Is.EqualTo(3));
                Assert.That(Numbers(watch(data.Member("VecArray").Item(0)).Current), Is.EqualTo(firstVector));
                Assert.That(Numbers(watch(data.Member("VecArray").Item(1)).Current), Is.EqualTo(secondVector));
            }
        });
    }

    // Position and the items of VecArray step into X, Y and Z by name and read as struct values with member names.
    [Test]
    public void Read_RealPlayer_StepsIntoVectorByName()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Path.Member("AuthorityGameMode").Member("FixtureData");
            ManualTicks ticks = new();
            var position = data.Member("Position").Value<StructValue>().Unwrap().CreateWatcher(ticks).Current;

            var firstVector = data.Member("VecArray").Item(0).Unwrap().CreateWatcher(ticks).Current as StructValue;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Number(data.Member("Position").Member("X")), Is.EqualTo(1));
                Assert.That(Number(data.Member("Position").Member("Y")), Is.EqualTo(2));
                Assert.That(Number(data.Member("Position").Member("Z")), Is.EqualTo(3));

                Assert.That(Number(data.Member("VecArray").Item(0).Member("Y")), Is.EqualTo(-2.5));
                Assert.That(position?.Names, Is.EqualTo(["X", "Y", "Z"]));
                Assert.That(Numbers(position?.Values), Is.EqualTo([1d, 2d, 3d]));

                Assert.That(Numbers(firstVector?.Values), Is.EqualTo([1.25, -2.5, 3.75]));
            }
        });
    }

    // The fixture package lists the fixture classes and FixtureData lists its own properties with their kinds.
    [Test]
    public void FindPackage_RealPlayer_ListsTheFixtureClasses()
    {
        WithAttachedPlayer(game =>
        {
            var classes = game.FindPackage("/Script/Fixture").Unwrap().GetClasses().Unwrap().Select(static klass => klass.Name);
            var properties = game.FindClass("FixtureData").Unwrap().GetProperties().Unwrap();
            var kinds = properties.Select(static property => (property.Name, property.ClassName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(classes, Does.Contain("FixtureData").And.Contain("FixtureItem").And.Contain("FixtureGameMode"));
                Assert.That(kinds, Does.Contain(("IntValue", "IntProperty")).And.Contain(("Hp", "IntProperty")));
            }
        });
    }

    // FixtureData creates a sword and a shield, so FixtureItem has 2 live objects and FixtureData 1 whose path reads IntValue.
    // The items come in slot order, which differs between runs because the object array reuses free slots.
    [Test]
    public void GetInstances_RealPlayer_ReturnsBothFixtureItems()
    {
        WithAttachedPlayer(game =>
        {
            var items = game.FindClass("FixtureItem").Unwrap().GetInstances().Unwrap();
            var data = game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    items.Select(static item => item.Path.Member("ItemName").Read<string>().Unwrap()),
                    Is.EquivalentTo(["sword", "shield"]));
                Assert.That(data.Single().Path.Member("IntValue").Read<int>().Unwrap(), Is.EqualTo(1337));
            }
        });
    }

    // FixtureGameMode derives from GameModeBase over GameMode and the engine object is a GameEngine.
    [Test]
    public void GetDerivedClasses_RealPlayer_ListsFixtureGameModeBelowGameModeBase()
    {
        Assume.That(Player.Version, Is.GreaterThanOrEqualTo(_firstGameModeBase), "The fixture game mode derives from GameModeBase from 4.14 on.");

        WithAttachedPlayer(game =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    game.FindClass("GameModeBase").Unwrap().GetDerivedClasses().Unwrap().Select(static klass => klass.Name),
                    Does.Contain("FixtureGameMode").And.Contain("GameMode"));
                Assert.That(
                    game.FindClass("Engine").Unwrap().GetDerivedClasses().Unwrap().Select(static klass => klass.Name),
                    Does.Contain("GameEngine"));
            }
        });
    }

    // The GEngine route of the document reaches the engine of the player and its viewport has the world of the game.
    [Test]
    public void GEngine_RealPlayer_ReachesIntValueThroughTheViewportWorld()
    {
        WithAttachedPlayer(game =>
        {
            var world = game.GEngine.Path.Member("GameViewport").Member("World");
            var intValue = world.Member("AuthorityGameMode").Member("FixtureData").Member("IntValue");

            Assert.That(intValue.Read<int>().Unwrap(), Is.EqualTo(1337));
        });
    }

    // A document before 4.22 doesn't have the subsystem collection, so the lookup fails with the version.
    [Test]
    public void FindSubsystem_RealPlayerBefore422_FailsWithTheVersion()
    {
        Assume.That(Player.Version, Is.LessThan(_firstSubsystem), "Players from 4.22 on have subsystems.");

        WithAttachedPlayer(game => Assert.That(
            game.FindSubsystem("FixtureSubsystem").UnwrapErr().Message,
            Is.EqualTo("Subsystems need 4.22 or later, UGameInstance.SubsystemCollection and FSubsystemCollectionBase.SubsystemMap in the profile.")));
    }

    // From 4.22 on the fixture subsystem sets SubsystemValue when the game instance creates it.
    [Test]
    public void FindSubsystem_RealPlayerFrom422_ReadsSubsystemValue()
    {
        Assume.That(Player.Version, Is.GreaterThanOrEqualTo(_firstSubsystem), "The fixture has a subsystem from 4.22 on.");

        WithAttachedPlayer(game => Assert.That(
            game.FindSubsystem("FixtureSubsystem").Unwrap().Path.Member("SubsystemValue").Read<int>().Unwrap(),
            Is.EqualTo(4242)));
    }

    // A shipping player from 4.8 on stamps its version and detection takes a name without a configuration as shipping, so detection and the shipped catalog pick a document that lists it.
    [Test]
    public void Attach_RealShippingPlayerWithoutAProfile_DetectsTheVersionAndPicksADocumentThatListsIt()
    {
        var live = Player;

        Assume.That(live.Configuration, Is.EqualTo(UnrealConfiguration.Shipping), "The name of a development player doesn't say development.");
        Assume.That(live.HasVersionStamp, Is.True, "Players before 4.8 don't stamp their version.");

        WithPlayer((memory, module) =>
        {
            var game = UnrealGame.Attach(memory, module, ProfileGuessHandling.Disallow).Unwrap();
            var world = game.GWorld.Path.Read<Address>().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(game.Selection!.Target, Is.EqualTo(live.Target));
                Assert.That(game.Selection.Entry.Versions, Does.Contain(live.Version));
                Assert.That(game.Selection.Match, Is.EqualTo(ProfileMatch.Listed));
                Assert.That(game.ClassOf(world).Unwrap().Name, Is.EqualTo("World"));
            }
        });
    }

    // A player before 4.8 doesn't stamp its version, so detection without a passed version fails.
    [Test]
    public void Detect_RealPlayerBefore48_FailsWithoutAStamp()
    {
        var live = Player;

        Assume.That(live.HasVersionStamp, Is.False, "Players from 4.8 on stamp their version.");

        WithPlayer((memory, module) => Assert.That(
            UnrealTarget.Detect(memory, module).UnwrapErr().Message,
            Does.Contain("doesn't have an engine version stamp")));
    }

    // Detection with the configuration of the player and, before 4.8, its version finds its key,
    // so the shipped catalog picks a document of that key that lists it.
    [Test]
    public void Attach_RealPlayerWithItsConfiguration_PicksADocumentOfItsKeyThatListsIt()
    {
        var live = Player;

        WithPlayer((memory, module) =>
        {
            var target = UnrealTarget.Detect(memory, module, live.VersionWithoutStamp, live.Configuration).Unwrap();
            var selection = UnrealProfile.CreateCatalog().Select(target, ProfileGuessHandling.Disallow).Unwrap();
            var game = UnrealGame.Attach(memory, module, selection).Unwrap();
            var world = game.GWorld.Path.Read<Address>().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(target, Is.EqualTo(live.Target));
                Assert.That(selection.Entry.Key, Is.EqualTo(live.Key));
                Assert.That(selection.Entry.Versions, Does.Contain(live.Version));
                Assert.That(game.ClassOf(world).Unwrap().Name, Is.EqualTo("World"));
            }
        });
    }

    // Read a float or double member as a double, so a vector compares the same on both sides of 5.0.
    private static double Number(Result<IMemberPath> path)
    {
        return Convert.ToDouble(path.Unwrap().CreateWatcher(new ManualTicks()).Current, CultureInfo.InvariantCulture);
    }

    // Turn the members of a struct value or a float or double array into doubles, so a vector compares the same on both sides of 5.0.
    private static double[] Numbers(object? value)
    {
        return value switch
        {
            StructValue members => Numbers(members.Values),
            IEnumerable<object?> members => [.. members.Select(static number => Convert.ToDouble(number, CultureInfo.InvariantCulture))],
            float[] floats => [.. floats.Select(static number => (double)number)],
            double[] doubles => doubles,
            _ => [],
        };
    }

    // Fail when FIXTURES_UNREAL_PLAYERS doesn't have a player, because that variable asks for a run on every player of its folder.
    // Skip the test when neither variable is set. Throw what Assume throws, because CA1062 only sees a null check.
    private static UnrealLivePlayer RequirePlayer(UnrealLivePlayer? player)
    {
        if (player is not null)
        {
            return player;
        }

        string? players = Environment.GetEnvironmentVariable(UnrealLivePlayer.PlayersVariable);

        if (players is not null)
        {
            throw new AssertionException(Directory.Exists(players)
                ? $"{UnrealLivePlayer.PlayersVariable} is {players}, which doesn't have a player folder."
                : $"{UnrealLivePlayer.PlayersVariable} is {players}, which doesn't exist.");
        }

        throw new InconclusiveException(
            $"Set {UnrealLivePlayer.PlayersVariable} to a players folder or {UnrealLivePlayer.PlayerVariable} to the 4.19.2 x64 shipping player to run this test.");
    }

    // Pick the document that lists the player from FIXTURES_UNREAL_PROFILES, else parse FIXTURES_UNREAL_PROFILE.
    private static UnrealProfile ProfileOf(UnrealLivePlayer player)
    {
        string? profiles = Environment.GetEnvironmentVariable(ProfilesVariable);

        if (profiles is null)
        {
            string? profilePath = Environment.GetEnvironmentVariable(ProfileVariable);

            Assume.That(
                profilePath is not null && File.Exists(profilePath),
                $"Set {ProfilesVariable} to a profiles folder or {ProfileVariable} to the profile document of that player to run this test.");

            return UnrealProfile.Parse(File.ReadAllText(profilePath!)).Unwrap();
        }

        Assume.That(Directory.Exists(profiles), $"Set {ProfilesVariable} to a profiles folder to run this test.");

        bool listed = _profilesCatalog.Value.Select(player.Target, ProfileGuessHandling.Disallow).TryUnwrap(out var selection, out _);

        Assume.That(listed, $"{ProfilesVariable} doesn't have a document that lists {player.Id}.");

        return selection!.Entry.Profile;
    }

    // Read the documents of FIXTURES_UNREAL_PROFILES once, because every test of every player picks from the same folder.
    private static ProfileCatalog<UnrealProfile> ReadProfilesCatalog()
    {
        ProfileCatalog<UnrealProfile> catalog = new(UnrealProfile.Map, UnrealTarget.ParseVersion);

        foreach (string path in Directory.EnumerateFiles(Environment.GetEnvironmentVariable(ProfilesVariable)!, "*.json"))
        {
            _ = catalog.AddFile(path).Unwrap();
        }

        return catalog;
    }

    // Attach to the started player with the profile of the player.
    private void WithAttachedPlayer(Action<UnrealGame> test)
    {
        var profile = ProfileOf(Player);

        WithPlayer((memory, module) => test(UnrealGame.Attach(memory, module, profile).Unwrap()));
    }

    private void WithPlayer(Action<IMemoryReader, MemoryModule> test)
    {
        Assert.That(_started, Is.Not.Null, "The player didn't start.");
        _started!.Run(test);
    }
}
