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

public sealed class UnrealGameLiveTests
{
    private const string ExecutableName = "Fixture-Win64-Shipping.exe";
    private const int MaxStarts = 3;

    // Set FIXTURES_UNREAL_PLAYER to the folder of the 4.19.2 x64 shipping player to attach to it.
    private const string PlayerVariable = "FIXTURES_UNREAL_PLAYER";

    // Set FIXTURES_UNREAL_PROFILE to the profile document of that player.
    private const string ProfileVariable = "FIXTURES_UNREAL_PROFILE";

    private const string ReadyFileName = "FixtureReady.txt";

    private static readonly TimeSpan _readyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _readyTimeout = TimeSpan.FromMinutes(2);

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

    // Typed paths read the arrays, the maps, the set and the weak pointer of FixtureData with the values BeginPlay sets.
    [Test]
    public void Read_RealPlayer_ReadsCollectionsThroughTypedPaths()
    {
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
                    Is.EqualTo(["sword", "shield"]));
                Assert.That(data.Single().Path.Member("IntValue").Read<int>().Unwrap(), Is.EqualTo(1337));
            }
        });
    }

    // FixtureGameMode derives from GameModeBase over GameMode and the engine object is a GameEngine.
    [Test]
    public void GetDerivedClasses_RealPlayer_ListsFixtureGameModeBelowGameModeBase()
    {
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

    // The GEngine route of the 4.18.3 document reaches the engine of the 4.19.2 player and its viewport has the world of the game.
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

    // The 4.18.3 document doesn't have the subsystem collection, so the lookup fails with the version.
    [Test]
    public void FindSubsystem_RealPlayer_FailsBefore422()
    {
        WithAttachedPlayer(game => Assert.That(
            game.FindSubsystem("FixtureSubsystem").UnwrapErr().Message,
            Is.EqualTo("Subsystems need 4.22 or later and UGameInstance.SubsystemCollection in the profile.")));
    }

    // The 4.19.2 player stamps its version, so detection and the shipped catalog pick the document of 4.18.3, which lists 4.19.2.
    [Test]
    public void Attach_RealPlayerWithoutAProfile_DetectsTheVersionAndPicksTheDocument()
    {
        WithPlayer((memory, module) =>
        {
            var game = UnrealGame.Attach(memory, module, ProfileGuessHandling.Disallow).Unwrap();
            var world = game.GWorld.Path.Read<Address>().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    game.Selection!.Target,
                    Is.EqualTo(new ProfileTarget(UnrealTarget.Unreal4, UnrealTarget.ParseVersion("4.19.2")!, "win-x64-shipping")));
                Assert.That(game.Selection.Entry.Source, Is.EqualTo("Profiles/unreal4-4.18.3-win-x64-shipping.json"));
                Assert.That(game.Selection.Match, Is.EqualTo(ProfileMatch.Listed));
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

    // Parse the profile of FIXTURES_UNREAL_PROFILE and attach with it to the started player.
    private static void WithAttachedPlayer(Action<UnrealGame> test)
    {
        string? profilePath = Environment.GetEnvironmentVariable(ProfileVariable);

        Assume.That(
            profilePath is not null && File.Exists(profilePath),
            $"Set {ProfileVariable} to the profile document of that player to run this test.");

        var profile = UnrealProfile.Parse(File.ReadAllText(profilePath!)).Unwrap();

        WithPlayer((memory, module) => test(UnrealGame.Attach(memory, module, profile).Unwrap()));
    }

    // Start the player, wait for its ready file, run the test, then kill the player.
    // Start again up to 3 times, because another test run can use the same player.
    private static void WithPlayer(Action<IMemoryReader, MemoryModule> test)
    {
        string? player = Environment.GetEnvironmentVariable(PlayerVariable);

        Assume.That(player is not null && Directory.Exists(player), $"Set {PlayerVariable} to the 4.19.2 x64 shipping player to run this test.");
        Assume.That(Environment.Is64BitProcess, "Run this test in a 64-bit process, because the player is x64.");

        string directory = Path.Combine(player!, "Fixture", "Binaries", "Win64");
        string readyPath = Path.Combine(directory, ReadyFileName);
        ProcessStartInfo startInfo = new(Path.Combine(directory, ExecutableName), "-nullrhi -nosound") { UseShellExecute = false };

        for (int attempt = 1; ; attempt++)
        {
            var started = DateTime.UtcNow;
            using var process = Process.Start(startInfo)!;

            try
            {
                if (!WaitForReadyFile(process, readyPath, started))
                {
                    string message = process.HasExited
                        ? $"The player exited before it wrote {ReadyFileName}, {MaxStarts} starts."
                        : $"The player didn't write {ReadyFileName} within {_readyTimeout}, {MaxStarts} starts.";

                    Assert.That(attempt, Is.LessThan(MaxStarts), message);
                    continue;
                }

                using RemoteProcessMemory memory = new(process);
                test(memory, memory.GetModule(ExecutableName).Unwrap());
                return;
            }
            finally
            {
                // Skip the kill of a player that already exited, because Kill throws for it on .NET Framework.
                if (!process.HasExited)
                {
                    process.Kill();
                }

                process.WaitForExit();
            }
        }
    }

    // Wait for a ready file written after the start, because the file of an earlier run stays next to the executable.
    // Return false when the player exits before it or doesn't write it in time.
    private static bool WaitForReadyFile(Process process, string readyPath, DateTime started)
    {
        var clock = Stopwatch.StartNew();

        while (!File.Exists(readyPath) || File.GetLastWriteTimeUtc(readyPath) < started)
        {
            if (process.HasExited || clock.Elapsed > _readyTimeout)
            {
                return false;
            }

            Thread.Sleep(_readyPollInterval);
        }

        return true;
    }
}
