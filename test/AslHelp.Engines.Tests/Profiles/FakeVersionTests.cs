using NUnit.Framework;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class FakeVersionTests
{
    // The made-up engine ranks its release types, so ProfileVersion sorts them in that order.
    [Test]
    public void Parse_ReleaseTypes_OrderAlphaBetaFinalPatchExperimental()
    {
        string[] ordered = ["2019.3.0a9", "2019.3.0b1", "2019.3.0f1", "2019.3.0p1", "2019.3.0x"];

        for (int i = 1; i < ordered.Length; i++)
        {
            Assert.That(FakeVersion.Parse(ordered[i - 1]), Is.LessThan(FakeVersion.Parse(ordered[i])!), $"{ordered[i - 1]} before {ordered[i]}");
        }
    }

    // A letter is a part of its own, so an alpha and a final of 1 patch are 2 versions.
    [Test]
    public void Parse_AlphaAndFinalOf1Patch_AreNotEqual()
    {
        var alpha = FakeVersion.Parse("2022.3.5a1");
        var final = FakeVersion.Parse("2022.3.5f1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alpha, Is.Not.EqualTo(final));
            Assert.That(alpha, Is.LessThan(final!));
        }
    }

    // c ranks like f. Text after the increment, a 2nd release type and a letter outside the types don't count.
    [TestCase("2022.3.8f1", "2022.3.8c1")]
    [TestCase("2019.3.0f11", "2019.3.0f11-Sunflower")]
    [TestCase("2022.3.8f1", "2022.3.8f1c1")]
    [TestCase("2020.1.0", "2020.1.0q7")]
    public void Parse_TextTheSchemeIgnores_ComparesEqual(string version, string same)
    {
        Assert.That(FakeVersion.Parse(same), Is.EqualTo(FakeVersion.Parse(version)));
    }

    // A release type adds the rank and the increment after the numbers, so 2022.3.8f1c1 has 5 parts.
    [Test]
    public void Parse_FinalWithASuffix_ReadsTheNumbersRankAndIncrement()
    {
        var version = FakeVersion.Parse("2022.3.8f1c1")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(version.Parts, Is.EqualTo([2022, 3, 8, 2, 1]));
            Assert.That(version.Text, Is.EqualTo("2022.3.8f1c1"));
        }
    }
}
