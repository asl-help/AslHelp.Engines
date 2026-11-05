using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Runs <c>UDK.exe</c> as a dedicated server with the fixture game and waits for its ready line.
/// </summary>
internal static class UdkServer
{
    /// <summary>
    ///     Represents the server command line, with the map as <c>{0}</c>.
    /// </summary>
    public const string Command = $"{FilePath} {ArgumentsFormat}";

    /// <summary>
    ///     Represents the server arguments, with the map as <c>{0}</c>.
    /// </summary>
    /// <remarks>
    ///     The server renders nothing, so the fixture game runs on it. <c>-FORCELOGFLUSH</c> flushes every log line, so
    ///     the poll reads a line as soon as it is logged.
    /// </remarks>
    private const string ArgumentsFormat = "server {0}?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH";

    /// <summary>
    ///     Represents the path of <c>UDK.exe</c>, relative to the install.
    /// </summary>
    private const string FilePath = $@"{UdkInstall.BinariesPath}\{UdkInstall.ExecutableName}";

    private const int LogBufferSize = 4096;
    private const string MapPattern = "*.udk";
    private const string MapsPath = @"UDKGame\Content\Maps";

    // Match the tag and the text of the line FixtureGame logs, because each line starts with a timestamp.
    private const string ReadyLine = "ScriptLog: Fixture ready";

    /// <summary>
    ///     Represents how long <see cref="CheckAsync"/> waits for the ready line by default, 2 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromMinutes(2);

    private static readonly CompositeFormat _arguments = CompositeFormat.Parse(ArgumentsFormat);
    private static readonly TimeSpan _logPollInterval = TimeSpan.FromMilliseconds(250);

    // Match the bytes of the ready line, because UDK writes Launch.log as ASCII.
    private static readonly byte[] _readyLineBytes = Encoding.ASCII.GetBytes(ReadyLine);

    /// <summary>
    ///     Starts the server of the install in <paramref name="installDirectory"/> and waits for the ready line.
    /// </summary>
    /// <remarks>
    ///     <c>Binaries/Win32/UDK.exe server &lt;map&gt;?game=Fixture.FixtureGame</c> runs in <paramref name="host"/>. The map is the 1st
    ///     <c>.udk</c> file under <c>UDKGame/Content/Maps</c> in ordinal order of its relative path. The check waits for the line <c>ScriptLog: Fixture ready</c> in <c>UDKGame/Logs/Launch.log</c>,
    ///     which <c>FixtureGame</c> logs once it has spawned the fixture actor. The log is deleted before the start, so a
    ///     line from an earlier run does not count. It is deleted after a pass too, so the player has no log of the build
    ///     machine. A missing <c>UDK.exe</c> or map throws before anything starts.
    ///     Any exit of the server is a failure, because the server runs until it is stopped.
    /// </remarks>
    /// <param name="host">The host that the server runs in.</param>
    /// <param name="installDirectory">The install with the built fixture package.</param>
    /// <param name="timeout">How long to wait for the ready line once the server runs.</param>
    /// <param name="cancellationToken">The token that stops the check and ends the server.</param>
    /// <returns>
    ///     A task that completes once the server has logged the ready line.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The install has no <c>UDK.exe</c> or no map.</exception>
    /// <exception cref="InvalidOperationException">The host cannot run anything or the server exits before the check finds the ready line.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The ready line is not in the log within <paramref name="timeout"/>.</exception>
    public static async Task CheckAsync(IPlayerHost host, string installDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        string serverPath = Path.Combine(installDirectory, FilePath);
        string logPath = Path.Combine(installDirectory, UdkInstall.LogPath);

        if (!File.Exists(serverPath))
        {
            throw new FileNotFoundException(SR.Format(UdkSR.ServerNotFound, serverPath), serverPath);
        }

        string map = FindMap(installDirectory);

        // Check the token before the delete, so a canceled call keeps the log of the last run and starts no server.
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        string arguments = string.Format(CultureInfo.InvariantCulture, _arguments, map);
        var player = await host.StartAsync(installDirectory, FilePath, arguments, cancellationToken).ConfigureAwait(false);

        await using (player.ConfigureAwait(false))
        {
            await WaitForReadyLineAsync(player, serverPath, logPath, timeout, cancellationToken).ConfigureAwait(false);
        }

        // Delete the log once the server is gone, because the install becomes the shared player and the log has the
        // machine name, the user and the paths of the build machine.
        File.Delete(logPath);
    }

    /// <summary>
    ///     Waits until the log of the running server <paramref name="player"/> has the ready line.
    /// </summary>
    /// <param name="player">The running server.</param>
    /// <param name="serverPath">The path of <c>UDK.exe</c>, for messages.</param>
    /// <param name="logPath">The path of <c>Launch.log</c>.</param>
    /// <param name="timeout">How long to wait for the ready line.</param>
    /// <param name="cancellationToken">The token that stops the wait.</param>
    /// <returns>
    ///     A task that completes once the log has the ready line.
    /// </returns>
    /// <exception cref="InvalidOperationException">The server exits before the wait finds the ready line.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The ready line is not in the log within <paramref name="timeout"/>.</exception>
    private static async Task WaitForReadyLineAsync(
        IPlayerRun player,
        string serverPath,
        string logPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Start the clock once the server runs, so the pull of a container image does not count against it.
        long started = Stopwatch.GetTimestamp();
        long scanned = 0;

        while (true)
        {
            if (player.HasExited)
            {
                throw ServerExited(player.ExitCode, serverPath, logPath);
            }

            if (HasReadyLine(logPath, ref scanned))
            {
                // Check the exit once more, so a server that exits while the log is read fails the check.
                if (player.HasExited)
                {
                    throw ServerExited(player.ExitCode, serverPath, logPath);
                }

                return;
            }

            if (Stopwatch.GetElapsedTime(started) > timeout)
            {
                // Point at the log only if it exists, because UDK.exe can hang before the engine opens it.
                string next = File.Exists(logPath)
                    ? SR.Format(UdkSR.ServerProgressFix, logPath)
                    : SR.Format(UdkSR.ServerStartFix, serverPath);

                throw new TimeoutException(SR.Format(UdkSR.ServerNotReady, ReadyLine, timeout.TotalSeconds, next));
            }

            await Task.Delay(_logPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Checks whether the log at <paramref name="logPath"/> has the ready line in the bytes after <paramref name="scanned"/>.
    /// </summary>
    /// <param name="logPath">The log, which the running engine keeps open.</param>
    /// <param name="scanned">
    ///     The number of bytes an earlier call read, set to the new length by a call that does not find the line. A log
    ///     shorter than it is read from the start.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if the log has the ready line. <see langword="false"/> if it does not or does not exist yet.
    /// </returns>
    public static bool HasReadyLine(string logPath, ref long scanned)
    {
        try
        {
            // Share read, write and delete, so the read never blocks the engine, which writes the log while it runs and
            // renames it to a backup when it starts again.
            using FileStream log = new(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);

            if (log.Length < scanned)
            {
                scanned = 0;
            }

            // Read the last bytes of the earlier call again, so a line split between 2 calls still matches.
            int overlap = _readyLineBytes.Length - 1;
            log.Position = Math.Max(0, scanned - overlap);

            Span<byte> buffer = stackalloc byte[LogBufferSize];
            int kept = 0;
            int read;

            while ((read = log.Read(buffer[kept..])) > 0)
            {
                int length = kept + read;

                if (buffer[..length].IndexOf(_readyLineBytes) >= 0)
                {
                    return true;
                }

                kept = Math.Min(length, overlap);
                buffer[(length - kept)..length].CopyTo(buffer);
            }

            scanned = log.Position;

            return false;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Creates the exception for a server that exited during the check.
    /// </summary>
    /// <param name="exitCode">The exit code of the server.</param>
    /// <param name="serverPath">The path of <c>UDK.exe</c>, for a server that exits before it opens the log.</param>
    /// <param name="logPath">The path of <c>Launch.log</c>.</param>
    /// <returns>
    ///     The exception, which points at the log only if the log exists.
    /// </returns>
    private static InvalidOperationException ServerExited(int exitCode, string serverPath, string logPath)
    {
        // Point at the log only if it exists, because UDK.exe can fail before the engine opens it.
        string next = File.Exists(logPath)
            ? SR.Format(UdkSR.ServerLogFix, logPath)
            : SR.Format(UdkSR.ServerStartFix, serverPath);

        return new InvalidOperationException(SR.Format(UdkSR.ServerFailed, exitCode, next));
    }

    /// <summary>
    ///     Gets the name of the 1st map under <c>UDKGame/Content/Maps</c> of <paramref name="installDirectory"/> in ordinal
    ///     order of its relative path.
    /// </summary>
    /// <param name="installDirectory">The install.</param>
    /// <returns>
    ///     The map file name without its extension, as the server takes it.
    /// </returns>
    /// <exception cref="FileNotFoundException">The install has no <c>.udk</c> file under <c>UDKGame/Content/Maps</c>.</exception>
    private static string FindMap(string installDirectory)
    {
        string mapsDirectory = Path.Combine(installDirectory, MapsPath);
        string? first = null;

        if (Directory.Exists(mapsDirectory))
        {
            foreach (string map in Directory.EnumerateFiles(mapsDirectory, MapPattern, SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(mapsDirectory, map);

                if (first is null || string.CompareOrdinal(relative, first) < 0)
                {
                    first = relative;
                }
            }
        }

        return first is null
            ? throw new FileNotFoundException(SR.Format(UdkSR.MapNotFound, MapPattern, mapsDirectory), mapsDirectory)
            : Path.GetFileNameWithoutExtension(first);
    }
}
