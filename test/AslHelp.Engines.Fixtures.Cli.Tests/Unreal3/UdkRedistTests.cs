using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkRedistTests
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

    // Binaries/Redist/UE3Redist.exe wins over Binaries/Windows/UE3Redist.exe, the layout of 2011-05 and later.
    [Test]
    public void FindPath_RedistAndWindowsPresent_ReturnsRedist()
    {
        string install = _folder.NewFolder();
        WriteBytes(install, "Binaries/Redist/UE3Redist.exe", [1]);
        WriteBytes(install, "Binaries/Windows/UE3Redist.exe", [1]);

        Assert.That(UdkRedist.FindPath(install), Is.EqualTo(Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe")));
    }

    // An install with only Binaries/Windows/UE3Redist.exe, the layout before 2011-05, is found.
    [Test]
    public void FindPath_OnlyWindowsPresent_ReturnsWindows()
    {
        string install = _folder.NewFolder();
        WriteBytes(install, "Binaries/Windows/UE3Redist.exe", [1]);

        Assert.That(UdkRedist.FindPath(install), Is.EqualTo(Path.Combine(install, "Binaries", "Windows", "UE3Redist.exe")));
    }

    // An install with neither file gives null, so the caller throws its own error for a missing redist.
    [Test]
    public void FindPath_NoRedist_ReturnsNull()
    {
        Assert.That(UdkRedist.FindPath(_folder.NewFolder()), Is.Null);
    }

    // A Binaries/Windows/UE3Redist.exe with the PhysX installer throws with the version.
    [Test]
    public void ThrowIfNeedsPhysXInstaller_RedistWithPhysXInstaller_ThrowsWithTheVersion()
    {
        string install = _folder.NewFolder();
        WriteZip(Path.Combine(install, "Binaries", "Windows", "UE3Redist.exe"), ("PhysX/PhysX_8.10.29_SystemSoftware.exe", [1]));

        Assert.That(
            () => UdkRedist.ThrowIfNeedsPhysXInstaller(install, "1.0"),
            Throws.InvalidOperationException
                .With.Message.Contains("UDK 1.0")
                .And.Message.Contains("PhysX"));
    }

    // An install without a redist passes, because the runtime step reports a missing redist only if it needs one.
    [Test]
    public void ThrowIfNeedsPhysXInstaller_NoRedist_Passes()
    {
        string install = _folder.NewFolder();

        Assert.That(() => UdkRedist.ThrowIfNeedsPhysXInstaller(install, "1.0"), Throws.Nothing);
    }

    // A redist with vcredist_x86_vs2008sp1.exe has the Visual C++ 2008 installer.
    [Test]
    public void Open_RedistWithVc2008Entry_HasVc2008()
    {
        string path = WriteRedist(("vcredist_x86_vs2008sp1.exe", [1]));

        using var redist = UdkRedist.Open(path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(redist.HasVc2008, Is.True);
            Assert.That(redist.HasVc2010, Is.False);
        }
    }

    // A redist with only vcredist_x86_vs2010sp1.exe has the Visual C++ 2010 installer and no Visual C++ 2008 installer.
    [Test]
    public void Open_RedistWithVc2010Entry_HasVc2010AndNoVc2008()
    {
        string path = WriteRedist(("vcredist_x86_vs2010sp1.exe", [1]));

        using var redist = UdkRedist.Open(path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(redist.HasVc2008, Is.False);
            Assert.That(redist.HasVc2010, Is.True);
            Assert.That(redist.FilePath, Is.EqualTo(path));
        }
    }

    // A zip whose end record is intact but whose central directory is damaged throws and closes the file, so the redist can be deleted at once.
    [Test]
    public void Open_DamagedCentralDirectory_ThrowsAndClosesTheFile()
    {
        string path = WriteRedist(("file.bin", [1]));
        byte[] bytes = File.ReadAllBytes(path);
        int central = bytes.AsSpan().LastIndexOf("PK\u0001\u0002"u8);
        bytes[central + 2] = 0xFF;
        File.WriteAllBytes(path, bytes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => UdkRedist.Open(path).Dispose(), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => File.Delete(path), Throws.Nothing);
        }
    }

    // A damaged x86 cabinet throws with the redist, the entry and the fix once, because the cabinet reader gives only the format fact.
    [Test]
    public void FindCabinet_DamagedCabinet_ThrowsWithRedistEntryAndFixOnce()
    {
        byte[] cabinet = FakeCabinet.Names("file.dll");
        cabinet[0] = (byte)'X';
        string path = WriteRedist(("DXRedistCutdown/file_x86.cab", cabinet));

        using var redist = UdkRedist.Open(path);

        var exception = Assert.Throws<InvalidDataException>(() => redist.FindCabinet("file.dll"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain(path).And.Contains("DXRedistCutdown/file_x86.cab"));
            Assert.That(exception.Message.Split("run the build again"), Has.Length.EqualTo(2));
        }
    }

    // With 2 x86 cabinets that list different names, the DLL of the 2nd one maps to the 2nd entry.
    [Test]
    public void FindCabinet_NameInSecondCabinet_ReturnsThatEntry()
    {
        string path = WriteRedist(
            ("DXRedistCutdown/file_x86.cab", FakeCabinet.Names("file.dll")),
            ("DXRedistCutdown/other_x86.cab", FakeCabinet.Names("other.dll", "other.inf")));

        using var redist = UdkRedist.Open(path);

        Assert.That(redist.FindCabinet("other.dll"), Is.EqualTo("DXRedistCutdown/other_x86.cab"));
    }

    // An import spelled in other case finds the cabinet, because Windows file names ignore case.
    [Test]
    public void FindCabinet_DifferentCase_Matches()
    {
        string path = WriteRedist(("DXRedistCutdown/file_x86.cab", FakeCabinet.Names("File_1.dll")));

        using var redist = UdkRedist.Open(path);

        Assert.That(redist.FindCabinet("FILE_1.DLL"), Is.EqualTo("DXRedistCutdown/file_x86.cab"));
    }

    // A name listed only in a _x64.cab is not mapped, so a 64-bit DLL never reaches Binaries/Win32.
    [Test]
    public void FindCabinet_NameOnlyInX64Cabinet_ReturnsNull()
    {
        string path = WriteRedist(
            ("DXRedistCutdown/file_x64.cab", FakeCabinet.Names("file.dll")),
            ("DXRedistCutdown/DXSETUP.exe", [1]));

        using var redist = UdkRedist.Open(path);

        Assert.That(redist.FindCabinet("file.dll"), Is.Null);
    }

    // A name that no x86 cabinet lists gives null, which the runtime step turns into its message.
    [Test]
    public void FindCabinet_NoCabinetHasTheName_ReturnsNull()
    {
        string path = WriteRedist(("DXRedistCutdown/file_x86.cab", FakeCabinet.Names("file.dll")));

        using var redist = UdkRedist.Open(path);

        Assert.That(redist.FindCabinet("other.dll"), Is.Null);
    }

    // A missing entry throws with the redist path and the entry name.
    [Test]
    public void Extract_MissingEntry_ThrowsInvalidDataExceptionWithRedistAndEntry()
    {
        string path = WriteRedist(("file.bin", [1]));

        using var redist = UdkRedist.Open(path);

        Assert.That(
            () => redist.Extract("DXRedistCutdown/file.cab", _folder.NewFolder()),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path).And.Message.Contains("DXRedistCutdown/file.cab"));
    }

    // An entry under a folder is written under its file name.
    [Test]
    public void Extract_Entry_WritesItUnderItsFileName()
    {
        string path = WriteRedist(("DXRedistCutdown/file.cab", [1, 2, 3]));
        string directory = _folder.NewFolder();

        using (var redist = UdkRedist.Open(path))
        {
            redist.Extract("DXRedistCutdown/file.cab", directory);
        }

        Assert.That(File.ReadAllBytes(Path.Combine(directory, "file.cab")), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    private string WriteRedist(params (string Name, byte[] Bytes)[] entries)
    {
        string path = Path.Combine(_folder.NewFolder(), "UE3Redist.exe");
        WriteZip(path, entries);

        return path;
    }
}
