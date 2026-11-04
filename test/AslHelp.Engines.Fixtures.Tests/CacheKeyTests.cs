using NUnit.Framework;

namespace AslHelp.Engines.Fixtures.Tests;

public sealed class CacheKeyTests
{
    private const string Hash = "3f2a9c01b7d4e5f60123456789abcdef0123456789abcdef0123456789abcdef";

    // The short form is a file name on disk, so it stays at the kind plus 16 hex chars.
    [Test]
    public void ToString_AnyKey_IsKindDashFirst16HexChars()
    {
        CacheKey key = new("player", Hash);

        Assert.That(key.ToString(), Is.EqualTo("player-3f2a9c01b7d4e5f6"));
    }

    // ShortHash is the first 16 hex chars of the hash, the same chars that ToString prints after the kind.
    [Test]
    public void ShortHash_AnyKey_IsFirst16HexChars()
    {
        CacheKey key = new("player", Hash);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(key.ShortHash, Is.EqualTo("3f2a9c01b7d4e5f6"));
            Assert.That(key.ToString(), Is.EqualTo($"{key.Kind}-{key.ShortHash}"));
        }
    }

    // The kind becomes part of a file name, so a path separator or '..' in it throws.
    [TestCase("")]
    [TestCase("../x")]
    [TestCase("a/b")]
    [TestCase("Player")]
    public void Constructor_BadKind_ThrowsArgumentException(string kind)
    {
        Assert.That(
            () => new CacheKey(kind, Hash),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("kind"));
    }

    // ToString cuts the hash at 16 chars, so a short or non-hex hash throws before it could fail there.
    [TestCase("abc")]
    [TestCase("3F2A9C01B7D4E5F60123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    [TestCase("3f2a9c01b7d4e5f60123456789abcdef0123456789abcdef0123456789abcdeg")]
    [TestCase("3f2a9c01b7d4e5f60123456789abcdef0123456789abcdef0123456789abcdef0")]
    public void Constructor_BadHash_ThrowsArgumentException(string hash)
    {
        Assert.That(
            () => new CacheKey("player", hash),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("hash"));
    }
}
