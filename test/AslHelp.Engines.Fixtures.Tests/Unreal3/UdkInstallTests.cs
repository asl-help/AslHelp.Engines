using AslHelp.Engines.Fixtures.Unreal3;
using NUnit.Framework;
using System.IO;

namespace AslHelp.Engines.Fixtures.Tests.Unreal3;

public sealed class UdkInstallTests
{
    private DirectoryInfo _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Directory.CreateTempSubdirectory("AslHelp.Engines.Fixtures.Tests-");
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete(recursive: true);
    }

    // An install without a map fails with the maps folder before anything starts.
    [Test]
    public void FindMap_NoMap_ThrowsWithTheFolder()
    {
        Assert.That(
            () => UdkInstall.FindMap(_folder.FullName),
            Throws.TypeOf<FileNotFoundException>().With.Message.Contains(_folder.FullName));
    }

    // The 1st map in ordinal order of its relative path comes back, so every run loads the same map.
    [Test]
    public void FindMap_TwoMaps_ReturnsTheFirstWithoutItsExtension()
    {
        string maps = _folder.CreateSubdirectory(@"UDKGame\Content\Maps").FullName;
        File.WriteAllText(Path.Combine(maps, "B.udk"), "");
        File.WriteAllText(Path.Combine(maps, "A.udk"), "");

        Assert.That(UdkInstall.FindMap(_folder.FullName), Is.EqualTo("A"));
    }

    // The arguments have the map where the server takes it.
    [Test]
    public void ServerArguments_Map_FillsTheMap()
    {
        Assert.That(UdkInstall.ServerArguments("A"), Does.StartWith("server A?game=Fixture.FixtureGame "));
    }
}
