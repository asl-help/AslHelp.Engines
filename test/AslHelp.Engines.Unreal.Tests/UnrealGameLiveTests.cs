using AslHelp.Memory;
using AslHelp.Memory.Remote;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
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
    public void ReadInt32_RealPlayer_ReadsIntValueAndHpThroughTheGameMode()
    {
        WithAttachedPlayer(game =>
        {
            var world = game.GWorld.Read().Unwrap();
            var gameMode = game.ReadObject(world, game.ClassOf(world).Unwrap().FindProperty("AuthorityGameMode").Unwrap()).Unwrap();
            var gameModeClass = game.ClassOf(gameMode).Unwrap();
            var data = game.ReadObject(gameMode, gameModeClass.FindProperty("FixtureData").Unwrap()).Unwrap();
            var dataClass = game.ClassOf(data).Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(gameModeClass.Name, Is.EqualTo("FixtureGameMode"));
                Assert.That(game.ReadInt32(data, dataClass.FindProperty("IntValue").Unwrap()).Unwrap(), Is.EqualTo(1337));
                Assert.That(game.ReadInt32(data, dataClass.FindProperty("Hp").Unwrap()).Unwrap(), Is.InRange(1, 1000));
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
            var data = game.GWorld.ObjectMember("AuthorityGameMode").ObjectMember("FixtureData");
            ManualTicks ticks = new();
            var hp = data.Int32Member("Hp").CreateWatcher(ticks);
            var intValue = data.Int32Member("IntValue").CreateWatcher(ticks);

            int firstHp = hp.Current;
            _ = intValue.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(data.Int32Member("IntValue").Read().Unwrap(), Is.EqualTo(1337));
                Assert.That(data.Int32Member("Hp").Read().Unwrap(), Is.InRange(1, 1000));
                Assert.That(firstHp, Is.InRange(1, 1000));
                Assert.That(hp.Current, Is.InRange(1, 1000));
                Assert.That(intValue.Current, Is.EqualTo(1337));
                Assert.That(intValue.Changed, Is.False);
            }
        });
    }

    // A chain written through dynamic watches Hp of the real player.
    [Test]
    public void GWorldChain_RealPlayer_WatchesHpThroughTheChain()
    {
        WithAttachedPlayer(game =>
        {
            IWatcherSource hp = game.GWorldChain.AuthorityGameMode.FixtureData.Hp;
            ManualTicks ticks = new();
            var watcher = (Watcher<int>)hp.CreateWatcher(ticks);

            int first = watcher.Current;
            ticks.Value++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.InRange(1, 1000));
                Assert.That(watcher.Current, Is.InRange(1, 1000));
            }
        });
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
