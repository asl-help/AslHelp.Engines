using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkRuntimesTests
{
    // Set FIXTURES_UDK_INSTALL to an extracted UDK install to add the runtimes to it. The test changes that install.
    private const string InstallVariable = "FIXTURES_UDK_INSTALL";

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

    // A UDK.exe that imports D3DCOMPILER_43.dll, d3dx9_43.dll and KERNEL32.dll gives those 2 runtimes and no d3dcsx_43.dll, so
    // the set comes from the install.
    [Test]
    public void FindImports_ImportsWithoutD3dcsx_OmitsIt()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "D3DCOMPILER_43.dll", "d3dx9_43.dll", "KERNEL32.dll");

        Assert.That(UdkRuntimes.FindImports(Binaries(install)), Is.EqualTo(["D3DCOMPILER_43.dll", "d3dx9_43.dll"]));
    }

    // UDK.exe imports file.dll from Binaries/Win32, which imports xinput1_3.dll, so the search goes through the DLLs of the folder.
    [Test]
    public void FindImports_ImportThroughDllInBinaries_FollowsIt()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "file.dll");
        FakeExecutable.Write(Path.Combine(Binaries(install), "file.dll"), "xinput1_3.dll");

        Assert.That(UdkRuntimes.FindImports(Binaries(install)), Is.EqualTo(["xinput1_3.dll"]));
    }

    // MSVCR100.dll from UDK.exe and msvcr100.dll from file.dll count once, because the names compare without case.
    [Test]
    public void FindImports_SameNameInTwoSpellings_ListsItOnce()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "MSVCR100.dll", "file.dll");
        FakeExecutable.Write(Path.Combine(Binaries(install), "file.dll"), "msvcr100.dll");

        Assert.That(UdkRuntimes.FindImports(Binaries(install)), Is.EqualTo(["MSVCR100.dll"]));
    }

    // A system import that is not in Binaries/Win32 is skipped and a DLL that nothing imports is not read, so only UDK.exe and
    // the DLLs it reaches count.
    [Test]
    public void FindImports_SystemImportMissingFromBinaries_SkipsIt()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "KERNEL32.dll");
        FakeExecutable.Write(Path.Combine(Binaries(install), "file.dll"), "xinput1_3.dll");

        Assert.That(UdkRuntimes.FindImports(Binaries(install)), Is.Empty);
    }

    // msvcrt.dll and MSVCP60.dll are system CRTs and not in Binaries/Win32, so they are skipped like any system DLL.
    [Test]
    public void FindImports_SystemCrtImports_SkipsThem()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "msvcrt.dll", "MSVCP60.dll", "d3dx9_43.dll");

        Assert.That(UdkRuntimes.FindImports(Binaries(install)), Is.EqualTo(["d3dx9_43.dll"]));
    }

    // A Binaries/Win32 without UDK.exe throws with its path and the UDKGame layout in the message.
    [Test]
    public void FindImports_MissingServer_ThrowsFileNotFoundException()
    {
        string binaries = Binaries(_folder.NewFolder());

        Assert.That(
            () => UdkRuntimes.FindImports(binaries),
            Throws.TypeOf<FileNotFoundException>()
                .With.Message.Contains(Path.Combine(binaries, "UDK.exe"))
                .And.Message.Contains("run the build again"));
    }

    // UDK.exe imports d3dx9_43.dll, which is in Binaries/Win32 already. The install has no redist, so the call reads nothing and writes nothing.
    [Test]
    public async Task AddAsync_EveryImportPresent_ReadsNoRedist()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteBytes(install, "Binaries/Win32/d3dx9_43.dll", [1]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFileSystemEntries(install), Is.EqualTo([Path.Combine(install, "Binaries")]));
            Assert.That(Directory.GetFiles(Binaries(install)), Has.Length.EqualTo(2));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1 }));
        }
    }

    // A Runtimes.partial folder left by an earlier run is deleted when every DLL is present, because the install becomes the shared player.
    [Test]
    public async Task AddAsync_EveryImportPresentAndLeftoverTempFolder_DeletesTheFolder()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteBytes(install, "Binaries/Win32/d3dx9_43.dll", [1]);
        WriteBytes(install, "Binaries/Redist/Runtimes.partial/d3dx9_43.dll", [9]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(install, "Binaries", "Redist", "Runtimes.partial")), Is.False);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1 }));
        }
    }

    // A leftover temp file that is open without delete sharing fails only the quiet delete, so an install that has every DLL still passes.
    [Test]
    [Platform("Win")]
    public async Task AddAsync_EveryImportPresentAndTempFileInUse_Passes()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteBytes(install, "Binaries/Win32/d3dx9_43.dll", [1]);
        WriteBytes(install, "Binaries/Redist/Runtimes.partial/file.bin", [9]);
        string leftover = Path.Combine(install, "Binaries", "Redist", "Runtimes.partial", "file.bin");

        using (new FileStream(leftover, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThatAsync(() => UdkRuntimes.AddAsync(install, CancellationToken.None), Throws.Nothing);
        }
    }

    // A UDK.exe with only system imports needs no runtime, so an install without a redist passes.
    [Test]
    public async Task AddAsync_NoRuntimeImports_ReadsNoRedist()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "KERNEL32.dll");

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        Assert.That(Directory.GetFiles(Binaries(install)), Has.Length.EqualTo(1));
    }

    // A missing d3dx9_43.dll with neither redist throws with both redist paths and the DLL, so the message shows which file to put back.
    [Test]
    public void AddAsync_ImportMissingAndNoRedist_ThrowsFileNotFoundException()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        string redist = Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe");
        string windowsRedist = Path.Combine(install, "Binaries", "Windows", "UE3Redist.exe");

        Assert.That(
            async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
            Throws.TypeOf<FileNotFoundException>()
                .With.Property(nameof(FileNotFoundException.FileName)).EqualTo(redist)
                .And.Message.Contains(redist)
                .And.Message.Contains(windowsRedist)
                .And.Message.Contains("d3dx9_43.dll"));
    }

    // A missing DLL that no x86 cabinet lists throws with the DLL and the redist before any extraction, so Binaries/Win32 stays
    // as it was and no temp folder is left.
    [Test]
    public void AddAsync_ImportInNoCabinet_ThrowsInvalidOperationExceptionAndKeepsBinaries()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        string redist = Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe");
        WriteZip(redist, ("DXRedistCutdown/Jun2010_XAudio_x86.cab", FakeCabinet.Names("XAudio2_7.dll")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("d3dx9_43.dll").And.Message.Contains(redist));
            Assert.That(Directory.GetFiles(Binaries(install)), Has.Length.EqualTo(1));
            Assert.That(Directory.Exists(Path.Combine(install, "Binaries", "Redist", "Runtimes.partial")), Is.False);
        }
    }

    // A real cabinet from makecab with d3dx9_43.dll and an inf gives only that DLL in Binaries/Win32 byte for byte and leaves
    // no temp folder, so the expand path works.
    [Test]
    [Platform("Win")]
    public async Task AddAsync_ImportInCabinet_ExpandsTheCabinetAndCopiesTheDll()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteDirectXRedist(_folder, install, "d3dx9_43.dll", [1, 2, 3]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(Directory.GetFiles(Binaries(install)), Has.Length.EqualTo(2));
            Assert.That(Directory.Exists(Path.Combine(install, "Binaries", "Redist", "Runtimes.partial")), Is.False);
        }
    }

    // A longer d3dx9_43.dll.partial from a crashed run is copied over and moved to d3dx9_43.dll, so only the full DLL is left.
    [Test]
    [Platform("Win")]
    public async Task AddAsync_LeftoverPartialDll_LeavesOnlyTheFullDll()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll");
        WriteBytes(install, "Binaries/Win32/d3dx9_43.dll.partial", [9, 9, 9, 9, 9]);
        WriteDirectXRedist(_folder, install, "d3dx9_43.dll", [1, 2, 3]);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(Binaries(install), "d3dx9_43.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(File.Exists(Path.Combine(Binaries(install), "d3dx9_43.dll.partial")), Is.False);
            Assert.That(Directory.GetFiles(Binaries(install)), Has.Length.EqualTo(2));
        }
    }

    // The Visual C++ 2005 and 2008 DLLs are parts of Windows, so a UDK.exe that imports them needs no redist and gets no copy.
    [TestCase("MSVCR80.dll")]
    [TestCase("msvcp80.dll")]
    [TestCase("MSVCR90.dll")]
    [TestCase("msvcp90.dll")]
    public async Task AddAsync_ImportsRuntimeOfWindows_CopiesNothing(string runtime)
    {
        string install = _folder.NewFolder();
        WriteServer(install, runtime);

        await UdkRuntimes.AddAsync(install, CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(Binaries(install), runtime)), Is.False);
    }

    // A UDK.exe that imports MSVCR110.dll is refused, because Windows does not ship it and the tool does not extract it.
    [Test]
    public void AddAsync_ImportsMsvcr110_ThrowsWithTheDll()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "MSVCR110.dll");

        Assert.That(
            async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("MSVCR110.dll")
                .And.Message.Contains("does not extract"));
    }

    // A missing d3dx9_43.dll and msvcr100.dll with a cabinet for the 1st and no Visual C++ installer throws before expand.exe
    // runs, so Binaries/Win32 stays as it was.
    [Test]
    public void AddAsync_DirectXInCabinetAndNoVcInstaller_ThrowsBeforeAnyExtraction()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "d3dx9_43.dll", "msvcr100.dll");
        byte[] cabinet = FakeCabinet.Names("d3dx9_43.dll");
        WriteZip(Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe"), ("DXRedistCutdown/Jun2010_d3dx9_43_x86.cab", cabinet));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains(Vc2010Entry));
            Assert.That(Directory.GetFiles(Binaries(install)), Has.Length.EqualTo(1));
        }
    }

    // A missing msvcr100.dll needs the Visual C++ 2010 installer of the redist, so a redist without it throws with that entry in the message.
    [Test]
    public void AddAsync_VcImportMissingAndNoVcInstaller_ThrowsWithTheEntry()
    {
        string install = _folder.NewFolder();
        WriteServer(install, "msvcr100.dll");
        WriteZip(Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe"), ("file.bin", [1]));

        Assert.That(
            async () => await UdkRuntimes.AddAsync(install, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(Vc2010Entry));
    }

    // A real install gets every runtime that UDK.exe and its DLLs import in Binaries/Win32 from its own UE3Redist.exe.
    [Test]
    public async Task AddAsync_RealInstall_PlacesEveryRuntime()
    {
        string? install = Environment.GetEnvironmentVariable(InstallVariable);
        Assume.That(install is not null && Directory.Exists(install), $"Set {InstallVariable} to an extracted install to run this test.");

        await UdkRuntimes.AddAsync(install!, CancellationToken.None);

        string binaries = Binaries(install!);
        var runtimes = UdkRuntimes.FindImports(binaries);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(runtimes, Is.Not.Empty);

            foreach (string runtime in runtimes)
            {
                Assert.That(File.Exists(Path.Combine(binaries, runtime)), Is.True, runtime);
            }
        }
    }
}
