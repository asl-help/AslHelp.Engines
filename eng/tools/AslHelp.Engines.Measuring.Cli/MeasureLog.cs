using Microsoft.Extensions.Logging;
using System;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Provides the log lines of a run over the players of 1 engine.
/// </summary>
internal static partial class MeasureLog
{
    private static readonly Func<ILogger, IDisposable?> _groupsScope = LoggerMessage.DefineScope("groups");
    private static readonly Func<ILogger, string, IDisposable?> _playerScope = LoggerMessage.DefineScope<string>("{Version}");

    /// <summary>
    ///     Opens the scope of the lines of the version groups.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <returns>
    ///     The scope to dispose after the last group.
    /// </returns>
    public static IDisposable? BeginGroupsScope(this ILogger logger)
    {
        return _groupsScope(logger);
    }

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

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Version} {Player}/{Players}")]
    public static partial void Starting(this ILogger logger, string version, int player, int players);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "done ({Seconds:F1} s): {Globals} globals, {Offsets} offsets, {Unmeasured} not found")]
    public static partial void Measured(this ILogger logger, double seconds, int globals, int offsets, int unmeasured);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "not found: {Value}: {Reason}")]
    public static partial void NotFound(this ILogger logger, string value, string reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "failed: {Version}: {Reason}")]
    public static partial void PlayerFailed(this ILogger logger, string version, string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "no {Engine} players in '{Folder}'")]
    public static partial void NoPlayers(this ILogger logger, string folder, string engine);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "{Reason}")]
    public static partial void RunFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "report: '{Path}', measured {Measured}/{Players} players")]
    public static partial void ReportWritten(this ILogger logger, string path, int measured, int players);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "players: {Players} in '{Folder}'")]
    public static partial void PlayersFound(this ILogger logger, int players, string folder);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "versions with the same offsets:")]
    public static partial void GroupsHeader(this ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "[{Versions}]")]
    public static partial void Group(this ILogger logger, string versions);
}
