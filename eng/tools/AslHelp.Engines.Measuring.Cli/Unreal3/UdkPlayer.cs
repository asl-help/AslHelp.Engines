using AslHelp.Engines.Fixtures;
using AslHelp.Engines.Fixtures.Unreal3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Runs the server of a player on this machine until the fixture game is ready.
/// </summary>
internal sealed class UdkPlayer : IDisposable
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _readyTimeout = TimeSpan.FromMinutes(2);

    private readonly string _logPath;
    private readonly Player _player;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UdkPlayer"/> class.
    /// </summary>
    /// <param name="player">The running server.</param>
    /// <param name="logPath">The path of the log of the server.</param>
    private UdkPlayer(Player player, string logPath)
    {
        _player = player;
        _logPath = logPath;
    }

    /// <summary>
    ///     Starts the server of the player in <paramref name="playerDirectory"/> and waits for the ready line in its log.
    /// </summary>
    /// <param name="playerDirectory">The player folder.</param>
    /// <param name="cancellationToken">The token that stops the wait and ends the server.</param>
    /// <returns>
    ///     The running server.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">The player has no <c>Binaries/Win32</c> folder.</exception>
    /// <exception cref="FileNotFoundException">The player has no map or no <c>UDK.exe</c>.</exception>
    /// <exception cref="InvalidOperationException">The server exits before it is ready.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">
    ///     Another player kept the machine lock too long or the server is not ready within 2 minutes.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The machine lock exists under another user account.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">The job, the desktop or the process cannot be created.</exception>
    public static async Task<UdkPlayer> StartAsync(string playerDirectory, CancellationToken cancellationToken)
    {
        string map = UdkInstall.FindMap(playerDirectory);
        string logPath = Path.Combine(playerDirectory, UdkInstall.LogPath);

        // Delete the log of an earlier run that died, because its ready line would count as this run's.
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        PlayerStartInfo info = new(
            Path.Combine(playerDirectory, UdkInstall.ServerPath),
            UdkInstall.ServerArguments(map),
            Path.Combine(playerDirectory, UdkInstall.BinariesPath))
        {
            HiddenDesktop = true,
        };

        UdkPlayer player = new(Player.Start(info), logPath);

        try
        {
            await player.WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);

            return player;
        }
        catch
        {
            player.Dispose();

            throw;
        }
    }

    /// <summary>
    ///     Ends the server and deletes its log, because the player folder stays in use and the log has the paths of this
    ///     machine.
    /// </summary>
    public void Dispose()
    {
        _player.Dispose();
        DeleteLog(_logPath);
    }

    /// <summary>
    ///     Deletes the log at <paramref name="logPath"/> if the server has let go of it.
    /// </summary>
    /// <remarks>
    ///     A failed delete stays quiet, because the server can outlive <see cref="Player.Dispose"/> for a moment and the
    ///     delete runs while a failure or a cancel is on its way out.
    /// </remarks>
    /// <param name="logPath">The path of the log.</param>
    internal static void DeleteLog(string logPath)
    {
        try
        {
            File.Delete(logPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    ///     Waits until the log of the server has the ready line.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the wait.</param>
    /// <returns>
    ///     A task that completes once the log has the ready line.
    /// </returns>
    /// <exception cref="InvalidOperationException">The server exits before it is ready.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The server is not ready in time.</exception>
    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        UdkLogScan scan = new(_logPath);
        long started = Stopwatch.GetTimestamp();

        while (!scan.HasReadyLine())
        {
            if (_player.HasExited)
            {
                throw new InvalidOperationException(UdkSR.FormatPlayerExited(_player.Process.ExitCode));
            }

            if (Stopwatch.GetElapsedTime(started) > _readyTimeout)
            {
                throw new TimeoutException(UdkSR.FormatPlayerNotReady(_readyTimeout.TotalSeconds));
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
