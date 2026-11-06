using Microsoft.Extensions.Logging;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Provides the log lines of a run over the players of 1 engine.
/// </summary>
internal static partial class MeasureLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Measuring {Version}.")]
    public static partial void Measuring(this ILogger logger, string version);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Measured {Version} in {Seconds:F1} s: {Globals} globals, {Offsets} offsets, {Unmeasured} not found.")]
    public static partial void Measured(this ILogger logger, string version, double seconds, int globals, int offsets, int unmeasured);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "{Value} not found: {Reason}")]
    public static partial void NotFound(this ILogger logger, string value, string reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "{Version} failed: {Reason}")]
    public static partial void PlayerFailed(this ILogger logger, string version, string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "'{Folder}' has no {Engine} players.")]
    public static partial void NoPlayers(this ILogger logger, string folder, string engine);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "{Reason}")]
    public static partial void RunFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Wrote '{Path}' with {Measured} of {Players} players measured.")]
    public static partial void ReportWritten(this ILogger logger, string path, int measured, int players);
}
