using AslHelp.Engines.Fixtures.Cli.Unreal4;
using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealReleaseTests
{
    private static readonly string[] _allPlayers = ["win-x64-shipping", "win-x64-development", "win-x86-shipping", "win-x86-development"];

    // The file has every minor from 4.0 to 5.8 once in numeric order, so 4.9 comes before 4.10.
    [Test]
    public void All_Rows_Has37InVersionOrderWithUniquePins()
    {
        Version[] versions = [.. EmbeddedReleases<UnrealRelease>.All.Select(row => Version.Parse(row.Version))];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(versions, Has.Length.EqualTo(37));
            Assert.That(versions, Is.Ordered.Ascending);
            Assert.That(versions.Select(version => (version.Major, version.Minor)), Is.Unique);
            Assert.That(EmbeddedReleases<UnrealRelease>.All.Select(row => row.Sha1), Is.Unique);
            Assert.That(EmbeddedReleases<UnrealRelease>.All.Select(row => row.App), Is.Unique);
        }
    }

    // Every app is UE_ and the minor, so a row with the app of another minor fails CI.
    [Test]
    public void All_EveryRow_AppIsUeAndTheMinor()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UnrealRelease>.All)
            {
                Assert.That(row.App, Is.EqualTo($"UE_{row.Minor}"), row.Version);
            }
        }
    }

    // Only the manifests of 4.0 to 4.9 don't have install tags, so a tag filter on any other row is right.
    [Test]
    public void All_RowsWithoutInstallTags_Are4_0To4_9()
    {
        string[] expected = [.. Enumerable.Range(0, 10).Select(minor => $"4.{minor}")];

        var untagged = EmbeddedReleases<UnrealRelease>.All.Where(row => !row.InstallTags).Select(row => row.Minor);

        Assert.That(untagged, Is.EqualTo(expected));
    }

    // 4.0 and 4.19 have the values of the hand-run builds that passed, so a typo in a proven row fails CI.
    [Test]
    public void All_ProvenRows_HaveTheHandRunValues()
    {
        var first = EmbeddedReleases<UnrealRelease>.Find("4.0");
        var vs2017 = EmbeddedReleases<UnrealRelease>.Find("4.19");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Tags(first), Is.EqualTo(["win-x64-development", "win-x86-shipping"]));
            Assert.That(first.EditorFlags, Is.EqualTo(["-rocket"]));
            Assert.That(first.PackageFlags, Is.EqualTo(["-rocket", "-nobootstrapexe"]));
            Assert.That(first.PathPrepend, Is.Null);
            Assert.That(first.Runtimes, Is.EqualTo(UnrealRuntimeSource.Image));
            Assert.That(Tags(vs2017), Is.EqualTo(_allPlayers));
            Assert.That(vs2017.EditorFlags, Is.EqualTo(["-waitmutex"]));
            Assert.That(vs2017.PackageFlags, Is.Empty);
            Assert.That(vs2017.PathPrepend, Is.EqualTo(@"C:\VS2017\VC\Tools\MSVC\14.16.27023\bin\HostX64\x64"));
            Assert.That(vs2017.Runtimes, Is.EqualTo(UnrealRuntimeSource.Engine));
        }
    }

    // Each row lists a player per arch and config its install has libs for: 2 before 4.8, 4 up to 4.27 and the x64 ones on 5.x.
    [Test]
    public void All_EveryRow_ListsThePlayersItsInstallHasLibsFor()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UnrealRelease>.All)
            {
                var minor = Version.Parse(row.Minor);
                string[] expected = minor < new Version(4, 8) ? ["win-x64-development", "win-x86-shipping"]
                    : minor.Major == 4 ? _allPlayers
                    : ["win-x64-shipping", "win-x64-development"];

                Assert.That(Tags(row), Is.EqualTo(expected), row.Version);
            }
        }
    }

    // Only 4.0 to 4.18 and 4.25 to 4.27 take the runtime DLLs from the image, because the installs of 4.0 to 4.18 don't have the DLLs of
    // AppLocalDependencies and the players of 4.25 import D3DCOMPILER_43, which its AppLocalDependencies doesn't have.
    [Test]
    public void All_RowsWithImageRuntimes_Are40To418And425To427()
    {
        string[] expected = [.. Enumerable.Range(0, 19).Select(minor => $"4.{minor}"), "4.25", "4.26", "4.27"];

        var fromImage = EmbeddedReleases<UnrealRelease>.All.Where(row => row.Runtimes == UnrealRuntimeSource.Image).Select(row => row.Minor);

        Assert.That(fromImage, Is.EqualTo(expected));
    }

    // Every spelling of a minor finds its pinned row.
    [TestCase("4.19")]
    [TestCase("4.19.2")]
    [TestCase("UE_4.19")]
    [TestCase("ue_4.19")]
    public void Find_EverySpelling_ReturnsTheSameRow(string spelling)
    {
        var release = EmbeddedReleases<UnrealRelease>.Find(spelling);

        Assert.That(release.Version, Is.EqualTo("4.19.2"));
    }

    // A minor that starts another minor finds its row, not the first row it prefixes.
    [TestCase("4.1", "4.1.2")]
    [TestCase("4.2", "4.2.1")]
    [TestCase("5.0", "5.0.3")]
    public void Find_MinorThatStartsAnother_ReturnsItsRow(string spelling, string version)
    {
        var release = EmbeddedReleases<UnrealRelease>.Find(spelling);

        Assert.That(release.Version, Is.EqualTo(version));
    }

    // An unpinned version throws with the pinned minors and the file, without a parameter suffix, because the CLI prints the message as is.
    [Test]
    public void Find_UnpinnedVersion_ThrowsWithThePinnedMinors()
    {
        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Find("4.28"),
            Throws.InvalidOperationException
                .With.Message.Contains("4.28")
                .And.Message.Contains("4.0, 4.1")
                .And.Message.Contains("5.8")
                .And.Message.Contains("releases.json")
                .And.Message.Not.Contains("(Parameter"));
    }

    // Another hotfix of a pinned minor throws, so a wrong hotfix never installs the pinned one silently.
    [Test]
    public void Find_OtherHotfixOfAPinnedMinor_ThrowsWithTheVersion()
    {
        Assert.That(() => EmbeddedReleases<UnrealRelease>.Find("4.19.1"), Throws.InvalidOperationException.With.Message.Contains("4.19.1"));
    }

    // A row reads with camelCase keys as in releases.json and the minor comes from the version.
    [Test]
    public void Read_Row_ReadsEveryKey()
    {
        using var json = Utf8Stream(Row(version: "1.2.3", build: "1.2.3-4"));

        var release = EmbeddedReleases<UnrealRelease>.Read(json).Single();
        string[] none = [];
        UnrealPlayer[] noPlayers = [];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                release with { Players = noPlayers, EditorFlags = none, PackageFlags = none },
                Is.EqualTo(new UnrealRelease(
                    "1.2.3",
                    "APP_1.2",
                    "1.2.3-4",
                    "0000000000000000000000000000000000000001",
                    true,
                    noPlayers,
                    none,
                    none,
                    @"C:\x",
                    UnrealRuntimeSource.Engine)));
            Assert.That(Tags(release), Is.EqualTo(["win-x64-shipping"]));
            Assert.That(release.EditorFlags, Is.EqualTo(["-a"]));
            Assert.That(release.PackageFlags, Is.EqualTo(["-b", "-c"]));
            Assert.That(release.Minor, Is.EqualTo("1.2"));
        }
    }

    // A build of another version throws at load with the version, so a row copied from another minor fails every run.
    [Test]
    public void Read_RowWithBuildOfOtherVersion_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row(version: "4.19.2", build: "4.18.3-1"));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("4.19.2")
                .And.Message.Contains("4.18.3-1")
                .And.Message.Contains("releases.json"));
    }

    // A build whose version only starts with the row version throws, because 4.19.2 is not 4.19.21.
    [Test]
    public void Read_RowWithBuildOfLongerVersion_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row(version: "4.19.2", build: "4.19.21-1"));

        Assert.That(() => EmbeddedReleases<UnrealRelease>.Read(json), Throws.TypeOf<InvalidDataException>().With.Message.Contains("4.19.21-1"));
    }

    // 2 hotfixes of 1 minor throw at load with the minor, because the minor and the app would pick only the 1st row.
    [Test]
    public void Read_TwoRowsOfOneMinor_ThrowsWithTheMinor()
    {
        string first = Row().Trim().TrimStart('[').TrimEnd(']');
        string second = Row(version: "1.2.4", build: "1.2.4-5").Trim().TrimStart('[').TrimEnd(']');
        using var json = Utf8Stream($"[{first}, {second}]");

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("'1.2'").And.Message.Contains("releases.json"));
    }

    // A null version fails the parse, so a half-copied row never reads as a null version.
    [Test]
    public void Read_RowWithNullVersion_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row().Replace("\"version\": \"1.2.3\"", "\"version\": null", StringComparison.Ordinal));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>());
    }

    // A misspelled key fails the parse, so it never reads as a missing value.
    [Test]
    public void Read_RowWithUnknownKey_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row().Replace("\"app\"", "\"apps\"", StringComparison.Ordinal));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>().And.Message.Contains("releases.json"));
    }

    // A row without a player throws at load with the file and the version, because an engine without a player builds nothing.
    [Test]
    public void Read_RowWithoutPlayers_ThrowsWithTheFileAndVersion()
    {
        using var json = Utf8Stream(Row(players: ""));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("releases.json").And.Message.Contains("'1.2.3'"));
    }

    // A tag of an arch the tool can't build fails the parse at load with the tag, so no row has a player that isn't one.
    [Test]
    public void Read_RowWithBadPlayer_ThrowsWithTheTag()
    {
        using var json = Utf8Stream(Row(players: "\"win-arm-shipping\""));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>()
                .With.InnerException.TypeOf<JsonException>()
                .And.Message.Contains("'win-arm-shipping'")
                .And.Message.Contains("releases.json"));
    }

    // A null in the players fails the parse at load like a bad tag, so no row has a null player.
    [Test]
    public void Read_RowWithNullPlayer_ThrowsInvalidDataException()
    {
        using var json = Utf8Stream(Row(players: "null"));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.InnerException.TypeOf<JsonException>().And.Message.Contains("releases.json"));
    }

    // A tag listed twice throws at load, because 2 builds of 1 player go to 1 folder.
    [Test]
    public void Read_RowWithRepeatedPlayer_ThrowsWithTheTag()
    {
        using var json = Utf8Stream(Row(players: "\"win-x64-shipping\", \"win-x64-shipping\""));

        Assert.That(
            () => EmbeddedReleases<UnrealRelease>.Read(json),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("'win-x64-shipping'").And.Message.Contains("twice"));
    }

    private static string Row(
        string version = "1.2.3",
        string build = "1.2.3-4",
        string sha1 = "0000000000000000000000000000000000000001",
        string players = "\"win-x64-shipping\"")
    {
        return $$"""
            [{ "version": "{{version}}", "app": "APP_1.2", "build": "{{build}}", "sha1": "{{sha1}}", "installTags": true,
               "players": [{{players}}], "editorFlags": ["-a"], "packageFlags": ["-b", "-c"], "pathPrepend": "C:\\x", "runtimes": "engine" }]
            """;
    }

    private static string[] Tags(UnrealRelease release)
    {
        return [.. release.Players.Select(static player => player.Tag)];
    }

    private static MemoryStream Utf8Stream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
