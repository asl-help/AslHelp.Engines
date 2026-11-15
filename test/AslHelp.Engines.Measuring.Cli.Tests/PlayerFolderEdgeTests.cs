using NUnit.Framework;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class PlayerFolderEdgeTests
{
    private const string Hash = "0123456789abcdef";

    private static readonly string[] _hotfixes = ["4.19.2-win-x64-shipping", "4.19.10-win-x64-shipping"];

    private static readonly string[] _mixedBuilds =
    [
        "4.9.2-win-x64-development",
        "4.9.2-win-x86-shipping",
        "4.10.4-win-x64-development",
        "4.10.4-win-x64-shipping",
        "4.10.4-win-x86-development",
        "5.0.3-win-x64-development",
    ];

    private static readonly string[] _shortest = ["1"];

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

    // A hotfix past 9 comes after 9, same as a minor.
    [Test]
    public void Find_HotfixesPastNine_OrdersNaturally()
    {
        CreatePlayers(_hotfixes);

        Assert.That(PlayerFolder.Find(_folder.Root, "engine").Select(static player => player.Version), Is.EqualTo(_hotfixes));
    }

    // The version sorts first and the build second, so a report lists the builds of a version next to each other.
    [Test]
    public void Find_MixedBuilds_OrdersByVersionThenBuild()
    {
        CreatePlayers(_mixedBuilds);

        Assert.That(PlayerFolder.Find(_folder.Root, "engine").Select(static player => player.Version), Is.EqualTo(_mixedBuilds));
    }

    // A number past the range of an int compares as digits and doesn't throw.
    [Test]
    public void Find_HugeVersionNumber_DoesNotThrow()
    {
        CreatePlayers("99999999999999999999.1-win-x64-shipping", "4.19.2-win-x64-shipping");

        Assert.That(PlayerFolder.Find(_folder.Root, "engine"), Has.Count.EqualTo(2));
    }

    // A separator at the end of the folder doesn't reach the paths of the players.
    [Test]
    public void Find_SeparatorAtTheEnd_ReturnsPlainPaths()
    {
        CreatePlayers("1.0");

        var found = PlayerFolder.Find(_folder.Root + Path.DirectorySeparatorChar, "engine");

        Assert.That(found[0].Directory, Is.EqualTo(Path.Combine(_folder.Root, $"engine-1.0-{Hash}")));
    }

    // The shortest version is 1 character, because the hash and both dashes take the rest of the folder.
    [Test]
    public void Find_OneCharacterVersion_ReturnsIt()
    {
        CreatePlayers("1");

        Assert.That(PlayerFolder.Find(_folder.Root, "engine").Select(static player => player.Version), Is.EqualTo(_shortest));
    }

    // A folder without a version between the engine and the hash or without a hash of lowercase hex isn't a player.
    [TestCase("engine--0123456789abcdef")]
    [TestCase("engine-0123456789abcdef")]
    [TestCase("engine-1.0-0123456789ABCDEF")]
    [TestCase("engine-1.0-0123456789abcdeg")]
    [TestCase("engine-1.0-0123456789abcde")]
    [TestCase("engine-1.0_0123456789abcdef")]
    [TestCase("engine")]
    [TestCase("engine-")]
    [TestCase("engines-1.0-0123456789abcdef")]
    [TestCase("ENGINE-1.0-0123456789abcdef")]
    public void Find_FolderOutsideTheForm_LeavesItOut(string folder)
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, folder));

        Assert.That(PlayerFolder.Find(_folder.Root, "engine"), Is.Empty);
    }

    private void CreatePlayers(params string[] versions)
    {
        foreach (string version in versions.Reverse())
        {
            _ = Directory.CreateDirectory(Path.Combine(_folder.Root, $"engine-{version}-{Hash}"));
        }
    }
}
