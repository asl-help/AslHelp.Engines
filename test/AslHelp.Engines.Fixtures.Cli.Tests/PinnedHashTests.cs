using NUnit.Framework;
using System;
using System.Security.Cryptography;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class PinnedHashTests
{
    // sha1 of "abc" from FIPS 180.
    private const string Sha1Hex = "a9993e364706816aba3e25717850c26c9cd0d89d";

    // A sha1 digest gives a pin of the SHA1 algorithm with the hex as written.
    [Test]
    public void Sha1_LowercaseHex_ReturnsPinWithTheHex()
    {
        var hash = PinnedHash.Sha1(Sha1Hex);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PinnedHash.Algorithm, Is.EqualTo(HashAlgorithmName.SHA1));
            Assert.That(hash.Hex, Is.EqualTo(Sha1Hex));
        }
    }

    // ToString puts 'sha1:' before the hex, so a message shows which algorithm the pin uses.
    [Test]
    public void ToString_Pin_GivesAlgorithmAndHex()
    {
        Assert.That(PinnedHash.Sha1(Sha1Hex).ToString(), Is.EqualTo("sha1:" + Sha1Hex));
    }

    // A digest of any length other than 40 chars is refused.
    [TestCase("a9993e364706816aba3e25717850c26c9cd0d89")]
    [TestCase("a9993e364706816aba3e25717850c26c9cd0d89d0")]
    [TestCase("")]
    public void Sha1_WrongLength_ThrowsFormatException(string hex)
    {
        Assert.That(() => PinnedHash.Sha1(hex), Throws.TypeOf<FormatException>());
    }

    // The hex is lowercase only and is never normalized, same as a cache key. A 'sha1:' prefix is not part of it.
    [TestCase("A9993e364706816aba3e25717850c26c9cd0d89d")]
    [TestCase("g9993e364706816aba3e25717850c26c9cd0d89d")]
    [TestCase("sha1:a9993e364706816aba3e25717850c26c9cd")]
    public void Sha1_UppercaseOrNonHexChar_ThrowsFormatException(string hex)
    {
        Assert.That(() => PinnedHash.Sha1(hex), Throws.TypeOf<FormatException>());
    }
}
