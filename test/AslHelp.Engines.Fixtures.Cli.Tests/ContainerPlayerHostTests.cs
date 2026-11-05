using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class ContainerPlayerHostTests
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

    // The container mounts the install, starts in the folder of the executable and gets each argument on its own.
    [Test]
    public void NewContainerParameters_InstallAndArguments_MountsTheInstallAndRunsTheExecutable()
    {
        string install = Path.Combine(_folder.NewFolder(), "a b");

        var parameters = ContainerPlayerHost.NewContainerParameters(install, @"Binaries\Win32\Game.exe", "server map?x=1 -flag");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters.Image, Is.EqualTo(ContainerPlayerHost.Image));
            Assert.That(parameters.HostConfig?.Binds?[0], Is.EqualTo($@"{install}:C:\player"));
            Assert.That(parameters.WorkingDir, Is.EqualTo(@"C:\player\Binaries\Win32"));
            Assert.That(string.Join('|', parameters.Cmd), Is.EqualTo(@"C:\player\Binaries\Win32\Game.exe|server|map?x=1|-flag"));
        }
    }

    // A file that a later step creates in the install gets a rule for this user, so the build can delete and move what the container writes.
    [Test]
    [Platform("Win")]
    public void GrantCurrentUser_NewFileInSubfolder_InheritsTheRule()
    {
        string install = _folder.NewFolder();
        string subfolder = Directory.CreateDirectory(Path.Combine(install, "sub")).FullName;

        ContainerPlayerHost.GrantCurrentUser(install);

        string file = Path.Combine(subfolder, "new.txt");
        File.WriteAllText(file, "x");
        var rules = new FileInfo(file).GetAccessControl().GetAccessRules(includeExplicit: false, includeInherited: true, typeof(SecurityIdentifier));

        Assert.That(
            rules.Cast<FileSystemAccessRule>().Any(rule =>
                rule.IdentityReference == WindowsIdentity.GetCurrent().User && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)),
            Is.True);
    }

    // An empty argument string adds nothing after the executable, because the engine would pass an empty argument on.
    [Test]
    public void NewContainerParameters_NoArguments_RunsOnlyTheExecutable()
    {
        var parameters = ContainerPlayerHost.NewContainerParameters(_folder.NewFolder(), "Game.exe", "");

        Assert.That(string.Join('|', parameters.Cmd), Is.EqualTo(@"C:\player\Game.exe"));
    }
}
