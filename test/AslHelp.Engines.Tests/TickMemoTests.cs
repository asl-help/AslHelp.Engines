using NUnit.Framework;

namespace AslHelp.Engines.Tests;

public sealed class TickMemoTests
{
    // A new memo doesn't have a value for any tick.
    [Test]
    public void TryGet_BeforeSet_ReturnsFalse()
    {
        TickMemo<string> memo = new();

        Assert.That(memo.TryGet(new ManualTicks(), out _), Is.False);
    }

    // A value set at tick 5 of a counter comes back at tick 5 of that counter.
    [Test]
    public void TryGet_SameCounterSameTick_ReturnsTheValue()
    {
        ManualTicks ticks = new() { Value = 5 };
        TickMemo<string> memo = new();
        memo.Set(ticks, "value");

        bool found = memo.TryGet(ticks, out string? value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("value"));
        }
    }

    // A value set at tick 5 doesn't come back at tick 6.
    [Test]
    public void TryGet_AfterTheTickMoved_ReturnsFalse()
    {
        ManualTicks ticks = new() { Value = 5 };
        TickMemo<string> memo = new();
        memo.Set(ticks, "value");
        ticks.Value++;

        Assert.That(memo.TryGet(ticks, out _), Is.False);
    }

    // 2 counters at the same tick number don't share a value.
    [Test]
    public void TryGet_AnotherCounterAtTheSameTick_ReturnsFalse()
    {
        TickMemo<string> memo = new();
        memo.Set(new ManualTicks { Value = 5 }, "value");

        Assert.That(memo.TryGet(new ManualTicks { Value = 5 }, out _), Is.False);
    }

    // After a set with the 2nd counter, the 1st counter doesn't get a value at its own tick.
    [Test]
    public void Set_AnotherCounter_ReplacesTheValueOfTheFirst()
    {
        ManualTicks first = new() { Value = 5 };
        ManualTicks second = new() { Value = 5 };
        TickMemo<string> memo = new();
        memo.Set(first, "first");
        memo.Set(second, "second");

        bool firstFound = memo.TryGet(first, out _);
        bool secondFound = memo.TryGet(second, out string? value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstFound, Is.False);
            Assert.That(secondFound, Is.True);
            Assert.That(value, Is.EqualTo("second"));
        }
    }

    // A null value set at tick 5 comes back as the value of tick 5.
    [Test]
    public void TryGet_NullValueSet_ReturnsTrueWithNull()
    {
        ManualTicks ticks = new() { Value = 5 };
        TickMemo<string?> memo = new();
        memo.Set(ticks, null);

        bool found = memo.TryGet(ticks, out string? value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.Null);
        }
    }

    // A counter that goes back to tick 5 gets the value of tick 5 again, like a watcher keeps its read.
    [Test]
    public void TryGet_CounterBackAtTheTickOfTheValue_ReturnsTheValue()
    {
        ManualTicks ticks = new() { Value = 5 };
        TickMemo<string> memo = new();
        memo.Set(ticks, "value");
        ticks.Value = 6;
        bool foundAtSix = memo.TryGet(ticks, out _);
        ticks.Value = 5;

        bool found = memo.TryGet(ticks, out string? value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(foundAtSix, Is.False);
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("value"));
        }
    }

    // A counter at long.MinValue gets its value back and a new memo doesn't match it.
    [Test]
    public void TryGet_TickAtTheSmallestValue_ReturnsTheValue()
    {
        ManualTicks ticks = new() { Value = long.MinValue };
        TickMemo<int> memo = new();
        memo.Set(ticks, 7);

        bool found = memo.TryGet(ticks, out int value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(new TickMemo<int>().TryGet(new ManualTicks(), out _), Is.False);
        }
    }

    // A null counter fails both calls with the parameter name.
    [Test]
    public void SetAndTryGet_NullCounter_Throw()
    {
        TickMemo<string> memo = new();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => memo.Set(null!, "value"), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("ticks"));
            Assert.That(() => memo.TryGet(null!, out _), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("ticks"));
        }
    }
}
