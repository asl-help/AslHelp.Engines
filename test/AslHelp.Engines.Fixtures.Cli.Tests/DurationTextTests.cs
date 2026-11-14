using NUnit.Framework;
using System;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DurationTextTests
{
    // Whole minutes print as minutes and anything else as seconds, so a 90 minute step limit reads as 90 min and not 5400 s.
    [TestCase(90 * 60, "90 min")]
    [TestCase(2 * 60, "2 min")]
    [TestCase(90, "90 s")]
    [TestCase(30, "30 s")]
    [TestCase(0.5, "0.5 s")]
    [TestCase(0, "0 s")]
    public void Format_Duration_UsesTheLargestWholeUnit(double seconds, string expected)
    {
        Assert.That(DurationText.Format(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    // A fraction just under a whole number prints every digit, so a timeout just under a minute never reads as 60 s.
    [TestCase(599_996_000, "59.9996 s")]
    [TestCase(1, "0.0000001 s")]
    public void Format_FractionNearAWholeNumber_PrintsItExactly(long ticks, string expected)
    {
        Assert.That(DurationText.Format(TimeSpan.FromTicks(ticks)), Is.EqualTo(expected));
    }
}
