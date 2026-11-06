using AslHelp.Memory;
using Microsoft.Extensions.Logging;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Provides the log lines of a UDK measurement.
/// </summary>
internal static partial class UdkLog
{
    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Server ready after {Seconds:F1} s.")]
    public static partial void ServerReady(this ILogger logger, double seconds);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Read {Sections} sections with {Bytes} bytes.")]
    public static partial void SectionsRead(this ILogger logger, int sections, int bytes);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Found the name array at {Rvas}.")]
    public static partial void NameArrayFound(this ILogger logger, string rvas);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Found the object array with {Objects} objects at {Rvas}.")]
    public static partial void ObjectArrayFound(this ILogger logger, int objects, string rvas);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Found {Offsets} offsets.")]
    public static partial void OffsetsFound(this ILogger logger, int offsets);

    [LoggerMessage(EventId = 106, Level = LogLevel.Information, Message = "Found {Global} at {Rvas}.")]
    public static partial void SlotFound(this ILogger logger, string global, string rvas);

    [LoggerMessage(EventId = 107, Level = LogLevel.Information, Message = "Read IntValue {Value} of FixtureData at {Address}.")]
    public static partial void FixtureValueRead(this ILogger logger, int value, Address address);
}
