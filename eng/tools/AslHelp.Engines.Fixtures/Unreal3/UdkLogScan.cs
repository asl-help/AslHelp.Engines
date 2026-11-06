using System;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Fixtures.Unreal3;

/// <summary>
///     Looks for the ready line in the log of a running server, reading only the bytes that earlier calls did not read.
/// </summary>
public sealed class UdkLogScan
{
    /// <summary>
    ///     Represents the line that the fixture game logs once it runs.
    /// </summary>
    /// <remarks>
    ///     The value has the tag and the text of the line, because each line starts with a timestamp.
    /// </remarks>
    public const string ReadyLine = "ScriptLog: Fixture ready";

    private const int BufferSize = 4096;

    // Match the bytes of the ready line, because UDK writes Launch.log as ASCII.
    private static readonly byte[] _readyLineBytes = Encoding.ASCII.GetBytes(ReadyLine);

    private readonly string _logPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UdkLogScan"/> class.
    /// </summary>
    /// <param name="logPath">The path of the log, which the running engine keeps open.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logPath"/> is <see langword="null"/>.</exception>
    public UdkLogScan(string logPath)
    {
        ArgumentNullException.ThrowIfNull(logPath);

        _logPath = logPath;
    }

    /// <summary>
    ///     Gets the number of bytes that the calls so far read without finding the line.
    /// </summary>
    internal long Scanned { get; private set; }

    /// <summary>
    ///     Checks whether the log has the ready line in the bytes after <see cref="Scanned"/>.
    /// </summary>
    /// <remarks>
    ///     A log shorter than <see cref="Scanned"/> is read from the start, because the engine starts a new log on each run.
    /// </remarks>
    /// <returns>
    ///     <see langword="true"/> if the log has the ready line. <see langword="false"/> if it does not or does not exist yet.
    /// </returns>
    public bool HasReadyLine()
    {
        try
        {
            // Share read, write and delete, so the read never blocks the engine, which writes the log while it runs and
            // renames it to a backup when it starts again.
            using FileStream log = new(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);

            if (log.Length < Scanned)
            {
                Scanned = 0;
            }

            // Read the last bytes of the earlier call again, so a line split between 2 calls still matches.
            int overlap = _readyLineBytes.Length - 1;
            log.Position = Math.Max(0, Scanned - overlap);

            Span<byte> buffer = stackalloc byte[BufferSize];
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

            Scanned = log.Position;

            return false;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}
