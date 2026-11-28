using AslHelp.Engines.Profiles;
using NUnit.Framework;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileVersionTests
{
    // A version without parts can't sort, so the constructor refuses it.
    [Test]
    public void Constructor_NoParts_Throws()
    {
        Assert.That(() => new ProfileVersion("1.0"), Throws.ArgumentException.With.Message.Contains("at least 1 part"));
    }

    // The version copies the parts, so changing the array after doesn't change the version.
    [Test]
    public void Constructor_PartsArrayChangedAfter_KeepsItsParts()
    {
        int[] parts = [1, 2];
        ProfileVersion version = new("1.2", parts);

        parts[1] = 3;

        Assert.That(version.Parts, Is.EqualTo([1, 2]));
    }

    // Parts compare as numbers, so 1.9 sorts below 1.10. Any version sorts above null.
    [Test]
    public void CompareTo_LowerPart_SortsBelow()
    {
        ProfileVersion lower = new("1.9", 1, 9);
        ProfileVersion higher = new("1.10", 1, 10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lower.CompareTo(higher), Is.Negative);
            Assert.That(higher.CompareTo(lower), Is.Positive);
            Assert.That(lower.CompareTo(null), Is.Positive);
        }
    }
}
