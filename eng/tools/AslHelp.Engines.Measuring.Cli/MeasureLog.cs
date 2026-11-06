using Microsoft.Extensions.Logging;
using System;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Provides the log lines of a run over the players of 1 engine.
/// </summary>
internal static partial class MeasureLog
{
    private static readonly Func<ILogger, string, IDisposable?> _playerScope = LoggerMessage.DefineScope<string>("{Version}");

    /// <summary>
    ///     Opens the scope of the lines of 1 player.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="version">The version of the player.</param>
    /// <returns>
    ///     The scope to dispose after the last line of the player.
    /// </returns>
    public static IDisposable? BeginPlayerScope(this ILogger logger, string version)
    {
        return _playerScope(logger, version);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Version} {Player}/{Players} Starting player.")]
    public static partial void Starting(this ILogger logger, string version, int player, int players);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Measured in {Seconds:F1} s: {Globals} globals, {Offsets} offsets, {Unmeasured} not found.")]
    public static partial void Measured(this ILogger logger, double seconds, int globals, int offsets, int unmeasured);

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

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Found {Players} players in '{Folder}'.")]
    public static partial void PlayersFound(this ILogger logger, int players, string folder);
}
