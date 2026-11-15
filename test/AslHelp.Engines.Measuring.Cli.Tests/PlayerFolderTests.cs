using NUnit.Framework;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class PlayerFolderTests
{
    private const string Hash = "0123456789abcdef";

    // The list the fixtures tool pins for dated versions, in the order of their releases.
    private static readonly string[] _datedVersions =
    [
        "2009-11-BETA",
        "2010-06-BETA",
        "2010-07-BETA",
        "2010-08-BETA",
        "2010-09",
        "2010-10-BETA",
        "2010-11-BETA",
        "2010-12-BETA",
        "2011-01-BETA",
        "2011-02-BETA",
        "2011-03-BETA",
        "2011-03-BETA2",
        "2011-05-BETA",
        "2011-07-BETA",
        "2011-10-BETA",
        "2011-11-BETA",
        "2011-12-BETA",
        "2012-01-BETA",
        "2012-02-BETA",
        "2012-05-BETA",
        "2012-07-BETA2",
        "2012-11-BETA",
        "2013-02-BETA",
        "2013-02-BETA2",
        "2013-07",
        "2014-02",
        "2015-02",
    ];

    private static readonly string[] _naturalVersions =
    [
        "4.2.8-win-x64-shipping",
        "4.9.2-win-x64-shipping",
        "4.10.4-win-x64-shipping",
        "4.19.2-win-x64-development",
        "4.19.2-win-x64-shipping",
        "5.0.3-win-x64-shipping",
        "5.8.1-win-x64-shipping",
    ];

    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // Only an <engine>-<version>-<16 hex chars> folder is a player, so an install or an archive beside it is skipped.
    [Test]
    public void Find_MixedFolder_ReturnsOnlyThePlayersOfTheEngine()
    {
        string[] folders =
        [
            "engine-2011-03-BETA2-957465e9d38d3bd6",
            "engine-2015-02-bb9a10ba945f098b",
            "engine-2015-02-short",
            "engine-bb9a10ba945f098b",
            "other-2015-02-bb9a10ba945f098b",
            "toolchain-2015-02",
        ];

        foreach (string folder in folders)
        {
            _ = Directory.CreateDirectory(Path.Combine(_folder.Root, folder));
        }

        File.WriteAllText(Path.Combine(_folder.Root, "engine-2015-03-bb9a10ba945f098b"), "");

        var found = PlayerFolder.Find(_folder.Root, "engine");

        string[] expected = ["2011-03-BETA2", "2015-02"];

        Assert.That(found.Select(static player => player.Version), Is.EqualTo(expected));
    }

    // A relative players folder comes back as a full path, because the player starts with another working folder.
    [Test]
    public void Find_RelativeFolder_ReturnsFullPaths()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "engine-1.0-0123456789abcdef"));
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), _folder.Root);

        var found = PlayerFolder.Find(relative, "engine");

        Assert.That(found[0].Directory, Is.EqualTo(Path.Combine(_folder.Root, "engine-1.0-0123456789abcdef")));
    }

    // A minor past 9 comes after 9, because ordinal text puts 4.10 before 4.2 and the report lists neighboring versions.
    [Test]
    public void Find_MinorsPastNine_OrdersNaturally()
    {
        CreatePlayers(_naturalVersions);

        var found = PlayerFolder.Find(_folder.Root, "engine");

        Assert.That(found.Select(static player => player.Version), Is.EqualTo(_naturalVersions));
    }

    // Dated versions keep their order, because their reports compare byte for byte with the earlier runs.
    [Test]
    public void Find_DatedVersions_KeepsTheOrdinalOrder()
    {
        CreatePlayers(_datedVersions);

        var found = PlayerFolder.Find(_folder.Root, "engine");

        Assert.That(found.Select(static player => player.Version), Is.EqualTo(_datedVersions));
    }

    // A version with dashes stays whole, because only the last part of the folder is the hash.
    [Test]
    public void Find_VersionWithDashes_KeepsTheWholeId()
    {
        CreatePlayers("4.19.2-win-x64-shipping");

        var found = PlayerFolder.Find(_folder.Root, "engine");

        Assert.That(found[0].Version, Is.EqualTo("4.19.2-win-x64-shipping"));
    }

    private void CreatePlayers(params string[] versions)
    {
        foreach (string version in versions.Reverse())
        {
            _ = Directory.CreateDirectory(Path.Combine(_folder.Root, $"engine-{version}-{Hash}"));
        }
    }
}
