using AslHelp.Engines.Profiles;
using NUnit.Framework;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileVersionEdgeTests
{
    // A version that leaves a part off sorts below the longer version on either side, so a sort gets the same order both ways.
    [Test]
    public void Operators_VersionWithFewerParts_SortsBelowAndFlipsWhenSwapped()
    {
        ProfileVersion shorter = new("2021.3", 2021, 3);
        ProfileVersion longer = new("2021.3.5", 2021, 3, 5);

        bool[] results = [shorter == longer, shorter != longer, shorter < longer, shorter <= longer, shorter > longer, shorter >= longer];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results, Is.EqualTo([false, true, true, true, false, false]));
            Assert.That(shorter.CompareTo(longer), Is.Negative);
            Assert.That(longer.CompareTo(shorter), Is.Positive);
        }
    }

    // Equality goes by the parts, so 2 texts with the same parts are 1 version with 1 hash code.
    [Test]
    public void Equals_SamePartsOtherText_AreEqualWithEqualHash()
    {
        ProfileVersion dashed = new("2010-09", 2010, 9);
        ProfileVersion dotted = new("2010.9", 2010, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dashed, Is.EqualTo(dotted));
            Assert.That(dashed.GetHashCode(), Is.EqualTo(dotted.GetHashCode()));
            Assert.That(dashed.CompareTo(dotted), Is.Zero);
            Assert.That(dashed.ToString(), Is.EqualTo("2010-09"));
        }
    }

    // null sorts below any version on either side of every operator.
    [Test]
    public void Operators_Null_SortsBelowAnyVersion()
    {
        ProfileVersion version = new("1.0", 1, 0);
        ProfileVersion? none = null;

        bool[] results = [none < version, version > none, none <= version, version >= none, version.CompareTo(none) > 0];

        Assert.That(results, Has.All.True);
    }
}
