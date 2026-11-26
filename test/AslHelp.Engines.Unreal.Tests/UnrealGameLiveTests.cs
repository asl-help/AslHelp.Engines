using AslHelp.Memory;
using AslHelp.Memory.Remote;
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
            var game = UnrealGame.Attach(memory, memory.GetModule(ExecutableName).Unwrap(), profile).Unwrap();
            var world = game.GWorld.Read().Unwrap();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(world, Is.Not.EqualTo(Address.Zero));
                Assert.That(game.Objects.NameOf(game.Objects.ClassOf(world).Unwrap()).Unwrap(), Is.EqualTo("World"));
                Assert.That(game.ObjectArray.ReadCount().Unwrap(), Is.GreaterThan(0));
            }
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
