using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System.IO;
using System.Text;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkProjectEdgeCaseTests
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

    // A write to a DefaultEngineUDK.ini.partial that is open without sharing fails before the move, so the ini keeps the shipped lines.
    [Test]
    [Platform("Win")]
    public void AddFixturePackage_PartialIniOpenWithoutSharing_ThrowsIOExceptionAndKeepsTheIni()
    {
        string install = WriteInstall(_folder, DefaultIni);
        WriteBytes(install, "UDKGame/Config/DefaultEngineUDK.ini.partial", [9]);
        string partialPath = IniPath(install) + ".partial";
        string classes = _folder.NewFolder();

        using (new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(
                () => UdkProject.AddFixturePackage(install, classes),
                Throws.TypeOf<IOException>().With.Message.Contains(partialPath));
        }

        Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(DefaultIni)));
    }

    // An ini open without delete sharing fails the move, so the message has both paths and the ini keeps its lines.
    [Test]
    [Platform("Win")]
    public void AddFixturePackage_IniOpenWithoutDeleteSharing_ThrowsIOExceptionWithBothPaths()
    {
        string install = WriteInstall(_folder, DefaultIni);
        string classes = _folder.NewFolder();

        using (new FileStream(IniPath(install), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.That(
                () => UdkProject.AddFixturePackage(install, classes),
                Throws.TypeOf<IOException>()
                    .With.Message.Contains($"'{IniPath(install)}.partial'")
                    .And.Message.Contains($"'{IniPath(install)}'"));
        }

        Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(DefaultIni)));
    }
}
