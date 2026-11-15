using Microsoft.Extensions.Logging;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Provides the log lines of an Unreal measurement.
/// </summary>
internal static partial class UnrealLog
{
    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "pdb: {Path}")]
    public static partial void PdbRead(this ILogger logger, string path);

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "layouts: {Types} types, {Offsets} offsets")]
    public static partial void LayoutsRead(this ILogger logger, int types, int offsets);

    [LoggerMessage(EventId = 203, Level = LogLevel.Information, Message = "{Global}: 0x{Rva:X}")]
    public static partial void StaticFound(this ILogger logger, string global, uint rva);

    [LoggerMessage(EventId = 204, Level = LogLevel.Warning, Message = "skipped: {Version}: {Reason}")]
    public static partial void PlayerSkipped(this ILogger logger, string version, string reason);
}
