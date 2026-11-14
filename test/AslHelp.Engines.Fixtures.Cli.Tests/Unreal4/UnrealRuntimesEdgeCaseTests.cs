using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System.IO;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealRuntimesEdgeCaseTests
{
    private string _binaries = null!;
    private TestFolder _folder = null!;
    private string _pool = null!;
    private string _system = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _binaries = _folder.NewFolder();
        _pool = _folder.NewFolder();
        _system = _folder.NewFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // 2 pool DLLs that import each other are both copied once, so a cycle in the imports ends.
    [Test]
    public void Add_PoolDllsImportEachOther_CopiesBoth()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "a.dll");
        FakeExecutable.Write(Path.Combine(_pool, "a.dll"), "b.dll");
        FakeExecutable.Write(Path.Combine(_pool, "b.dll"), "a.dll");

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(
            Directory.GetFiles(_binaries, "*.dll"),
            Is.EquivalentTo((string[])[Path.Combine(_binaries, "a.dll"), Path.Combine(_binaries, "b.dll")]));
    }

    // A pool DLL that imports a DLL of neither the pool nor Windows throws with that DLL and copies nothing, also not the pool DLL.
    [Test]
    public void Add_PoolDllImportsMissingDll_ThrowsWithItAndCopiesNothing()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "a.dll");
        FakeExecutable.Write(Path.Combine(_pool, "a.dll"), "missing.dll");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system),
                Throws.TypeOf<FileNotFoundException>().With.Message.Contains("missing.dll"));
            Assert.That(Directory.GetFiles(_binaries), Is.EqualTo([Path.Combine(_binaries, "player.exe")]));
        }
    }

    // A DLL next to the executable stays as it is, also when the pool has another DLL of that name.
    [Test]
    public void Add_DllInFolderAndPool_KeepsTheFolderDll()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "a.dll");
        FakeExecutable.Write(Path.Combine(_binaries, "a.dll"));
        FakeExecutable.Write(Path.Combine(_pool, "a.dll"), "b.dll");
        byte[] before = File.ReadAllBytes(Path.Combine(_binaries, "a.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(File.ReadAllBytes(Path.Combine(_binaries, "a.dll")), Is.EqualTo(before));
    }

    // 2 files of 1 name with the same bytes are 1 DLL, so the copy goes on.
    [Test]
    public void Add_TwoSamePoolFilesOfImport_CopiesTheImport()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "msvcp140.dll");
        FakeExecutable.Write(Path.Combine(_pool, "a", "msvcp140.dll"));
        FakeExecutable.Write(Path.Combine(_pool, "b", "msvcp140.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(File.Exists(Path.Combine(_binaries, "msvcp140.dll")), Is.True);
    }

    // An executable without imports copies nothing and doesn't throw.
    [Test]
    public void Add_ExecutableWithoutImports_CopiesNothing()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"));
        FakeExecutable.Write(Path.Combine(_pool, "a.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(Directory.GetFiles(_binaries), Is.EqualTo([Path.Combine(_binaries, "player.exe")]));
    }

    // A folder with a space and a char outside ASCII gets its DLL like any other folder.
    [Test]
    public void Add_FolderWithSpaceAndUnicode_CopiesTheImport()
    {
        string binaries = Path.Combine(_binaries, "Spiel ü 1");
        FakeExecutable.Write(Path.Combine(binaries, "player.exe"), "a.dll");
        FakeExecutable.Write(Path.Combine(_pool, "ü sub", "a.dll"));

        UnrealRuntimes.Add(Path.Combine(binaries, "player.exe"), _pool, _system);

        Assert.That(File.Exists(Path.Combine(binaries, "a.dll")), Is.True);
    }

    // A pool DLL that isn't a PE file throws, so a truncated file never ends up next to the player.
    [Test]
    public void Add_PoolDllNotPe_ThrowsInvalidDataException()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "a.dll");
        WriteBytes(_pool, "a.dll", [1, 2, 3]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system), Throws.TypeOf<InvalidDataException>());
            Assert.That(File.Exists(Path.Combine(_binaries, "a.dll")), Is.False);
        }
    }
}
