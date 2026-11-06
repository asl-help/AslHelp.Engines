using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Writes 1 line per log entry with the time, the level, the category and the event ID, indented by the open scopes.
/// </summary>
/// <remarks>
///     Shows the depth of the scopes and not their text, because the line that opens a scope already has that text.
/// </remarks>
internal sealed class MeasureConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    /// <summary>
    ///     Represents the name of the formatter in the console options.
    /// </summary>
    public const string FormatterName = "measure";

    /// <inheritdoc/>
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        StrongBox<int> depth = new();
        scopeProvider?.ForEachScope(static (_, count) => count.Value++, depth);

        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string indent = depth.Value > 0 ? $"{new string(' ', depth.Value * 2)}=> " : "";
        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        textWriter.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{time} {Tag(logEntry.LogLevel)}: {logEntry.Category}[{logEntry.EventId.Id}] {indent}{message}"));
    }

    private static string Tag(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none",
        };
    }
}
