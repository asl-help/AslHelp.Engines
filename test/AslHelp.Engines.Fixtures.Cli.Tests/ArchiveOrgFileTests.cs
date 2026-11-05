using NUnit.Framework;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class ArchiveOrgFileTests
{
    // sha1 of "abc" from FIPS 180.
    private const string AbcSha1Hex = "a9993e364706816aba3e25717850c26c9cd0d89d";

    // A name with a space is escaped, so the URL stays 1 path segment.
    [Test]
    public void Url_NameWithSpace_EscapesIt()
    {
        ArchiveOrgFile file = new("players", "a b.bin", PinnedHash.Sha1(AbcSha1Hex));

        Assert.That(file.Url.AbsoluteUri, Is.EqualTo("https://archive.org/download/players/a%20b.bin"));
    }
}
