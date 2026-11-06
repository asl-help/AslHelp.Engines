using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class MeasureConsoleFormatterTests
{
    private const string PlainLine = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} info: category\[7\] text\r?\n$";

    // A line outside any scope has no arrow.
    [Test]
    public void Write_NoScope_WritesThePlainLine()
    {
        Assert.That(Write(LogLevel.Information, new LoggerExternalScopeProvider()), Does.Match(PlainLine));
    }

    // A missing scope provider reads the same as no open scope.
    [Test]
    public void Write_NoScopeProvider_WritesThePlainLine()
    {
        Assert.That(Write(LogLevel.Information, null), Does.Match(PlainLine));
    }

    // Each open scope adds 2 spaces in front of the arrow and the text of a scope stays out of the line.
    [TestCase(1, "  => ")]
    [TestCase(2, "    => ")]
    public void Write_OpenScopes_IndentsByDepth(int depth, string indent)
    {
        LoggerExternalScopeProvider scopes = new();

        for (int scope = 0; scope < depth; scope++)
        {
            _ = scopes.Push("scope text");
        }

        Assert.That(Write(LogLevel.Information, scopes), Does.EndWith($"category[7] {indent}text{Environment.NewLine}").And.Not.Contain("scope text"));
    }

    // Every level has its 4-letter tag, the same width as the others.
    [TestCase(LogLevel.Trace, "trce")]
    [TestCase(LogLevel.Debug, "dbug")]
    [TestCase(LogLevel.Information, "info")]
    [TestCase(LogLevel.Warning, "warn")]
    [TestCase(LogLevel.Error, "fail")]
    [TestCase(LogLevel.Critical, "crit")]
    public void Write_Level_WritesItsTag(LogLevel level, string tag)
    {
        Assert.That(Write(level, null), Does.Contain($" {tag}: category"));
    }

    private static string Write(LogLevel level, IExternalScopeProvider? scopes)
    {
        using StringWriter writer = new();
        LogEntry<string> entry = new(level, "category", new EventId(7), "text", null, static (state, _) => state);

        new MeasureConsoleFormatter().Write(entry, scopes, writer);

        return writer.ToString();
    }
}
