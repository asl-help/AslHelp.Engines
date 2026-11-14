using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class BufferedLoggerTests
{
    private static readonly (string Message, int Scopes)[] _insideThenAfter = [("inside", 1), ("after", 0)];
    private static readonly (string Message, int Scopes)[] _warningOnly = [("warning", 0)];

    // A line inside a scope of the buffer comes out inside that scope and the line after the scope comes out outside of it.
    [Test]
    public void Replay_LineInsideScope_WritesItInsideTheScope()
    {
        BufferedLogger buffer = new();
        RecordingLogger target = new(LogLevel.Trace);

        using (buffer.BeginScope("scope"))
        {
            Write(buffer, LogLevel.Information, "inside");
        }

        Write(buffer, LogLevel.Information, "after");
        buffer.Replay(target);

        Assert.That(target.Lines, Is.EqualTo(_insideThenAfter));
    }

    // The replay closes a scope that stays open in the buffer, so the next line of the target is outside of it.
    [Test]
    public void Replay_ScopeLeftOpen_ClosesIt()
    {
        BufferedLogger buffer = new();
        RecordingLogger target = new(LogLevel.Trace);

        _ = buffer.BeginScope("scope");
        Write(buffer, LogLevel.Information, "inside");
        buffer.Replay(target);
        Write(target, LogLevel.Information, "after");

        Assert.That(target.Lines, Is.EqualTo(_insideThenAfter));
    }

    // The buffer keeps every level and the replay drops the lines below the level of the target.
    [Test]
    public void Replay_LineBelowTheLevelOfTheTarget_DropsIt()
    {
        BufferedLogger buffer = new();
        RecordingLogger target = new(LogLevel.Warning);

        Write(buffer, LogLevel.Information, "information");
        Write(buffer, LogLevel.Warning, "warning");
        buffer.Replay(target);

        Assert.That(target.Lines, Is.EqualTo(_warningOnly));
    }

    private static void Write(ILogger logger, LogLevel level, string message)
    {
        logger.Log(level, default, message, null, static (state, _) => state);
    }
}

// Records the message of each line with the number of scopes open at that line.
file sealed class RecordingLogger(LogLevel level) : ILogger
{
    private readonly LoggerExternalScopeProvider _scopes = new();

    public List<(string Message, int Scopes)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _scopes.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= level;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        StrongBox<int> scopes = new();
        _scopes.ForEachScope(static (_, count) => count.Value++, scopes);

        Lines.Add((formatter(state, exception), scopes.Value));
    }
}
