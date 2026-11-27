using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using AslHelp.Memory.Remote;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameLiveTests
{
    private const string ExecutableName = "Fixture-Win64-Shipping.exe";

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
            var world = game.GWorld.Read().Unwrap();

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
            var world = game.GWorld.Read().Unwrap();
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
            var data = game.GWorld.Member("AuthorityGameMode").Member("FixtureData");
            ManualTicks ticks = new();
            var hp = data.Member<int>("Hp").CreateWatcher(ticks);
            var intValue = data.Member<int>("IntValue").CreateWatcher(ticks);

            int firstHp = hp.Current;
            _ = intValue.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member<int>("IntValue").Read().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Member<int>("Hp").Read().Unwrap(), Is.InRange(1, 1000));
                Assert.That(firstHp, Is.InRange(1, 1000));
                Assert.That(hp.Current, Is.InRange(1, 1000));
                Assert.That(intValue.Current, Is.EqualTo(1337));
                Assert.That(intValue.Changed, Is.False);
            }
        });
    }

    // Typed paths read every value kind of FixtureData with the values BeginPlay sets, Coord through its struct offset.
    [Test]
    public void Read_RealPlayer_ReadsEveryFixtureValueThroughTypedPaths()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Member("AuthorityGameMode").Member("FixtureData");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member<int>("IntValue").Read().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Member<long>("LongValue").Read().Unwrap(), Is.EqualTo(9_000_000_000));
                Assert.That(data.Member<float>("FloatValue").Read().Unwrap(), Is.EqualTo(3.5f));
                Assert.That(data.Member<double>("DoubleValue").Read().Unwrap(), Is.EqualTo(2.718281828));
                Assert.That(data.Member<bool>("BoolValue").Read().Unwrap(), Is.True);
                Assert.That(data.Member<bool>("bFirst").Read().Unwrap(), Is.True);
                Assert.That(data.Member<bool>("bSecond").Read().Unwrap(), Is.False);
                Assert.That(data.Member<bool>("bThird").Read().Unwrap(), Is.True);
                Assert.That(data.Member<byte>("Kind").Read().Unwrap(), Is.EqualTo(1));
                Assert.That(data.Member<byte>("OldKind").Read().Unwrap(), Is.EqualTo(1));
                Assert.That(data.Member<string>("StringValue").Read().Unwrap(), Is.EqualTo("hello-fixture"));
                Assert.That(data.Member<string>("NameValue").Read().Unwrap(), Is.EqualTo("fixture-name"));
                Assert.That(data.Member("Coord").Member<int>("X").Read().Unwrap(), Is.EqualTo(7));
                Assert.That(data.Member("Coord").Member<int>("Y").Read().Unwrap(), Is.EqualTo(9));
                Assert.That(data.Member<int>("Hp").Read().Unwrap(), Is.InRange(1, 1000));
            }
        });
    }

    // Chain watchers give every value of FixtureData in the type of its kind, so a script gets an int, a float, a bool or a string.
    [Test]
    public void GWorldChain_RealPlayer_WatchesEveryFixtureValue()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorldChain.AuthorityGameMode.FixtureData;
            ManualTicks ticks = new();
            Watcher<object?> watch(IWatcherSource chain)
            {
                return (Watcher<object?>)chain.CreateWatcher(ticks);
            }

            var hp = watch(data.Hp);
            object? firstHp = hp.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(watch(data.IntValue).Current, Is.TypeOf<int>().And.EqualTo(1337));
                Assert.That(watch(data.LongValue).Current, Is.TypeOf<long>().And.EqualTo(9_000_000_000));
                Assert.That(watch(data.FloatValue).Current, Is.TypeOf<float>().And.EqualTo(3.5f));
                Assert.That(watch(data.DoubleValue).Current, Is.TypeOf<double>().And.EqualTo(2.718281828));
                Assert.That(watch(data.BoolValue).Current, Is.TypeOf<bool>().And.True);
                Assert.That(watch(data.bFirst).Current, Is.True);
                Assert.That(watch(data.bSecond).Current, Is.False);
                Assert.That(watch(data.bThird).Current, Is.True);
                Assert.That(watch(data.Kind).Current, Is.TypeOf<byte>().And.EqualTo(1));
                Assert.That(watch(data.OldKind).Current, Is.TypeOf<byte>().And.EqualTo(1));
                Assert.That(watch(data.StringValue).Current, Is.EqualTo("hello-fixture"));
                Assert.That(watch(data.NameValue).Current, Is.EqualTo("fixture-name"));
                Assert.That(watch(data.Coord.X).Current, Is.TypeOf<int>().And.EqualTo(7));
                Assert.That(watch(data.Coord.Y).Current, Is.EqualTo(9));
                Assert.That(firstHp, Is.TypeOf<int>().And.InRange(1, 1000));
                Assert.That(hp.Current, Is.InRange(1, 1000));
            }
        });
    }

    // Typed paths read the arrays, the maps, the set and the weak pointer of FixtureData with the values BeginPlay sets.
    [Test]
    public void Read_RealPlayer_ReadsCollectionsThroughTypedPaths()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorld.Member("AuthorityGameMode").Member("FixtureData");
            int[] intArray = [100, 200, 300];
            string[] stringArray = ["a", "b", "c"];
            int[][] coordArray = [[1, 2], [3, 4]];
            HashSet<int> intSet = [11, 22, 33];
            Dictionary<string, int> dict = new() { ["one"] = 1, ["two"] = 2, ["three"] = 3 };
            var shield = data.Member("Items").Element<Address>(1).Read().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member<int[]>("IntArray").Read().Unwrap(), Is.EqualTo(intArray));
                Assert.That(data.Member<string[]>("StringArray").Read().Unwrap(), Is.EqualTo(stringArray));
                Assert.That(data.Member<int[][]>("CoordArray").Read().Unwrap(), Is.EqualTo(coordArray));
                Assert.That(data.Member("CoordArray")[1].Member<int>("X").Read().Unwrap(), Is.EqualTo(3));
                Assert.That(data.Member("Items")[1].Member<int>("Count").Read().Unwrap(), Is.EqualTo(3));
                Assert.That(data.Member("Items").Member<int>("Count").Read().Unwrap(), Is.EqualTo(2));
                Assert.That(data.Member<Dictionary<string, int>>("Dict").Read().Unwrap(), Is.EquivalentTo(dict));
                Assert.That(data.Member("Dict").Element<int>("two").Read().Unwrap(), Is.EqualTo(2));
                Assert.That(data.Member("IntDict").Element<int>(5).Read().Unwrap(), Is.EqualTo(50));
                Assert.That(data.Member<HashSet<int>>("IntSet").Read().Unwrap(), Is.EquivalentTo(intSet));
                Assert.That(data.Member("IntSet").Element<bool>(22).Read().Unwrap(), Is.True);
                Assert.That(data.Member<Address>("WeakItem").Read().Unwrap(), Is.EqualTo(shield));
                Assert.That(data.Member("WeakItem").Member<string>("ItemName").Read().Unwrap(), Is.EqualTo("shield"));
            }
        });
    }

    // Chain watchers read the same collections with index and key hops and a 2nd tick gives the same instances.
    [Test]
    public void GWorldChain_RealPlayer_WatchesCollections()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.GWorldChain.AuthorityGameMode.FixtureData;
            double[] firstVector = [1.25, -2.5, 3.75];
            double[] secondVector = [4.5, 5.25, -6.75];
            ManualTicks ticks = new();
            Watcher<object?> watch(IWatcherSource chain)
            {
                return (Watcher<object?>)chain.CreateWatcher(ticks);
            }

            var intArray = watch(data.IntArray);
            var intSet = watch(data.IntSet);
            object? firstArray = intArray.Current;
            object? firstSet = intSet.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(intArray.Current, Is.SameAs(firstArray));
                Assert.That(intSet.Current, Is.SameAs(firstSet));
                Assert.That(watch(data.Items[1].ItemName).Current, Is.EqualTo("shield"));
                Assert.That(watch(data.Dict["two"]).Current, Is.EqualTo(2));
                Assert.That(watch(data.IntDict[6]).Current, Is.EqualTo(60));
                Assert.That(watch(data.IntSet[33]).Current, Is.True);
                Assert.That(watch(data.Items.Count).Current, Is.EqualTo(2));
                Assert.That(watch(data.WeakItem.Count).Current, Is.EqualTo(3));
                Assert.That(Numbers(watch(data.VecArray[0]).Current), Is.EqualTo(firstVector));
                Assert.That(Numbers(watch(data.VecArray[1]).Current), Is.EqualTo(secondVector));
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
                Assert.That(items.Select(static item => item.Path.Member<string>("ItemName").Read().Unwrap()), Is.EqualTo(["sword", "shield"]));
                Assert.That(data.Single().Path.Member<int>("IntValue").Read().Unwrap(), Is.EqualTo(1337));
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
            var world = game.GEngine.Member("GameViewport").Member("World");
            var intValue = world.Member("AuthorityGameMode").Member("FixtureData").Member<int>("IntValue");

            Assert.That(intValue.Read().Unwrap(), Is.EqualTo(1337));
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

    // Turn a float or double array into doubles, so a vector compares the same on both sides of 5.0.
    private static double[] Numbers(object? value)
    {
        return value switch
        {
            float[] floats => [.. floats.Select(static number => (double)number)],
            double[] doubles => doubles,
            _ => [],
        };
    }

    // Start the player, wait for its ready file, attach and run the test, then kill the player.
    private static void WithAttachedPlayer(Action<UnrealGame> test)
    {
        string? player = Environment.GetEnvironmentVariable(PlayerVariable);
        string? profilePath = Environment.GetEnvironmentVariable(ProfileVariable);

        Assume.That(player is not null && Directory.Exists(player), $"Set {PlayerVariable} to the 4.19.2 x64 shipping player to run this test.");
        Assume.That(
            profilePath is not null && File.Exists(profilePath),
            $"Set {ProfileVariable} to the profile document of that player to run this test.");
        Assume.That(Environment.Is64BitProcess, "Run this test in a 64-bit process, because the player is x64.");

        var profile = UnrealProfile.Parse(File.ReadAllText(profilePath!)).Unwrap();
        string directory = Path.Combine(player!, "Fixture", "Binaries", "Win64");
        string readyPath = Path.Combine(directory, ReadyFileName);
        var started = DateTime.UtcNow;

        ProcessStartInfo startInfo = new(Path.Combine(directory, ExecutableName), "-nullrhi -nosound") { UseShellExecute = false };
        using var process = Process.Start(startInfo)!;

        try
        {
            WaitForReadyFile(process, readyPath, started);

            using RemoteProcessMemory memory = new(process);
            test(UnrealGame.Attach(memory, memory.GetModule(ExecutableName).Unwrap(), profile).Unwrap());
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

    // Wait for a ready file written after the start, because the file of an earlier run stays next to the executable.
    private static void WaitForReadyFile(Process process, string readyPath, DateTime started)
    {
        var clock = Stopwatch.StartNew();

        while (!File.Exists(readyPath) || File.GetLastWriteTimeUtc(readyPath) < started)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(process.HasExited, Is.False, $"The player exited before it wrote {ReadyFileName}.");
                Assert.That(clock.Elapsed, Is.LessThan(_readyTimeout), $"The player didn't write {ReadyFileName} within {_readyTimeout}.");
            }

            Thread.Sleep(_readyPollInterval);
        }
    }
}
