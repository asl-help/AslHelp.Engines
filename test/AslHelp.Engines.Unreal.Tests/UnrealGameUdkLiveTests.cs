using AslHelp.Memory;
using AslHelp.Memory.Remote;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkLiveTests
{
    private const string BinariesPath = @"Binaries\Win32";
    private const string ExecutableName = "UDK.exe";
    private const string LogPath = @"UDKGame\Logs\Launch.log";
    private const string MapsPath = @"UDKGame\Content\Maps";

    // Set FIXTURES_UNREAL3_PLAYER to the folder of the UDK 2010-09 player to attach to it.
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
            var world = game.GWorld.Read().Unwrap();

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
                Assert.That(data.Path.Member<int>("IntValue").Read().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Path.Member<int>("Hp").Read().Unwrap(), Is.InRange(1, 1000));
            }
        });
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
                Assert.That(game.ClassOf(game.GEngine.Read().Unwrap()).Unwrap().Name, Is.EqualTo("GameEngine"));
                Assert.That(game.GEngine.Member<string>("TinyFontName").Read().Unwrap(), Is.EqualTo("EngineFonts.TinyFont"));
            }
        });
    }

    // Start the server on the 1st map, wait for its ready line, attach and run the test, then kill the server.
    private static void WithAttachedPlayer(Action<UnrealGame> test)
    {
        string? player = Environment.GetEnvironmentVariable(PlayerVariable);
        string? profilePath = Environment.GetEnvironmentVariable(ProfileVariable);

        Assume.That(player is not null && Directory.Exists(player), $"Set {PlayerVariable} to the UDK 2010-09 player to run this test.");
        Assume.That(
            profilePath is not null && File.Exists(profilePath),
            $"Set {ProfileVariable} to the profile document of that player to run this test.");

        var profile = UnrealProfile.Parse(File.ReadAllText(profilePath!)).Unwrap();
        string directory = Path.Combine(player!, BinariesPath);
        string logPath = Path.Combine(player!, LogPath);

        // Delete the log of an earlier run, because its ready line would count as this run's.
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        string arguments = $"server {FirstMap(player!)}?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH";
        ProcessStartInfo startInfo = new(Path.Combine(directory, ExecutableName), arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        using var process = Process.Start(startInfo)!;

        try
        {
            WaitForReadyLine(process, logPath);

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

    private static void WaitForReadyLine(Process process, string logPath)
    {
        var clock = Stopwatch.StartNew();

        while (!HasReadyLine(logPath))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(process.HasExited, Is.False, $"The player exited before it logged '{ReadyLine}'.");
                Assert.That(clock.Elapsed, Is.LessThan(_readyTimeout), $"The player didn't log '{ReadyLine}' within {_readyTimeout}.");
            }

            Thread.Sleep(_readyPollInterval);
        }
    }
}
