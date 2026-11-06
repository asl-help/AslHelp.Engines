using NUnit.Framework;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class PlayerFolderTests
{
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
}
