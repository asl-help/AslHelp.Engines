using NUnit.Framework;
using System;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class PinnedHashEdgeCaseTests
{
    // sha1 of "abc" from FIPS 180.
    private const string Sha1Hex = "a9993e364706816aba3e25717850c26c9cd0d89d";

    // A digest with spaces or a line break around it is refused, since Sha1 never trims.
    [TestCase(" " + Sha1Hex)]
    [TestCase(Sha1Hex + "\n")]
    public void Sha1_WhitespaceAroundDigest_ThrowsFormatException(string hex)
    {
        Assert.That(() => PinnedHash.Sha1(hex), Throws.TypeOf<FormatException>());
    }

    // Digits and letters outside ASCII are refused even where char.IsDigit or char.IsLetter accepts them.
    [TestCase("١" + "9993e364706816aba3e25717850c26c9cd0d89d")]
    [TestCase("ａ" + "9993e364706816aba3e25717850c26c9cd0d89d")]
    public void Sha1_NonAsciiHexLookalike_ThrowsFormatException(string hex)
    {
        Assert.That(() => PinnedHash.Sha1(hex), Throws.TypeOf<FormatException>());
    }

    // The message quotes the bad digest, so the user can find the pin to fix.
    [Test]
    public void Sha1_BadDigest_MessageQuotesIt()
    {
        var exception = Assert.Throws<FormatException>(() => PinnedHash.Sha1("xyz"));

        Assert.That(exception!.Message, Does.Contain("'xyz'"));
    }
}
