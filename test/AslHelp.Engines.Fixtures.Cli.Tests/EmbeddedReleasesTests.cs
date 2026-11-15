using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class EmbeddedReleasesTests
{
    private const string Sha1 = "0000000000000000000000000000000000000001";

    // The embedded rows read once in file order from the assembly of the row type.
    [Test]
    public void All_EmbeddedFile_ReadsTheRowsInFileOrder()
    {
        Assert.That(EmbeddedReleases<FakeRelease>.All.Select(row => row.Version), Is.EqualTo(["1.0", "2.0"]));
    }

    // Every key of a row finds that row in any case, because the row decides which spellings match.
    [TestCase("2.0")]
    [TestCase("second")]
    [TestCase("SECOND")]
    public void Find_KeyOfARow_ReturnsThatRow(string spelling)
    {
        Assert.That(EmbeddedReleases<FakeRelease>.Find(spelling).Version, Is.EqualTo("2.0"));
    }

    // An unpinned version throws with the name, the pins and the file, without a parameter suffix, because the CLI prints the message as is.
    [Test]
    public void Find_UnpinnedVersion_ThrowsWithThePins()
    {
        Assert.That(
            () => EmbeddedReleases<FakeRelease>.Find("3.0"),
            Throws.InvalidOperationException
                .With.Message.Contains("Fake '3.0'")
                .And.Message.Contains("1.0, 2.0")
                .And.Message.Contains(FakeRelease.ReleasesPath)
                .And.Message.Not.Contains("(Parameter"));
    }

    // A file that isn't a JSON array of rows throws with the path of the file, also when it's empty or cut off.
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{}")]
    [TestCase("1")]
    [TestCase("[")]
    [TestCase("[1]")]
    [TestCase("[[]]")]
    [TestCase("[{}]")]
    [TestCase("null")]
    [TestCase("[null]")]
    public void Read_NotAnArrayOfRows_ThrowsWithTheFile(string text)
    {
        using var json = Utf8Stream(text);

        Assert.That(() => Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains(FakeRelease.ReleasesPath));
    }

    // A file cut off after a row or with a trailing comma throws, so half a table never loads as a short table.
    [TestCase(",]")]
    [TestCase(",")]
    [TestCase("")]
    public void Read_DamagedEndOfFile_ThrowsWithTheFile(string end)
    {
        using var json = Utf8Stream(Rows(Row("1.0", "a")).TrimEnd(']') + end);

        Assert.That(() => Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains(FakeRelease.ReleasesPath));
    }

    // A byte that isn't UTF-8 throws with the path of the file instead of the raw decoder failure.
    [Test]
    public void Read_InvalidUtf8_ThrowsWithTheFile()
    {
        byte[] text = Encoding.UTF8.GetBytes(Rows(Row("1.0", "a")));
        text[Array.IndexOf(text, (byte)'a')] = 0xFF;
        using MemoryStream json = new(text);

        Assert.That(() => Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains(FakeRelease.ReleasesPath));
    }

    // A file saved with a byte order mark reads like any other.
    [Test]
    public void Read_ByteOrderMark_ReadsTheRow()
    {
        using MemoryStream json = new([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(Rows(Row("1.0", "a")))]);

        Assert.That(Read(json), Has.Count.EqualTo(1));
    }

    // A sha1 of the wrong length, in upper case or with a char outside lowercase hex throws at load with the value and the version.
    [TestCase("")]
    [TestCase("000000000000000000000000000000000000001")]
    [TestCase("00000000000000000000000000000000000000001")]
    [TestCase("000000000000000000000000000000000000000A")]
    [TestCase("000000000000000000000000000000000000000g")]
    [TestCase("000000000000000000000000000000000000000٣")]
    [TestCase("00000000000000000000 0000000000000000001")]
    public void Read_BadSha1_ThrowsWithTheValueAndTheVersion(string sha1)
    {
        using var json = Utf8Stream(Rows(Row("1.0", "a", sha1)));

        Assert.That(
            () => Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(FakeRelease.ReleasesPath).And.Message.Contains($"'{sha1}'").And.Message.Contains("'1.0'"));
    }

    // The check of the row type runs at load, so a value only that row type knows fails every build.
    [Test]
    public void Read_RowCheckFails_ThrowsTheRowFailure()
    {
        using var json = Utf8Stream(Rows(Row("1.0", FakeRelease.InvalidAlias)));

        Assert.That(() => Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("bad alias"));
    }

    // The check of the row runs before the sha1 check, because the sha1 message names the version that the row checks.
    [Test]
    public void Read_RowCheckAndSha1Fail_ThrowsTheRowFailure()
    {
        using var json = Utf8Stream(Rows(Row("1.0", FakeRelease.InvalidAlias, "bad")));

        Assert.That(() => Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("bad alias"));
    }

    // A key of an earlier row throws with the key in any case and across key kinds, so 2 rows never answer to 1 spelling.
    [TestCase("1.0", "b")]
    [TestCase("2.0", "A")]
    [TestCase("2.0", "1.0")]
    public void Read_KeyOfAnEarlierRow_ThrowsWithTheKey(string version, string alias)
    {
        using var json = Utf8Stream(Rows(Row("1.0", "a"), Row(version, alias)));

        Assert.That(() => Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains(FakeRelease.ReleasesPath).And.Message.Contains("2 rows"));
    }

    // A row whose own keys are spelled the same reads, because only a key of another row takes the spelling away.
    [Test]
    public void Read_RowWithEqualOwnKeys_ReadsTheRow()
    {
        using var json = Utf8Stream(Rows(Row("1.0", "1.0")));

        Assert.That(Read(json), Has.Count.EqualTo(1));
    }

    private static IReadOnlyList<FakeRelease> Read(Stream json)
    {
        return EmbeddedReleases<FakeRelease>.Read(json);
    }

    private static string Row(string version, string alias, string sha1 = Sha1)
    {
        return $$"""{ "version": "{{version}}", "alias": "{{alias}}", "sha1": "{{sha1}}" }""";
    }

    private static string Rows(params string[] rows)
    {
        return $"[{string.Join(", ", rows)}]";
    }

    private static MemoryStream Utf8Stream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
