using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Tests;

/// <summary>
///     Writes log lines to 2 writers, errors to the 2nd, so a test can check what a run logged.
/// </summary>
/// <param name="output">The writer for the lines below an error.</param>
/// <param name="error">The writer for the errors.</param>
internal sealed class WriterLogger(
    TextWriter output,
    TextWriter error) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        (logLevel >= LogLevel.Error ? error : output).WriteLine(formatter(state, exception));
    }
}
