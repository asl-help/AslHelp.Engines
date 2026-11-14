using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Reads and writes the file that marks an engine folder as finished, with the pin it was installed from.
/// </summary>
internal static class EngineMarker
{
    /// <summary>
    ///     Represents the file name of the marker in the engine folder.
    /// </summary>
    public const string FileName = ".fixtures-sha1";

    /// <summary>
    ///     Reads the pin in the marker of <paramref name="engineDirectory"/>.
    /// </summary>
    /// <param name="engineDirectory">The engine folder, which doesn't have to exist.</param>
    /// <returns>
    ///     The pin as hex chars or <see langword="null"/> when the folder doesn't have a marker.
    /// </returns>
    public static string? Read(string engineDirectory)
    {
        string path = Path.Combine(engineDirectory, FileName);

        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    /// <summary>
    ///     Marks <paramref name="engineDirectory"/> as finished.
    /// </summary>
    /// <param name="engineDirectory">The engine folder.</param>
    /// <param name="sha1">The pin of the install as hex chars.</param>
    public static void Write(string engineDirectory, string sha1)
    {
        File.WriteAllText(Path.Combine(engineDirectory, FileName), sha1);
    }

    /// <summary>
    ///     Gets the time the marker of <paramref name="engineDirectory"/> was written.
    /// </summary>
    /// <param name="engineDirectory">The engine folder, with a marker.</param>
    /// <returns>
    ///     The time in UTC.
    /// </returns>
    public static DateTime WrittenAt(string engineDirectory)
    {
        return File.GetLastWriteTimeUtc(Path.Combine(engineDirectory, FileName));
    }
}
