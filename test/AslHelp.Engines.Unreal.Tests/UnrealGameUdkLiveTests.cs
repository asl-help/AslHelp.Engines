using AslHelp.Engines.Errors;
using AslHelp.Engines.Profiles;
using AslHelp.Memory;
using AslHelp.Memory.Remote;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkLiveTests
{
    private const string BinariesPath = @"Binaries\Win32";
    private const string ExecutableName = "UDK.exe";
    private const string LogPath = @"UDKGame\Logs\Launch.log";
    private const string MapsPath = @"UDKGame\Content\Maps";
    private const int MaxStarts = 3;

    // Set FIXTURES_UNREAL3_PLAYER to the folder of a UDK player to attach to it.
    private const string PlayerVariable = "FIXTURES_UNREAL3_PLAYER";

    // Set FIXTURES_UNREAL3_PROFILE to the profile document of that player.
    private const string ProfileVariable = "FIXTURES_UNREAL3_PROFILE";

    private const string ReadyLine = "ScriptLog: Fixture ready";

    private static readonly TimeSpan _readyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _readyTimeout = TimeSpan.FromMinutes(2);

    // The real player's GWorld points at an object of the class World once the fixture logs its ready line.
    [Test]
    public void Attach_RealPlayer_ReadsWorldAsTheClassOfGWorld()
    {
        WithAttachedPlayer(game =>
        {
            var world = game.GWorld.Path.Read<Address>().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(world, Is.Not.EqualTo(Address.Zero));
                Assert.That(game.ClassOf(world).Unwrap().Name, Is.EqualTo("World"));
                Assert.That(game.ObjectArray.ReadCount().Unwrap(), Is.GreaterThan(0));
            }
        });
    }

    // World in UDK doesn't have a property that reaches the game, so the live object of FixtureData is the root of the paths.
    [Test]
    public void GetInstances_RealPlayer_ReachesIntValueAndHpOfFixtureData()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(game.FindClass("World").Unwrap().GetProperties().Unwrap(), Is.Empty);
                Assert.That(data.Path.Member("IntValue").Read<int>().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Path.Member("Hp").Read<int>().Unwrap(), Is.InRange(1, 1000));
            }
        });
    }

    // A direct read gives the value of a watcher, boxed or typed and with the name in any case.
    [Test]
    public void Read_RealPlayerIntValueInAnyCase_EqualsTheWatcherValue()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single().Path;
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
            game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single().Path.Member("Hpp").UnwrapErr(),
            Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True));
    }

    // The value kinds that Unreal Engine 3 stores in its own way read the values PostBeginPlay sets, bools through their 4-byte masks.
    [Test]
    public void Read_RealPlayer_ReadsEveryValueKindOfFixtureData()
    {
        WithAttachedPlayer(game =>
        {
            var dataClass = game.FindClass("FixtureData").Unwrap();
            var data = dataClass.GetInstances().Unwrap().Single().Address;

            T read<T>(string name)
            {
                return game.Read<T>(data, dataClass.FindProperty(name).Unwrap()).Unwrap();
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read<float>("FloatValue"), Is.EqualTo(3.5f));
                Assert.That(
                    (read<bool>("BoolValue"), read<bool>("bFirst"), read<bool>("bSecond"), read<bool>("bThird")),
                    Is.EqualTo((true, true, false, true)));
                Assert.That(read<byte>("Kind"), Is.EqualTo(1));
                Assert.That(read<string>("StringValue"), Is.EqualTo("hello-fixture"));
                Assert.That(read<string>("NameValue"), Is.EqualTo("fixture-name"));
            }
        });
    }

    // On 2 ticks, 30 watchers of 1 counter that share the root at FixtureData give what a watcher of its own counter reads.
    [Test]
    public void CreateWatcher_RealPlayer_WatchersWithSharedStepsGiveTheValuesOfSingleReads()
    {
        WithAttachedPlayer(game =>
        {
            string[] names = ["IntValue", "FloatValue", "BoolValue", "bFirst", "bSecond", "Kind", "StringValue", "NameValue"];
            var data = game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single().Path;
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

    // Coord steps into the struct by its offset with the measured UStructProperty.Struct, so X reads the 7 PostBeginPlay sets.
    [Test]
    public void Read_RealPlayer_StepsIntoCoordOfFixtureData()
    {
        WithAttachedPlayer(game =>
        {
            var dataClass = game.FindClass("FixtureData").Unwrap();
            var data = dataClass.GetInstances().Unwrap().Single().Address;
            var coord = dataClass.FindProperty("Coord").Unwrap().Bind().Unwrap().Step(game.Memory, data).Unwrap();

            Assert.That(game.Read<int>(coord.Address, coord.StructType!.FindProperty("X").Unwrap()).Unwrap(), Is.EqualTo(7));
        });
    }

    // The arrays read through FScriptArray with the measured UArrayProperty.Inner. Position reads as its 3 floats.
    [Test]
    public void Read_RealPlayer_ReadsArraysAndPositionOfFixtureData()
    {
        WithAttachedPlayer(game =>
        {
            var dataClass = game.FindClass("FixtureData").Unwrap();
            var data = dataClass.GetInstances().Unwrap().Single().Address;
            int[] intArray = [100, 200, 300];
            string[] stringArray = ["a", "b", "c"];
            int[][] coordArray = [[1, 2], [3, 4]];
            float[] position = [1f, 2f, 3f];

            T read<T>(string name)
            {
                return game.Read<T>(data, dataClass.FindProperty(name).Unwrap()).Unwrap();
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read<int[]>("IntArray"), Is.EqualTo(intArray));
                Assert.That(read<string[]>("StringArray"), Is.EqualTo(stringArray));
                Assert.That(read<int[][]>("CoordArray"), Is.EqualTo(coordArray));
                Assert.That(read<Address[]>("Items"), Has.Length.EqualTo(2).And.None.EqualTo(Address.Zero));
                Assert.That(read<float[]>("Position"), Is.EqualTo(position));
            }
        });
    }

    // Position steps into X, Y and Z by name and reads as a struct value with member names. The UDK fixture doesn't have a rotator.
    [Test]
    public void Read_RealPlayer_StepsIntoVectorByName()
    {
        WithAttachedPlayer(game =>
        {
            var data = game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single().Path;
            var position = data.Member("Position").Unwrap().CreateWatcher(new ManualTicks()).Current as StructValue;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Member("Position").Member("X").Read<float>().Unwrap(), Is.EqualTo(1f));
                Assert.That(data.Member("Position").Member("Y").Read<float>().Unwrap(), Is.EqualTo(2f));
                Assert.That(data.Member("Position").Member("Z").Read<float>().Unwrap(), Is.EqualTo(3f));
                Assert.That(position?.Names, Is.EqualTo(["X", "Y", "Z"]));
                Assert.That(position?.Values, Is.EqualTo([1f, 2f, 3f]));
            }
        });
    }

    // Unreal Engine 3 packages have bare names, so the fixture script package is Fixture and its classes are the fixture classes.
    [Test]
    public void FindPackage_RealPlayer_FindsTheFixturePackageByBareName()
    {
        WithAttachedPlayer(game =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    game.FindPackage("fixture").Unwrap().FindClass("FixtureData").Unwrap(),
                    Is.SameAs(game.FindClass("FixtureData").Unwrap()));
                Assert.That(game.FindPackage("Engine").Unwrap().FindClass("Engine").Unwrap().Name, Is.EqualTo("Engine"));
            }
        });
    }

    // The GEngine route of the document reaches the GameEngine of the server, whose config strings read through a path.
    [Test]
    public void GEngine_RealPlayer_ReadsAMemberOfTheEngine()
    {
        WithAttachedPlayer(game =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(game.ClassOf(game.GEngine.Path.Read<Address>().Unwrap()).Unwrap().Name, Is.EqualTo("GameEngine"));
                Assert.That(game.GEngine.Path.Member("TinyFontName").Read<string>().Unwrap(), Is.EqualTo("EngineFonts.TinyFont"));
            }
        });
    }

    // UDK.exe stamps its engine build, so detection reads a release that the document of the player's release lists too. Releases that
    // share a build read as the 1st of them, which the same document lists.
    [Test]
    public void Attach_RealPlayerWithoutAProfile_DetectsTheVersionAndPicksTheDocument()
    {
        WithPlayer((memory, module) =>
        {
            var game = UnrealGame.Attach(memory, module, ProfileGuessHandling.Disallow).Unwrap();
            var data = game.FindClass("FixtureData").Unwrap().GetInstances().Unwrap().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(game.Selection!.Target.Engine, Is.EqualTo(UnrealTarget.Unreal3));
                Assert.That(game.Selection.Entry.Profile.Versions, Does.Contain(PlayerVersion()).And.Contain(game.Selection.Target.Version.Text));
                Assert.That(game.Selection.Match, Is.EqualTo(ProfileMatch.Listed));
                Assert.That(data.Path.Member("IntValue").Read<int>().Unwrap(), Is.EqualTo(1337));
            }
        });
    }

    // Read the version from the manifest that the fixtures command line writes into each player folder.
    private static string PlayerVersion()
    {
        string manifest = File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable(PlayerVariable)!, "manifest.json"));

        return JsonNode.Parse(manifest)!["version"]!.GetValue<string>();
    }

    // Parse the profile of FIXTURES_UNREAL3_PROFILE and attach with it to the started server.
    private static void WithAttachedPlayer(Action<UnrealGame> test)
    {
        string? profilePath = Environment.GetEnvironmentVariable(ProfileVariable);

        Assume.That(
            profilePath is not null && File.Exists(profilePath),
            $"Set {ProfileVariable} to the profile document of that player to run this test.");

        var profile = UnrealProfile.Parse(File.ReadAllText(profilePath!)).Unwrap();

        WithPlayer((memory, module) => test(UnrealGame.Attach(memory, module, profile).Unwrap()));
    }

    // Start the server on the 1st map, wait for its ready line, run the test, then kill the server.
    // Start again up to 3 times, because another test run can use the same player.
    private static void WithPlayer(Action<IMemoryReader, MemoryModule> test)
    {
        string? player = Environment.GetEnvironmentVariable(PlayerVariable);

        Assume.That(player is not null && Directory.Exists(player), $"Set {PlayerVariable} to a UDK player folder to run this test.");

        string directory = Path.Combine(player!, BinariesPath);
        string logPath = Path.Combine(player!, LogPath);
        string arguments = $"server {FirstMap(player!)}?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH";
        ProcessStartInfo startInfo = new(Path.Combine(directory, ExecutableName), arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = directory,
        };

        for (int attempt = 1; ; attempt++)
        {
            // Delete the log of an earlier run, because its ready line would count as this run's.
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            using var process = Process.Start(startInfo)!;

            try
            {
                if (!WaitForReadyLine(process, logPath))
                {
                    string message = process.HasExited
                        ? $"The player exited before it logged '{ReadyLine}', {MaxStarts} starts."
                        : $"The player didn't log '{ReadyLine}' within {_readyTimeout}, {MaxStarts} starts.";

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

    // Take the 1st map in ordinal order of its path, like the fixtures tool does.
    private static string FirstMap(string player)
    {
        string first = Directory.GetFiles(Path.Combine(player, MapsPath), "*.udk", SearchOption.AllDirectories)
            .OrderBy(static map => map, StringComparer.Ordinal)
            .First();

        return Path.GetFileNameWithoutExtension(first);
    }

    // Read the log with every share flag, because the server keeps it open for writing.
    private static bool HasReadyLine(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return false;
        }

        using FileStream stream = new(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.EndsWith(ReadyLine, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Wait for the ready line and return false when the player exits before it or doesn't log it in time.
    private static bool WaitForReadyLine(Process process, string logPath)
    {
        var clock = Stopwatch.StartNew();

        while (!HasReadyLine(logPath))
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
