using NUnit.Framework;
using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DiskSpaceTests
{
    // Equals the size of a finished engine of this file, which is only its marker.
    private const int EngineBytes = 40;
    private const long Gib = 1L << 30;
    private const string Sha1 = "0000000000000000000000000000000000000001";

    private string _downloads = null!;
    private string _engines = null!;
    private TestFolder _folder = null!;
    private StringWriter _output = null!;
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _root = _folder.NewFolder();
        _engines = Directory.CreateDirectory(Path.Combine(_root, DiskSpace.EnginesFolder)).FullName;
        _downloads = Path.Combine(_root, DiskSpace.DownloadsFolder);
        _output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _folder.Delete();
    }

    // A drive with room for the install and the scratch keeps every engine.
    [Test]
    public void MakeRoom_EnoughSpace_DeletesNothing()
    {
        string other = NewFinishedEngine("other", ageInDays: 9);
        string wanted = Path.Combine(_engines, "wanted");

        DiskSpace.MakeRoom(_root, Space(10 * Gib, [wanted], []), _ => DiskSpace.MarginBytes + (10 * Gib), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(other, Does.Exist);
            Assert.That(_output.ToString(), Is.Empty);
        }
    }

    // A short drive loses the engines with the oldest markers first until there's room. The rest stays.
    [Test]
    public void MakeRoom_ShortSpace_DeletesOldestMarkerFirstAndStopsWhenEnough()
    {
        string newest = NewFinishedEngine("a", ageInDays: 1);
        string oldest = NewFinishedEngine("b", ageInDays: 3);
        string middle = NewFinishedEngine("c", ageInDays: 2);
        string wanted = Path.Combine(_engines, "wanted");
        long start = DiskSpace.MarginBytes - EngineBytes - 1;

        DiskSpace.MakeRoom(_root, Space(0, [wanted], []), _ => start + (EngineBytes * DeletedOf(3)), _output);

        string[] lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldest, Does.Not.Exist);
            Assert.That(middle, Does.Not.Exist);
            Assert.That(newest, Does.Exist);
            Assert.That(lines, Is.EqualTo([$"Removed {oldest}", $"Removed {middle}"]));
        }
    }

    // A scratch folder of a failed build goes before any engine, because an engine costs a download to get back.
    [Test]
    public void MakeRoom_StaleScratch_DeletesItBeforeAnyEngine()
    {
        string engine = NewFinishedEngine("old", ageInDays: 9);
        string scratch = _folder.NewFolder();
        TestFolder.WriteBytes(scratch, "out.bin", new byte[EngineBytes]);
        string wanted = Path.Combine(_engines, "wanted");

        DiskSpace.MakeRoom(
            _root,
            Space(0, [wanted], [scratch, Path.Combine(_folder.Root, "missing")]),
            _ => DiskSpace.MarginBytes - (Directory.Exists(scratch) ? 1 : 0),
            _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scratch, Does.Not.Exist);
            Assert.That(engine, Does.Exist);
            Assert.That(_output.ToString().Trim(), Is.EqualTo($"Removed {scratch}"));
        }
    }

    // The wanted engine stays even with the oldest marker, because deleting it would mean another download.
    [Test]
    public void MakeRoom_WantedEngine_IsNeverDeleted()
    {
        string wanted = NewFinishedEngine("wanted", ageInDays: 9);
        string other = NewFinishedEngine("other", ageInDays: 1);

        DiskSpace.MakeRoom(_root, Space(0, [wanted], []), _ => DiskSpace.MarginBytes - 1 + DeletedOf(2), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wanted, Does.Exist);
            Assert.That(other, Does.Not.Exist);
        }
    }

    // A different spelling of the wanted folder still keeps it.
    [Test]
    public void MakeRoom_WantedEngineInOtherCase_IsNeverDeleted()
    {
        string wanted = NewFinishedEngine("wanted", ageInDays: 9);

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [wanted.ToUpperInvariant()], []), _ => 0, _output),
            Throws.InstanceOf<IOException>());
        Assert.That(wanted, Does.Exist);
    }

    // A folder without a marker stays even as the only candidate, because a run may be writing to it and the call fails with the numbers.
    [Test]
    public void MakeRoom_FolderWithoutMarker_IsNeverDeleted()
    {
        string unfinished = Path.Combine(_engines, "unfinished");
        _ = Directory.CreateDirectory(unfinished);
        string wanted = Path.Combine(_engines, "wanted");

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [wanted], []), _ => 0, _output),
            Throws.InstanceOf<IOException>());
        Assert.That(unfinished, Does.Exist);
    }

    // A file next to the engine folders stays, because only a folder can be an engine.
    [Test]
    public void MakeRoom_FileNextToTheEngines_StaysInPlace()
    {
        string file = Path.Combine(_engines, "other.manifest");
        File.WriteAllText(file, "text");

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => 0, _output),
            Throws.InstanceOf<IOException>());
        Assert.That(file, Does.Exist);
    }

    // Read-only and hidden files and read-only folders don't stop a delete, because Directory.Delete refuses them by itself.
    [Test]
    public void MakeRoom_ReadOnlyFiles_AreDeleted()
    {
        string other = NewFinishedEngine("other", ageInDays: 1);
        string readOnlyFolder = Path.Combine(other, "folder");
        _ = Directory.CreateDirectory(readOnlyFolder);
        string readOnlyFile = Path.Combine(readOnlyFolder, "file.bin");
        string hiddenFile = Path.Combine(other, "hidden.bin");
        File.WriteAllText(readOnlyFile, "text");
        File.WriteAllText(hiddenFile, "text");
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
        File.SetAttributes(hiddenFile, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(readOnlyFolder, FileAttributes.Directory | FileAttributes.ReadOnly);

        DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => DiskSpace.MarginBytes - 1 + DeletedOf(1), _output);

        Assert.That(other, Does.Not.Exist);
    }

    // A delete that stops halfway leaves a folder without a marker, because a marker on half an engine would pass as installed.
    [Test]
    public void Delete_FileInUse_RemovesTheMarkerFirst()
    {
        string other = NewFinishedEngine("other", ageInDays: 1);
        string file = Path.Combine(other, "file.bin");

        using (File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.That(() => DiskSpace.Delete(other), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        }

        Assert.That(EngineMarker.Read(other), Is.Null);
    }

    // A drive too small without the other engines keeps them and fails with the drive and both numbers, rounded so they don't look like enough.
    [Test]
    public void MakeRoom_TooShortWithEveryEngineGone_ThrowsAndDeletesNothing()
    {
        string other = NewFinishedEngine("other", ageInDays: 1);
        string wanted = Path.Combine(_engines, "wanted");
        long free = (5 * Gib) + (Gib / 2);
        string drive = Path.GetPathRoot(_engines)!;

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space((10 * Gib) + 1, [wanted], []), _ => free, _output),
            Throws.InstanceOf<IOException>()
                .With.Message.Contains(drive)
                .And.Message.Contains("5 GiB")
                .And.Message.Contains("31 GiB"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(other, Does.Exist);
            Assert.That(_output.ToString(), Is.Empty);
        }
    }

    // A drive too small without the other engines still loses the scratch folders, because nothing needs them.
    [Test]
    public void MakeRoom_TooShortWithStaleScratch_DeletesTheScratchAndKeepsTheEngines()
    {
        string other = NewFinishedEngine("other", ageInDays: 1);
        string scratch = _folder.NewFolder();
        TestFolder.WriteBytes(scratch, "out.bin", new byte[EngineBytes]);
        string wanted = Path.Combine(_engines, "wanted");

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(10 * Gib, [wanted], [scratch]), _ => 0, _output),
            Throws.InstanceOf<IOException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scratch, Does.Not.Exist);
            Assert.That(other, Does.Exist);
            Assert.That(_output.ToString().Trim(), Is.EqualTo($"Removed {scratch}"));
        }
    }

    // Engines whose sizes add up to exactly the missing space go, because removing them makes the install fit.
    [Test]
    public void MakeRoom_SizesAddUpToTheMissingSpace_DeletesTheEngines()
    {
        string first = NewFinishedEngine("first", ageInDays: 2);
        string second = NewFinishedEngine("second", ageInDays: 1);
        long start = DiskSpace.MarginBytes - (2 * EngineBytes);

        DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => start + (EngineBytes * DeletedOf(2)), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Does.Not.Exist);
            Assert.That(second, Does.Not.Exist);
        }
    }

    // Engines whose sizes add up to 1 byte less than the missing space stay, because removing them wouldn't make the install fit.
    [Test]
    public void MakeRoom_SizesOneByteShort_ThrowsAndDeletesNothing()
    {
        string first = NewFinishedEngine("first", ageInDays: 2);
        string second = NewFinishedEngine("second", ageInDays: 1);
        long start = DiskSpace.MarginBytes - (2 * EngineBytes) - 1;

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => start + (EngineBytes * DeletedOf(2)), _output),
            Throws.InstanceOf<IOException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Does.Exist);
            Assert.That(second, Does.Exist);
        }
    }

    // A file in a subfolder and a hidden file count toward the size of an engine, because a delete frees those too.
    [Test]
    public void SizeOf_FilesInSubfolderAndHidden_AddsUpEveryFile()
    {
        string engine = NewFinishedEngine("engine", ageInDays: 1);
        string subfolder = Path.Combine(engine, "folder");
        _ = Directory.CreateDirectory(subfolder);
        File.WriteAllText(Path.Combine(subfolder, "file.bin"), "text");
        string hiddenFile = Path.Combine(engine, "hidden.bin");
        File.WriteAllText(hiddenFile, "text");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        Assert.That(DiskSpace.SizeOf(engine), Is.EqualTo(EngineBytes + 8));
    }

    // A folder that doesn't exist yet has 0 bytes, so a 1st install needs its whole size.
    [Test]
    public void SizeOf_MissingFolder_ReturnsZero()
    {
        Assert.That(DiskSpace.SizeOf(Path.Combine(_engines, "missing")), Is.Zero);
    }

    // An engine that goes takes its manifest along and the manifest of another engine stays, so an evicted engine doesn't leave a file behind.
    [Test]
    public void MakeRoom_EngineWithManifest_DeletesThatManifestOnly()
    {
        string oldest = NewFinishedEngine("oldest", ageInDays: 2);
        string newest = NewFinishedEngine("newest", ageInDays: 1);
        File.WriteAllText(DiskSpace.ManifestPath(oldest), "text");
        File.WriteAllText(DiskSpace.ManifestPath(newest), "text");

        DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => DiskSpace.MarginBytes - 1 + DeletedOf(2), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldest, Does.Not.Exist);
            Assert.That(DiskSpace.ManifestPath(oldest), Does.Not.Exist);
            Assert.That(DiskSpace.ManifestPath(newest), Does.Exist);
        }
    }

    // An installed engine with nothing to build needs exactly the margin, 20 GiB.
    [TestCase(20 * Gib, false)]
    [TestCase((20 * Gib) - 1, true)]
    public void MakeRoom_NothingMissing_NeedsOnlyTheMargin(long free, bool deleted)
    {
        string other = NewFinishedEngine("other", ageInDays: 1);

        DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => free + DeletedOf(1), _output);

        Assert.That(!Directory.Exists(other), Is.EqualTo(deleted));
    }

    // The build bytes count on top of the install and the margin, so a cook that needs scratch gets it.
    [TestCase((25 * Gib) + 3, false)]
    [TestCase((25 * Gib) + 2, true)]
    public void MakeRoom_BuildBytes_CountOnTopOfTheInstall(long free, bool deleted)
    {
        string other = NewFinishedEngine("other", ageInDays: 1);
        InstallSpace space = new(3, 5 * Gib, [Path.Combine(_engines, "wanted")], []);

        DiskSpace.MakeRoom(_root, space, _ => free + DeletedOf(1), _output);

        Assert.That(!Directory.Exists(other), Is.EqualTo(deleted));
    }

    // Installers go before engines and the oldest installer goes first, because an engine is the biggest download to get back.
    [Test]
    public void MakeRoom_InstallersAndEngines_DeletesOldestInstallerThenEngines()
    {
        string engine = NewFinishedEngine("engine", ageInDays: 9);
        string newer = NewInstaller("newer.exe", ageInDays: 1);
        string older = NewInstaller("older.exe", ageInDays: 2);
        long start = DiskSpace.MarginBytes - (2 * EngineBytes);

        DiskSpace.MakeRoom(_root, Space(0, [], []), _ => start + (EngineBytes * (2 - Directory.GetFiles(_downloads).Length)), _output);

        string[] lines = _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Is.EqualTo([$"Removed {older}", $"Removed {newer}"]));
            Assert.That(engine, Does.Exist);
        }
    }

    // The installer and the partial download of the install stay, because the install needs them right after.
    [Test]
    public void MakeRoom_KeptInstaller_StaysAndOtherDownloadsGo()
    {
        string kept = NewInstaller("kept.exe", ageInDays: 9);
        string keptPartial = NewInstaller("kept.exe.partial", ageInDays: 9);
        string other = NewInstaller("other.exe.partial", ageInDays: 1);

        DiskSpace.MakeRoom(_root, Space(0, [kept, keptPartial], []), _ => DiskSpace.MarginBytes - (File.Exists(other) ? 1 : 0), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kept, Does.Exist);
            Assert.That(keptPartial, Does.Exist);
            Assert.That(other, Does.Not.Exist);
        }
    }

    // A leftover file goes like a leftover folder, also when it is read-only.
    [Test]
    public void MakeRoom_ReadOnlyLeftoverFile_IsDeleted()
    {
        string leftover = Path.Combine(_root, "left.partial");
        File.WriteAllText(leftover, "text");
        File.SetAttributes(leftover, FileAttributes.ReadOnly);

        DiskSpace.MakeRoom(_root, Space(0, [], [leftover]), _ => DiskSpace.MarginBytes - (File.Exists(leftover) ? 1 : 0), _output);

        Assert.That(leftover, Does.Not.Exist);
    }

    // An output folder without downloads and engines fails with the numbers, because there is nothing to remove.
    [Test]
    public void MakeRoom_NoDownloadsOrEnginesFolder_ThrowsWithTheNumbers()
    {
        Directory.Delete(_engines);

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [], []), _ => 0, _output),
            Throws.InstanceOf<IOException>().With.Message.Contains("has 0 GiB free").And.Message.Contains("needs 20 GiB"));
    }

    // A file counts with its own size, so an installer measures like a folder.
    [Test]
    public void SizeOf_File_ReturnsItsLength()
    {
        Assert.That(DiskSpace.SizeOf(NewInstaller("file.exe", ageInDays: 1)), Is.EqualTo(EngineBytes));
    }

    // The free space comes from the drive, so a folder that doesn't exist yet reads too.
    [Test]
    public void FreeBytes_MissingFolder_ReadsItsDrive()
    {
        Assert.That(DiskSpace.FreeBytes(Path.Combine(_engines, "missing")), Is.Positive);
    }

    private int DeletedOf(int engines)
    {
        return engines - Directory.GetDirectories(_engines).Length;
    }

    private static InstallSpace Space(long missingBytes, string[] keepPaths, string[] leftovers)
    {
        return new InstallSpace(missingBytes, 0, keepPaths, leftovers);
    }

    private string NewFinishedEngine(string name, int ageInDays)
    {
        string engine = Path.Combine(_engines, name);
        _ = Directory.CreateDirectory(engine);
        EngineMarker.Write(engine, Sha1);
        File.SetLastWriteTimeUtc(Path.Combine(engine, EngineMarker.FileName), DateTime.UtcNow.AddDays(-ageInDays));

        return engine;
    }

    private string NewInstaller(string name, int ageInDays)
    {
        string installer = Path.Combine(_downloads, name);
        TestFolder.WriteBytes(_downloads, name, new byte[EngineBytes]);
        File.SetLastWriteTimeUtc(installer, DateTime.UtcNow.AddDays(-ageInDays));

        return installer;
    }
}
