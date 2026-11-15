using AslHelp.Engines.Measuring.Cli.Unreal4;
using NUnit.Framework;
using System;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealVersionsTests
{
    // Both ends of a range count, a hotfix counts as its minor and 4.10 comes after 4.9.
    [TestCase("4.25.0", "4.25", null, true)]
    [TestCase("4.24.3", "4.25", null, false)]
    [TestCase("4.22.3", null, "4.22", true)]
    [TestCase("4.23.0", null, "4.22", false)]
    [TestCase("4.10.1", null, "4.9", false)]
    [TestCase("4.9.2", null, "4.10", true)]
    [TestCase("5.0.3", "4.25", null, true)]
    [TestCase("4.8.3", "4.8", "4.24", true)]
    [TestCase("4.24.3", "4.8", "4.24", true)]
    [TestCase("4.7.6", "4.8", "4.24", false)]
    [TestCase("4.25.4", "4.8", "4.24", false)]
    [TestCase("4.0.2", null, null, true)]
    public void Within_Boundaries_IncludeBothEnds(string version, string? from, string? until, bool expected)
    {
        var minor = UnrealVersions.MinorOf(Version.Parse(version));

        Assert.That(UnrealVersions.Within(minor, UnrealVersions.ParseMinor(from), UnrealVersions.ParseMinor(until)), Is.EqualTo(expected));
    }

    // A range end has exactly 2 parts, so a hotfix, a single number or text reads as no minor version.
    [TestCase("4.19", true)]
    [TestCase("5.0", true)]
    [TestCase("4.19.2", false)]
    [TestCase("4", false)]
    [TestCase("4.x", false)]
    [TestCase(null, false)]
    public void ParseMinor_Text_KeepsOnlyMajorAndMinor(string? text, bool parses)
    {
        Assert.That(UnrealVersions.ParseMinor(text) is not null, Is.EqualTo(parses));
    }

    // A hotfix drops out of the minor version, so a table compares against 4.19 for every 4.19 hotfix.
    [Test]
    public void MinorOf_VersionWithHotfix_ReturnsMajorAndMinor()
    {
        Assert.That(UnrealVersions.MinorOf(new Version(4, 19, 2)), Is.EqualTo(new Version(4, 19)));
    }
}
