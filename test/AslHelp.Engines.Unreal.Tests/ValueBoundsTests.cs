using NUnit.Framework;
using System;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class ValueBoundsTests
{
    // A bound below 1 would fail every string or collection, so it throws where it's set.
    [TestCase(0)]
    [TestCase(-1)]
    public void Init_BoundBelowOne_Throws(int bound)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => new ValueBounds { MaxChars = bound }, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new ValueBounds { MaxElements = bound }, Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    // A read sizes its buffers by the bounds, so a bound above the limit throws where it's set and the limit itself is fine.
    [Test]
    public void Init_BoundAboveTheLimit_Throws()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => new ValueBounds { MaxChars = ValueBounds.Limit + 1 }, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new ValueBounds { MaxElements = ValueBounds.Limit + 1 }, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(new ValueBounds { MaxChars = ValueBounds.Limit, MaxElements = ValueBounds.Limit }.MaxElements, Is.EqualTo(1 << 20));
        }
    }

    // The default bounds are 512 characters and 1024 elements.
    [Test]
    public void Default_Bounds_Are512And1024()
    {
        Assert.That((ValueBounds.Default.MaxChars, ValueBounds.Default.MaxElements), Is.EqualTo((512, 1024)));
    }
}
