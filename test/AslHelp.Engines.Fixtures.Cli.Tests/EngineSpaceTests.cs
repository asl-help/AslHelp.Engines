using NUnit.Framework;
using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class EngineSpaceTests
{
    // Equals the size of a finished engine of this file, which is only its marker.
    private const int EngineBytes = 40;
    private const long Gib = 1L << 30;
    private const string Sha1 = "0000000000000000000000000000000000000001";

    private string _engines = null!;
    private TestFolder _folder = null!;
    private StringWriter _output = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _engines = _folder.NewFolder();
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

        EngineSpace.MakeRoom(_engines, wanted, 10 * Gib, [], _ => EngineSpace.ScratchBytes + (10 * Gib), _output);

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
        long start = EngineSpace.ScratchBytes - EngineBytes - 1;

        EngineSpace.MakeRoom(_engines, wanted, 0, [], _ => start + (EngineBytes * DeletedOf(3)), _output);

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

        EngineSpace.MakeRoom(
            _engines,
            wanted,
            0,
            [scratch, Path.Combine(_folder.Root, "missing")],
            _ => EngineSpace.ScratchBytes - (Directory.Exists(scratch) ? 1 : 0),
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

        EngineSpace.MakeRoom(_engines, wanted, 0, [], _ => EngineSpace.ScratchBytes - 1 + DeletedOf(2), _output);

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
            () => EngineSpace.MakeRoom(_engines, wanted.ToUpperInvariant(), 0, [], _ => 0, _output),
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
            () => EngineSpace.MakeRoom(_engines, wanted, 0, [], _ => 0, _output),
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
            () => EngineSpace.MakeRoom(_engines, Path.Combine(_engines, "wanted"), 0, [], _ => 0, _output),
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

        EngineSpace.MakeRoom(_engines, Path.Combine(_engines, "wanted"), 0, [], _ => EngineSpace.ScratchBytes - 1 + DeletedOf(1), _output);

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
            Assert.That(() => EngineSpace.Delete(other), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
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
            () => EngineSpace.MakeRoom(_engines, wanted, (10 * Gib) + 1, [], _ => free, _output),
            Throws.InstanceOf<IOException>()
                .With.Message.Contains(drive)
                .And.Message.Contains("5 GiB")
                .And.Message.Contains("51 GiB"));

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
            () => EngineSpace.MakeRoom(_engines, wanted, 10 * Gib, [scratch], _ => 0, _output),
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
        long start = EngineSpace.ScratchBytes - (2 * EngineBytes);

        EngineSpace.MakeRoom(_engines, Path.Combine(_engines, "wanted"), 0, [], _ => start + (EngineBytes * DeletedOf(2)), _output);

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
        long start = EngineSpace.ScratchBytes - (2 * EngineBytes) - 1;

        Assert.That(
            () => EngineSpace.MakeRoom(_engines, Path.Combine(_engines, "wanted"), 0, [], _ => start + (EngineBytes * DeletedOf(2)), _output),
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

        Assert.That(EngineSpace.SizeOf(engine), Is.EqualTo(EngineBytes + 8));
    }

    // A folder that doesn't exist yet has 0 bytes, so a 1st install needs its whole size.
    [Test]
    public void SizeOf_MissingFolder_ReturnsZero()
    {
        Assert.That(EngineSpace.SizeOf(Path.Combine(_engines, "missing")), Is.Zero);
    }

    // An engine that goes takes its manifest along and the manifest of another engine stays, so an evicted engine doesn't leave a file behind.
    [Test]
    public void MakeRoom_EngineWithManifest_DeletesThatManifestOnly()
    {
        string oldest = NewFinishedEngine("oldest", ageInDays: 2);
        string newest = NewFinishedEngine("newest", ageInDays: 1);
        File.WriteAllText(EngineSpace.ManifestPath(oldest), "text");
        File.WriteAllText(EngineSpace.ManifestPath(newest), "text");

        EngineSpace.MakeRoom(_engines, Path.Combine(_engines, "wanted"), 0, [], _ => EngineSpace.ScratchBytes - 1 + DeletedOf(2), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldest, Does.Not.Exist);
            Assert.That(EngineSpace.ManifestPath(oldest), Does.Not.Exist);
            Assert.That(EngineSpace.ManifestPath(newest), Does.Exist);
        }
    }

    // An installed engine needs exactly the scratch, 40 GiB.
    [TestCase(40 * Gib, false)]
    [TestCase((40 * Gib) - 1, true)]
    public void MakeRoom_InstalledEngine_NeedsOnlyTheScratch(long free, bool deleted)
    {
        string other = NewFinishedEngine("other", ageInDays: 1);

        EngineSpace.MakeRoom(_engines, Path.Combine(_engines, "wanted"), 0, [], _ => free + DeletedOf(1), _output);

        Assert.That(!Directory.Exists(other), Is.EqualTo(deleted));
    }

    // The free space comes from the drive, so a folder that doesn't exist yet reads too.
    [Test]
    public void FreeBytes_MissingFolder_ReadsItsDrive()
    {
        Assert.That(EngineSpace.FreeBytes(Path.Combine(_engines, "missing")), Is.Positive);
    }

    private int DeletedOf(int engines)
    {
        return engines - Directory.GetDirectories(_engines).Length;
    }

    private string NewFinishedEngine(string name, int ageInDays)
    {
        string engine = Path.Combine(_engines, name);
        _ = Directory.CreateDirectory(engine);
        EngineMarker.Write(engine, Sha1);
        File.SetLastWriteTimeUtc(Path.Combine(engine, EngineMarker.FileName), DateTime.UtcNow.AddDays(-ageInDays));

        return engine;
    }
}
