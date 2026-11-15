using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Unreal4;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Runs the executable of a player and waits for the ready file the fixture writes.
/// </summary>
internal static class UnrealGame
{
    /// <summary>
    ///     Represents the path of the log of the player, relative to the player folder. Shipping players don't write it.
    /// </summary>
    public const string LogPath = SavedPath + @"\Logs\Fixture.log";

    // Turn rendering and sound off, because a container doesn't have either.
    private const string Arguments = "-nullrhi -nosound";

    // The fixture writes this file next to the executable once it is set up.
    private const string ReadyFileName = "FixtureReady.txt";

    // The folder the player writes its logs and settings to, relative to the player folder.
    private const string SavedPath = @"Fixture\Saved";

    // STATUS_DLL_NOT_FOUND, the exit code of a process that Windows can't load a DLL for.
    private const int StatusDllNotFound = unchecked((int)0xC0000135);

    /// <summary>
    ///     Represents how long <see cref="CheckAsync"/> waits for the ready file by default, 3 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan _readyPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     Starts the executable of <paramref name="player"/> in <paramref name="playerDirectory"/> and waits for the ready file.
    /// </summary>
    /// <remarks>
    ///     The ready file and <c>Fixture/Saved</c> are deleted before the start, so the files of an earlier run don't count. Any
    ///     exit of the player is a failure, because the player runs until it is stopped. Once the player is gone both are
    ///     deleted again, so the player doesn't have the logs of the build machine.
    /// </remarks>
    /// <param name="host">The host the player runs in.</param>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="player">The player.</param>
    /// <param name="version">The engine version, such as 4.19.2.</param>
    /// <param name="timeout">How long to wait for the ready file once the player runs.</param>
    /// <param name="cancellationToken">The token that stops the check and ends the player.</param>
    /// <returns>
    ///     A task that completes once the fixture has written the ready file.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The executable does not exist.</exception>
    /// <exception cref="InvalidOperationException">The host cannot run anything or the player exits during the check.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The player doesn't write the ready file within <paramref name="timeout"/>.</exception>
    public static async Task CheckAsync(
        IPlayerHost host,
        string playerDirectory,
        UnrealPlayer player,
        Version version,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(playerDirectory);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(version);

        string executablePath = player.ExecutablePath(playerDirectory, version);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(UnrealSR.FormatExecutableNotFound(executablePath), executablePath);
        }

        string readyPath = Path.Combine(Path.GetDirectoryName(executablePath)!, ReadyFileName);
        string savedDirectory = Path.Combine(playerDirectory, SavedPath);
        string logPath = Path.Combine(playerDirectory, LogPath);

        // Check the token before the delete, so a canceled call keeps the files of the last run and starts no player.
        cancellationToken.ThrowIfCancellationRequested();

        DeleteRunFiles(readyPath, savedDirectory);

        string relativePath = Path.GetRelativePath(playerDirectory, executablePath);
        var run = await host.StartAsync(playerDirectory, relativePath, Arguments, cancellationToken).ConfigureAwait(false);

        await using (run.ConfigureAwait(false))
        {
            await WaitForReadyFileAsync(run, player, executablePath, readyPath, logPath, timeout, cancellationToken).ConfigureAwait(false);
        }

        // Delete what the run wrote once the player is gone, because the folder becomes the shared player and Development
        // builds log the machine name, the user and the paths there.
        DeleteRunFiles(readyPath, savedDirectory);
    }

    /// <summary>
    ///     Deletes the files a run of the player writes.
    /// </summary>
    /// <param name="readyPath">The path of the ready file.</param>
    /// <param name="savedDirectory">The <c>Fixture/Saved</c> folder of the player.</param>
    private static void DeleteRunFiles(string readyPath, string savedDirectory)
    {
        File.Delete(readyPath);

        if (Directory.Exists(savedDirectory))
        {
            Directory.Delete(savedDirectory, recursive: true);
        }
    }

    /// <summary>
    ///     Waits until the running player <paramref name="run"/> has written the ready file.
    /// </summary>
    /// <param name="run">The running player.</param>
    /// <param name="player">The player, for messages.</param>
    /// <param name="executablePath">The path of the executable, for messages.</param>
    /// <param name="readyPath">The path of the ready file.</param>
    /// <param name="logPath">The path of the log of the player, for messages.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">The token that stops the wait.</param>
    /// <returns>
    ///     A task that completes once the ready file exists.
    /// </returns>
    /// <exception cref="InvalidOperationException">The player exits during the wait.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The ready file doesn't exist within <paramref name="timeout"/>.</exception>
    private static async Task WaitForReadyFileAsync(
        IHostedRun run,
        UnrealPlayer player,
        string executablePath,
        string readyPath,
        string logPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Start the clock once the player runs, so the start of the container doesn't count against it.
        long started = Stopwatch.GetTimestamp();

        while (true)
        {
            if (run.HasExited)
            {
                throw GameExited(player, run.ExitCode, executablePath, logPath);
            }

            if (File.Exists(readyPath))
            {
                // Check the exit once more, so a player that exits while the file is checked fails the check.
                if (run.HasExited)
                {
                    throw GameExited(player, run.ExitCode, executablePath, logPath);
                }

                return;
            }

            if (Stopwatch.GetElapsedTime(started) > timeout)
            {
                // Point at the log only if it exists, because a Shipping player doesn't write one.
                string next = File.Exists(logPath)
                    ? UnrealSR.FormatGameProgressFix(logPath)
                    : UnrealSR.FormatGameStartFix(executablePath, Arguments);

                throw new TimeoutException(UnrealSR.FormatGameNotReady(player.Tag, ReadyFileName, DurationText.Format(timeout), next));
            }

            await Task.Delay(_readyPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the exception for a player that exited during the check.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="exitCode">The exit code of the player.</param>
    /// <param name="executablePath">The path of the executable, for a player without a log.</param>
    /// <param name="logPath">The path of the log of the player.</param>
    /// <returns>
    ///     The exception, which points at the log only if the log exists.
    /// </returns>
    private static InvalidOperationException GameExited(UnrealPlayer player, int exitCode, string executablePath, string logPath)
    {
        // Point at the log only if it exists, because a Shipping player doesn't write one and a missing DLL stops the player before it.
        string next = File.Exists(logPath)
            ? UnrealSR.FormatGameLogFix(logPath)
            : UnrealSR.FormatGameStartFix(executablePath, Arguments);

        return new InvalidOperationException(
            exitCode == StatusDllNotFound
                ? UnrealSR.FormatGameDllMissing(player.Tag, next)
                : UnrealSR.FormatGameExited(player.Tag, exitCode, next));
    }
}
