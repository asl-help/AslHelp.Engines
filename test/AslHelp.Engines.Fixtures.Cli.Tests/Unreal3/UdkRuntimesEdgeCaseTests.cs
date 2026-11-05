using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkRuntimesEdgeCaseTests
{
    private const string Vc2010Entry = "vcredist_x86_vs2010sp1.exe";

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

    // The next run after a kill deletes the old Runtimes.partial and overwrites the cut d3dx9_43.dll.partial, so only the full DLL is left.
    [Test]
    [Platform("Win")]
    public async Task AddAsync_LeftoversOfKilledRun_CopiesTheFullDll()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteBytes(install, "Binaries/Win32/d3dx9_43.dll.partial", [9]);
        WriteBytes(install, "Binaries/Redist/Runtimes.partial/Jun2010_d3dx9_43_x86.cab", [9]);
        WriteBytes(install, "Binaries/Redist/Runtimes.partial/d3dx9_43.dll", [9, 9]);
        WriteDirectXRedist(_folder, install, "d3dx9_43.dll", [1, 2, 3]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        string[] expected = [Path.Combine(Binaries(install), "UDK.exe"), Path.Combine(Binaries(install), "d3dx9_43.dll")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(Binaries(install)), Is.EquivalentTo(expected));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(Directory.Exists(Path.Combine(install, "Binaries", "Redist", "Runtimes.partial")), Is.False);
        }
    }

    // A copy that fails on a d3dx9_43.dll.partial open without sharing leaves d3dx9_43.dll missing, so the next run copies the DLL again.
    [Test]
    [Platform("Win")]
    public void AddAsync_PartialDllOpenWithoutSharing_ThrowsIOExceptionAndLeavesNoDll()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteBytes(install, "Binaries/Win32/d3dx9_43.dll.partial", [9]);
        WriteDirectXRedist(_folder, install, "d3dx9_43.dll", [1, 2, 3]);
        string partialPath = Path.Combine(Binaries(install), "d3dx9_43.dll.partial");

        using (new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(
                async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
                Throws.TypeOf<IOException>().With.Message.Contains(partialPath));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(install, "Binaries", "Redist", "Runtimes.partial")), Is.False);
        }
    }

    // A folder at the d3dx9_43.dll path fails the move of the complete copy, so the message has both paths.
    [Test]
    [Platform("Win")]
    public void AddAsync_FolderAtDllPath_ThrowsIOExceptionWithBothPaths()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteDirectXRedist(_folder, install, "d3dx9_43.dll", [1, 2, 3]);
        string runtimePath = Path.Combine(Binaries(install), "d3dx9_43.dll");
        _ = Directory.CreateDirectory(runtimePath);

        Assert.That(
            async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
            Throws.TypeOf<IOException>()
                .With.Message.Contains($"'{runtimePath}.partial'")
                .And.Message.Contains($"'{runtimePath}'"));
    }

    // A cabinet that spells D3DX9_43.DLL gives d3dx9_43.dll in Binaries/Win32, because the .partial copy moves to the importer's spelling.
    [Test]
    [Platform("Win")]
    public async Task AddAsync_CabinetSpellsDllInOtherCase_CopiesUnderTheImportersSpelling()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteDirectXRedist(_folder, install, "D3DX9_43.DLL", [1, 2, 3]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        string[] expected = [Path.Combine(Binaries(install), "UDK.exe"), Path.Combine(Binaries(install), "d3dx9_43.dll")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(Binaries(install)), Is.EquivalentTo(expected));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
    }

    // An install path with a space, a comma, a semicolon and an ampersand gets the DLL, because each path reaches expand.exe as 1 argument.
    [Test]
    [Platform("Win")]
    public async Task AddAsync_InstallPathWithSpaceAndPunctuation_CopiesTheDll()
    {
        string install = Path.Combine(_folder.NewFolder(), "a b,c;d&e");
        WriteServer(install, "d3dx9_43.dll");
        WriteDirectXRedist(_folder, install, "d3dx9_43.dll", [1, 2, 3]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        string[] expected = [Path.Combine(Binaries(install), "UDK.exe"), Path.Combine(Binaries(install), "d3dx9_43.dll")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(Binaries(install)), Is.EquivalentTo(expected));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
    }

    // expand.exe does not unpack a 1-file cabinet into the cabinet's own folder, so xinput1_3.dll fails the check before d3dx9_43.dll is copied.
    [Test]
    [Platform("Win")]
    public void AddAsync_DllNotUnpacked_ThrowsBeforeTheFirstCopy()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll", "xinput1_3.dll");
        string redist = Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe");
        byte[] d3dx9Cabinet = FakeCabinet.Make(_folder, ("d3dx9_43.dll", [1, 2, 3]), ("d3dx9_43_x86.inf", [4]));
        byte[] xinputCabinet = FakeCabinet.Make(_folder, ("xinput1_3.dll", [5]));
        WriteZip(
            redist,
            ("DXRedistCutdown/Jun2010_d3dx9_43_x86.cab", d3dx9Cabinet),
            ("DXRedistCutdown/APR2007_xinput_x86.cab", xinputCabinet));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains(redist).And.Message.Contains("xinput1_3.dll"));
            Assert.That(Directory.GetFiles(Binaries(install)), Is.EqualTo([Path.Combine(Binaries(install), "UDK.exe")]));
            Assert.That(Directory.Exists(Path.Combine(install, "Binaries", "Redist", "Runtimes.partial")), Is.False);
        }
    }

    // tree.com as the Visual C++ installer exits with 0 but does not write vc_red.cab, so the expand.exe step after it throws with the cabinet path.
    [Test]
    [Platform("Win")]
    public void AddAsync_VcInstallerWritesNoCabinet_ThrowsWithTheCabinetPath()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "msvcr100.dll");
        byte[] installer = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "tree.com"));
        WriteZip(Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe"), (Vc2010Entry, installer));
        string tempDirectory = Path.Combine(install, "Binaries", "Redist", "Runtimes.partial");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains(Path.Combine(tempDirectory, "vc_red.cab")));
            Assert.That(Directory.GetFiles(Binaries(install)), Is.EqualTo([Path.Combine(Binaries(install), "UDK.exe")]));
            Assert.That(Directory.Exists(tempDirectory), Is.False);
        }
    }
}
