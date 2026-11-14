using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealReleaseEdgeCaseTests
{
    // Every spelling of every row finds that row, so a minor that starts another minor, such as 4.1 and 4.10, never gets the wrong engine.
    [Test]
    public void Find_EverySpellingOfEveryRow_ReturnsThatRow()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UnrealRelease>.All)
            {
                foreach (string spelling in new[] { row.Version, row.Minor, row.App, row.App.Replace("UE_", "ue_", StringComparison.Ordinal) })
                {
                    Assert.That(EmbeddedReleases<UnrealRelease>.Find(spelling), Is.SameAs(row), spelling);
                }
            }
        }
    }

    // A text close to a spelling throws, so a typo never installs some other engine.
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("4")]
    [TestCase("4.")]
    [TestCase("4.19.")]
    [TestCase(" 4.19")]
    [TestCase("4.19 ")]
    [TestCase("4,19")]
    [TestCase("04.19")]
    [TestCase("4.19.2.0")]
    [TestCase("UE_4")]
    [TestCase("UE_4.19.2")]
    [TestCase("UE4.19")]
    [TestCase("٤.١٩")]
    public void Find_NearSpelling_Throws(string spelling)
    {
        Assert.That(() => EmbeddedReleases<UnrealRelease>.Find(spelling), Throws.InvalidOperationException.With.Message.Contains($"'{spelling}'"));
    }

    // -waitmutex is only on 4.8 and later and never next to -rocket, -nobootstrapexe only before 4.8 and the PATH
    // folder only on 4.15 to 4.19, 4.22 and 4.23, so an unproven row copied from another row fails here.
    [Test]
    public void All_EveryRow_HasTheSwitchesOfItsVersion()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UnrealRelease>.All)
            {
                var minor = Version.Parse(row.Minor);
                string[] flags = [.. row.EditorFlags, .. row.PackageFlags];
                bool vs2017 = (minor >= new Version(4, 15) && minor <= new Version(4, 19))
                    || minor == new Version(4, 22) || minor == new Version(4, 23);

                Assert.That(flags.Contains("-waitmutex") && (minor < new Version(4, 8) || flags.Contains("-rocket")), Is.False, row.Version);
                Assert.That(row.PackageFlags.Contains("-nobootstrapexe"), Is.EqualTo(minor < new Version(4, 8)), row.Version);
                Assert.That(row.PathPrepend is not null, Is.EqualTo(vs2017), row.Version);
            }
        }
    }

    // A row that lacks any 1 of its keys throws, so a half-copied row never reads with a default such as 0 install bytes.
    [TestCase("version")]
    [TestCase("app")]
    [TestCase("build")]
    [TestCase("sha1")]
    [TestCase("installBytes")]
    [TestCase("installTags")]
    [TestCase("players")]
    [TestCase("editorFlags")]
    [TestCase("packageFlags")]
    [TestCase("pathPrepend")]
    [TestCase("runtimes")]
    public void Read_RowWithoutAKey_ThrowsWithTheKey(string key)
    {
        string[] pairs = [.. RowPairs().Where(pair => !pair.StartsWith($"\"{key}\"", StringComparison.Ordinal))];
        using var json = Utf8Stream($"[{{ {string.Join(", ", pairs)} }}]");

        Assert.That(() => EmbeddedReleases<UnrealRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains($"'{key}'"));
    }

    // A null or a value of another type throws, so a row never reads with a value its type doesn't allow.
    [TestCase("\"app\": \"APP_1.2\"", "\"app\": null")]
    [TestCase("\"build\": \"1.2.3-4\"", "\"build\": null")]
    [TestCase("\"sha1\": \"0000000000000000000000000000000000000001\"", "\"sha1\": null")]
    [TestCase("\"installBytes\": 5", "\"installBytes\": null")]
    [TestCase("\"installBytes\": 5", "\"installBytes\": \"5\"")]
    [TestCase("\"installBytes\": 5", "\"installBytes\": 1.5")]
    [TestCase("\"installBytes\": 5", "\"installBytes\": 9223372036854775808")]
    [TestCase("\"installTags\": true", "\"installTags\": 1")]
    [TestCase("\"installTags\": true", "\"installTags\": null")]
    [TestCase("\"players\": [\"win-x64-shipping\"]", "\"players\": null")]
    [TestCase("\"players\": [\"win-x64-shipping\"]", "\"players\": \"win-x64-shipping\"")]
    [TestCase("\"players\": [\"win-x64-shipping\"]", "\"players\": [5]")]
    [TestCase("\"editorFlags\": []", "\"editorFlags\": null")]
    [TestCase("\"packageFlags\": []", "\"packageFlags\": null")]
    [TestCase("\"pathPrepend\": null", "\"pathPrepend\": 5")]
    [TestCase("\"runtimes\": \"engine\"", "\"runtimes\": \"disk\"")]
    [TestCase("\"runtimes\": \"engine\"", "\"runtimes\": null")]
    [TestCase("\"runtimes\": \"engine\"", "\"runtimes\": 1")]
    public void Read_ValueOfTheWrongType_ThrowsWithTheFile(string pair, string other)
    {
        using var json = Utf8Stream(Row().Replace(pair, other, StringComparison.Ordinal));

        Assert.That(() => EmbeddedReleases<UnrealRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("releases.json"));
    }

    // A version that isn't major.minor.hotfix throws at load with the value, because Find reads the minor from every row.
    [TestCase("1")]
    [TestCase("1.2")]
    [TestCase("1.2.3.4")]
    [TestCase("01.2.3")]
    [TestCase(" 1.2.3")]
    [TestCase("a.b.c")]
    [TestCase("")]
    public void Read_BadVersionShape_ThrowsWithTheValue(string version)
    {
        using var json = Utf8Stream(Row().Replace("1.2.3", version, StringComparison.Ordinal));

        Assert.That(() => EmbeddedReleases<UnrealRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains($"'{version}'"));
    }

    // A build that is only the version throws, because a real build always has a changelist after the version.
    [Test]
    public void Read_BuildWithoutAChangelist_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row().Replace("1.2.3-4", "1.2.3", StringComparison.Ordinal));

        Assert.That(() => EmbeddedReleases<UnrealRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("build '1.2.3'"));
    }

    // A null or an empty switch throws at load with the version, because the JSON options refuse a null list but not a null in a list.
    [TestCase("\"editorFlags\": []", "\"editorFlags\": [null]")]
    [TestCase("\"editorFlags\": []", "\"editorFlags\": [\"\"]")]
    [TestCase("\"packageFlags\": []", "\"packageFlags\": [\"-a\", \" \"]")]
    public void Read_EmptySwitch_ThrowsWithTheVersion(string pair, string other)
    {
        using var json = Utf8Stream(Row().Replace(pair, other, StringComparison.Ordinal));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("empty switch").And.Message.Contains("'1.2.3'"));
    }

    private static string Row()
    {
        return $"[{{ {string.Join(", ", RowPairs())} }}]";
    }

    private static string[] RowPairs()
    {
        return
        [
            "\"version\": \"1.2.3\"",
            "\"app\": \"APP_1.2\"",
            "\"build\": \"1.2.3-4\"",
            "\"sha1\": \"0000000000000000000000000000000000000001\"",
            "\"installBytes\": 5",
            "\"installTags\": true",
            "\"players\": [\"win-x64-shipping\"]",
            "\"editorFlags\": []",
            "\"packageFlags\": []",
            "\"pathPrepend\": null",
            "\"runtimes\": \"engine\"",
        ];
    }

    private static MemoryStream Utf8Stream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
