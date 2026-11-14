using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealRuntimesTests
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

    // An import in a pool subfolder is copied under the importer's spelling, because the loader ignores case.
    [Test]
    public void Add_ImportInPool_CopiesUnderTheImporterSpelling()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "MSVCP120.dll");
        FakeExecutable.Write(Path.Combine(_pool, "crt", "msvcp120.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(Directory.GetFiles(_binaries, "*.dll"), Is.EqualTo([Path.Combine(_binaries, "MSVCP120.dll")]));
    }

    // A DLL of Windows is neither copied nor reported, because the base image has it.
    [Test]
    public void Add_WindowsImport_IsSkipped()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "KERNEL32.dll");
        WriteBytes(_system, "kernel32.dll", [1]);

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(Directory.GetFiles(_binaries), Has.Length.EqualTo(1));
    }

    // An API set in the pool isn't copied, because 4.19 ships the UCRT sets in AppLocalDependencies and Windows ignores app-local API
    // sets.
    [Test]
    public void Add_ApiSetInPool_IsNotCopied()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "api-ms-win-crt-runtime-l1-1-0.dll");
        FakeExecutable.Write(Path.Combine(_pool, "ucrt", "api-ms-win-crt-runtime-l1-1-0.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(Directory.GetFiles(_binaries), Has.Length.EqualTo(1));
    }

    // 2 files of 1 name with other bytes throw with both paths, so the copied build never depends on the enumeration order.
    [Test]
    public void Add_TwoDifferentPoolFilesOfImport_ThrowsWithBothPaths()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "msvcp140.dll");
        FakeExecutable.Write(Path.Combine(_pool, "a", "msvcp140.dll"));
        FakeExecutable.Write(Path.Combine(_pool, "b", "msvcp140.dll"), "KERNEL32.dll");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system),
                Throws.TypeOf<InvalidDataException>()
                    .With.Message.Contains(Path.Combine(_pool, "a", "msvcp140.dll"))
                    .And.Message.Contains(Path.Combine(_pool, "b", "msvcp140.dll")));
            Assert.That(Directory.GetFiles(_binaries), Has.Length.EqualTo(1));
        }
    }

    // A pool DLL wins over a Windows DLL of the same name, because the build machine has runtimes the base image doesn't have.
    [Test]
    public void Add_ImportInPoolAndWindows_CopiesFromThePool()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "xinput1_3.dll");
        FakeExecutable.Write(Path.Combine(_pool, "xinput1_3.dll"));
        WriteBytes(_system, "xinput1_3.dll", [1]);

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(File.ReadAllBytes(Path.Combine(_binaries, "xinput1_3.dll")), Is.EqualTo(File.ReadAllBytes(Path.Combine(_pool, "xinput1_3.dll"))));
    }

    // Imports in neither the pool nor Windows fail with every DLL and the pool. Nothing is copied.
    [Test]
    public void Add_ImportsNowhere_ThrowsWithEveryNameAndCopiesNothing()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "first.dll", "msvcp120.dll", "second.dll");
        FakeExecutable.Write(Path.Combine(_pool, "msvcp120.dll"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system),
                Throws.TypeOf<FileNotFoundException>()
                    .With.Message.Contains("first.dll, second.dll")
                    .And.Message.Contains(_pool)
                    .And.Message.Contains("player.exe"));
            Assert.That(Directory.GetFiles(_binaries), Has.Length.EqualTo(1));
        }
    }

    // A pool DLL that imports another pool DLL brings that one too, so a runtime that needs another runtime starts.
    [Test]
    public void Add_PoolDllImportsAnother_CopiesBoth()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "first.dll");
        FakeExecutable.Write(Path.Combine(_pool, "first.dll"), "second.dll");
        FakeExecutable.Write(Path.Combine(_pool, "second.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(
            Directory.GetFiles(_binaries, "*.dll"),
            Is.EquivalentTo((string[])[Path.Combine(_binaries, "first.dll"), Path.Combine(_binaries, "second.dll")]));
    }

    // 2 files of 1 name with other bytes don't throw when no import loads that name.
    [Test]
    public void Add_TwoDifferentPoolFilesOfOtherDll_CopiesTheImport()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "runtime.dll");
        FakeExecutable.Write(Path.Combine(_pool, "runtime.dll"));
        FakeExecutable.Write(Path.Combine(_pool, "a", "other.dll"));
        FakeExecutable.Write(Path.Combine(_pool, "b", "other.dll"), "KERNEL32.dll");

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(Directory.GetFiles(_binaries, "*.dll"), Is.EqualTo([Path.Combine(_binaries, "runtime.dll")]));
    }

    // A DLL next to the executable is read too, so its own imports get copied.
    [Test]
    public void Add_LocalDllImportsRuntime_CopiesTheRuntime()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "local.dll");
        FakeExecutable.Write(Path.Combine(_binaries, "local.dll"), "runtime.dll");
        FakeExecutable.Write(Path.Combine(_pool, "runtime.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        Assert.That(File.Exists(Path.Combine(_binaries, "runtime.dll")), Is.True);
    }

    // A .partial file of a killed run is replaced and doesn't stay next to the copy.
    [Test]
    public void Add_PartialLeftBehind_IsReplaced()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "runtime.dll");
        WriteBytes(_binaries, "runtime.dll.partial", [1, 1, 1]);
        FakeExecutable.Write(Path.Combine(_pool, "runtime.dll"));

        UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(Path.Combine(_binaries, "runtime.dll")), Is.EqualTo(File.ReadAllBytes(Path.Combine(_pool, "runtime.dll"))));
            Assert.That(File.Exists(Path.Combine(_binaries, "runtime.dll.partial")), Is.False);
        }
    }

    // A missing pool folder counts as empty, so the message points at the missing DLL and not at the folder.
    [Test]
    public void Add_PoolMissing_ThrowsWithTheMissingName()
    {
        FakeExecutable.Write(Path.Combine(_binaries, "player.exe"), "runtime.dll");

        Assert.That(
            () => UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), Path.Combine(_pool, "missing"), _system),
            Throws.TypeOf<FileNotFoundException>().With.Message.Contains("runtime.dll"));
    }

    // A missing executable throws with its path.
    [Test]
    public void Add_ExecutableMissing_ThrowsFileNotFoundException()
    {
        Assert.That(
            () => UnrealRuntimes.Add(Path.Combine(_binaries, "player.exe"), _pool, _system),
            Throws.TypeOf<FileNotFoundException>()
                .With.Property(nameof(FileNotFoundException.FileName)).EqualTo(Path.Combine(_binaries, "player.exe")));
    }
}
