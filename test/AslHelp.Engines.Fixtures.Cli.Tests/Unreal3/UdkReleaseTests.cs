using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkReleaseTests
{
    // The file has every version once in ordinal order, so an unpinned version lists them in version order.
    [Test]
    public void All_Rows_Has27InVersionOrderWithUniquePins()
    {
        string[] versions = [.. EmbeddedReleases<UdkRelease>.All.Select(row => row.Version)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(versions, Has.Length.EqualTo(27));
            Assert.That(versions, Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(versions, Is.Unique);
            Assert.That(EmbeddedReleases<UdkRelease>.All.Select(row => row.Sha1), Is.Unique);
        }
    }

    // Every row maps to UDKInstall-<version>.exe of the udkinstaller item with its sha1, so every installer downloads pinned.
    [Test]
    public void All_EveryRow_MapsToItsPinnedFile()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UdkRelease>.All)
            {
                var file = row.File;

                Assert.That(file.Name, Is.EqualTo($"UDKInstall-{row.Version}.exe"), row.Version);
                Assert.That(file.Item, Is.EqualTo("udkinstaller"), row.Version);
                Assert.That(file.Hash.Hex, Is.EqualTo(row.Sha1), row.Version);
            }
        }
    }

    // Every spelling of a version finds its pinned row.
    [TestCase("2011-03-BETA2")]
    [TestCase("2011-03-beta2")]
    public void Find_EverySpelling_ReturnsTheSameRow(string spelling)
    {
        var release = EmbeddedReleases<UdkRelease>.Find(spelling);

        Assert.That(release, Is.EqualTo(new UdkRelease("2011-03-BETA2", "1c578892b9adfb861a62d4960cc5cfd80e26801f")));
    }

    // An unpinned version throws with the pinned versions and the file, without a parameter suffix, because the CLI prints the message as is.
    [Test]
    public void Find_UnpinnedVersion_ThrowsWithThePinnedVersions()
    {
        Assert.That(
            () => EmbeddedReleases<UdkRelease>.Find("1999-01"),
            Throws.InvalidOperationException
                .With.Message.Contains("1999-01")
                .And.Message.Contains("2009-11-BETA")
                .And.Message.Contains("2015-02")
                .And.Message.Contains("releases.json")
                .And.Message.Not.Contains("(Parameter"));
    }

    // A row reads with camelCase keys as in releases.json.
    [Test]
    public void Read_Row_ReadsEveryKey()
    {
        using var json = Utf8Stream(Row(version: "2.0", sha1: "0000000000000000000000000000000000000002"));

        var release = EmbeddedReleases<UdkRelease>.Read(json).Single();

        Assert.That(release, Is.EqualTo(new UdkRelease("2.0", "0000000000000000000000000000000000000002")));
    }

    // A null version fails the parse, so a half-copied row never reads as a null version.
    [Test]
    public void Read_RowWithNullVersion_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row().Replace("\"version\": \"1.0\"", "\"version\": null", StringComparison.Ordinal));

        Assert.That(
            () => EmbeddedReleases<UdkRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>());
    }

    // A key outside the 2 fails the parse, so a misspelled key never reads as a missing value.
    [Test]
    public void Read_RowWithUnknownKey_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream("""[{ "version": "1.0", "sha1": "0000000000000000000000000000000000000001", "note": "" }]""");

        Assert.That(
            () => EmbeddedReleases<UdkRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>().And.Message.Contains("releases.json"));
    }

    private static string Row(
        string version = "1.0",
        string sha1 = "0000000000000000000000000000000000000001")
    {
        return $$"""
            [{ "version": "{{version}}", "sha1": "{{sha1}}" }]
            """;
    }

    private static MemoryStream Utf8Stream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
