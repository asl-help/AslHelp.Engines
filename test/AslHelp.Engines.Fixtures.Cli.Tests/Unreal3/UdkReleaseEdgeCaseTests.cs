using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkReleaseEdgeCaseTests
{
    // Every spelling of every row finds that row, so 2011-03-BETA never gets the installer of 2011-03-BETA2.
    [Test]
    public void Find_EverySpellingOfEveryRow_ReturnsThatRow()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UdkRelease>.All)
            {
                foreach (string spelling in new[] { row.Version, row.Version.Replace("BETA", "beta", StringComparison.Ordinal) })
                {
                    Assert.That(EmbeddedReleases<UdkRelease>.Find(spelling), Is.SameAs(row), spelling);
                }
            }
        }
    }

    // A text close to a spelling throws, so a typo never installs some other engine.
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("2015")]
    [TestCase(" 2015-02")]
    [TestCase("2015-02 ")]
    [TestCase("2015_02")]
    [TestCase("2015-2")]
    [TestCase("2011-03-BETA3")]
    public void Find_NearSpelling_Throws(string spelling)
    {
        Assert.That(() => EmbeddedReleases<UdkRelease>.Find(spelling), Throws.InvalidOperationException.With.Message.Contains($"'{spelling}'"));
    }

    // A row that lacks any 1 of its keys throws, so a half-copied row never reads as a null sha1.
    [TestCase("version")]
    [TestCase("sha1")]
    public void Read_RowWithoutAKey_ThrowsWithTheKey(string key)
    {
        string[] pairs = [.. RowPairs().Where(pair => !pair.StartsWith($"\"{key}\"", StringComparison.Ordinal))];
        using var json = Utf8Stream($"[{{ {string.Join(", ", pairs)} }}]");

        Assert.That(() => EmbeddedReleases<UdkRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains(key));
    }

    // A null or a value of another type throws, so a row never reads with a value its type doesn't allow.
    [TestCase("\"sha1\": \"0000000000000000000000000000000000000001\"", "\"sha1\": null")]
    [TestCase("\"sha1\": \"0000000000000000000000000000000000000001\"", "\"sha1\": 1")]
    [TestCase("\"version\": \"1.0\"", "\"version\": 1")]
    [TestCase("\"version\": \"1.0\"", "\"version\": true")]
    public void Read_ValueOfTheWrongType_ThrowsWithTheFile(string pair, string other)
    {
        using var json = Utf8Stream(Row().Replace(pair, other, StringComparison.Ordinal));

        Assert.That(() => EmbeddedReleases<UdkRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("releases.json"));
    }

    private static string Row()
    {
        return $"[{{ {string.Join(", ", RowPairs())} }}]";
    }

    private static string[] RowPairs()
    {
        return
        [
            "\"version\": \"1.0\"",
            "\"sha1\": \"0000000000000000000000000000000000000001\"",
        ];
    }

    private static MemoryStream Utf8Stream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
