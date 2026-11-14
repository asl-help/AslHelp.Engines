using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Keeps the log entries and scopes of 1 player in memory until <see cref="Replay"/> writes them to another logger.
/// </summary>
/// <remarks>
///     Lets players run at once while the lines of each player stay together and in player order.
/// </remarks>
internal sealed class BufferedLogger : ILogger
{
    private readonly List<BufferedEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        Add(new ScopeOpened<TState>(state));

        return new ScopeCloser(this);
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Add(new Logged<TState>(logLevel, eventId, state, exception, formatter));
    }

    /// <summary>
    ///     Writes the kept entries to <paramref name="target"/> in the order they came in, with their scopes.
    /// </summary>
    /// <param name="target">The logger to write to.</param>
    public void Replay(ILogger target)
    {
        Stack<IDisposable?> scopes = [];

        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                entry.Replay(target, scopes);
            }
        }

        // Close a scope that the measurer left open, so the lines after the player do not end up inside it.
        while (scopes.Count > 0)
        {
            scopes.Pop()?.Dispose();
        }
    }

    private void Add(BufferedEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    // Closes 1 scope of the buffer, once, because a caller can dispose a scope twice.
    private sealed class ScopeCloser(BufferedLogger logger) : IDisposable
    {
        private bool _closed;

        public void Dispose()
        {
            if (!_closed)
            {
                _closed = true;
                logger.Add(new ScopeClosed());
            }
        }
    }

    // Represents 1 kept call to a logger.
    private abstract record BufferedEntry
    {
        public abstract void Replay(ILogger target, Stack<IDisposable?> scopes);
    }

    // Represents 1 log entry.
    private sealed record Logged<TState>(
        LogLevel Level,
        EventId EventId,
        TState State,
        Exception? Exception,
        Func<TState, Exception?, string> Formatter) : BufferedEntry
    {
        public override void Replay(ILogger target, Stack<IDisposable?> scopes)
        {
            // Check the level against the target here, because the buffer keeps every level and only the target has the filter.
            if (target.IsEnabled(Level))
            {
                target.Log(Level, EventId, State, Exception, Formatter);
            }
        }
    }

    // Represents the start of a scope.
    private sealed record ScopeOpened<TState>(
        TState State) : BufferedEntry
        where TState : notnull
    {
        public override void Replay(ILogger target, Stack<IDisposable?> scopes)
        {
            scopes.Push(target.BeginScope(State));
        }
    }

    // Represents the end of the scope that opened last.
    private sealed record ScopeClosed : BufferedEntry
    {
        public override void Replay(ILogger target, Stack<IDisposable?> scopes)
        {
            if (scopes.Count > 0)
            {
                scopes.Pop()?.Dispose();
            }
        }
    }
}
