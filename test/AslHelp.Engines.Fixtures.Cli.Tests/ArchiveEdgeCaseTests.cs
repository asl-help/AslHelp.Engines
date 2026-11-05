using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using ZstdSharp;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class ArchiveEdgeCaseTests
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

    // A 0-byte file is read back with no data stream, so it comes back as an empty file.
    [Test]
    public void Pack_EmptyFile_RoundTrips()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "empty.bin", []);
        WriteBytes(source, "sub/empty.txt", []);

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new FileInfo(Path.Combine(destination, "empty.bin")).Length, Is.Zero);
            Assert.That(new FileInfo(Path.Combine(destination, "sub", "empty.txt")).Length, Is.Zero);
        }
    }

    // Entries are written in ordinal order with '/' separators, so '-' sorts before '/' and upper case sorts before lower case.
    [Test]
    public void Pack_MixedNames_WritesEntriesInOrdinalOrder()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "b.txt", [1]);
        WriteBytes(source, "C.txt", [2]);
        WriteBytes(source, "a/x.txt", [3]);
        WriteBytes(source, "a-b/x.txt", [4]);
        WriteBytes(source, "a.txt", [5]);
        _ = Directory.CreateDirectory(Path.Combine(source, "a", "empty"));

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        Archive.Pack(source, archive);

        string[] expected = ["C.txt", "a-b/", "a-b/x.txt", "a.txt", "a/", "a/empty/", "a/x.txt", "b.txt"];

        Assert.That(ReadEntryNames(archive), Is.EqualTo(expected));
    }

    // Data larger than a zstd worker job is split across threads and still comes back whole and packs the same twice.
    [Test]
    public void Pack_LargeIncompressibleFile_RoundTripsAndPacksTheSameTwice()
    {
        string source = _folder.NewFolder();
        byte[] bytes = Noise(24 * 1024 * 1024);
        WriteBytes(source, "large.bin", bytes);

        string output = _folder.NewFolder();
        string first = Path.Combine(output, "first.tar.zst");
        string second = Path.Combine(output, "second.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, first);
        Archive.Pack(source, second);
        Archive.Unpack(first, destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(second), Is.EqualTo(File.ReadAllBytes(first)));
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "large.bin")), Is.EqualTo(bytes));
        }
    }

    // A file that cannot be read fails the pack, so the temp file is deleted and the old archive stays.
    [Test]
    [Platform("Win")]
    public void Pack_LockedFile_ThrowsAndKeepsOldArchive()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", [1, 2, 3]);

        string output = _folder.NewFolder();
        string archive = Path.Combine(output, "out.tar.zst");
        Archive.Pack(source, archive);
        byte[] old = File.ReadAllBytes(archive);

        WriteBytes(source, "b.txt", [4]);

        using (new FileStream(Path.Combine(source, "b.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(() => Archive.Pack(source, archive), Throws.InstanceOf<IOException>());
        }

        string[] expected = [archive];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFileSystemEntries(output), Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(archive), Is.EqualTo(old));
        }
    }

    // A folder in the way of the archive is refused before any write, so no temp file is made.
    [Test]
    public void Pack_ArchivePathIsFolder_ThrowsIOExceptionAndLeavesNoTempFile()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", [1]);

        string archive = _folder.NewFolder();

        Assert.That(() => Archive.Pack(source, archive), Throws.TypeOf<IOException>().With.Message.Contains(archive));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(archive + ".partial"), Is.False);
            Assert.That(Directory.GetFileSystemEntries(archive), Is.Empty);
        }
    }

    // Pack creates every missing folder above the archive, the same way Unpack creates the destination's parents.
    [Test]
    public void Pack_MissingArchiveFolder_CreatesIt()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", [1]);

        string archive = Path.Combine(_folder.Root, "x", "y", "out.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        Assert.That(File.ReadAllBytes(Path.Combine(destination, "a.txt")), Is.EqualTo(new byte[] { 1 }));
    }

    // A failed final move keeps the complete temp file and puts its path in the message, so a large pack is not lost.
    [Test]
    [Platform("Win")]
    public void Pack_ArchiveOpenAtMove_ThrowsIOExceptionAndKeepsTempFile()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", [1, 2, 3]);

        string output = _folder.NewFolder();
        string archive = Path.Combine(output, "out.tar.zst");
        File.WriteAllBytes(archive, [9]);

        string tempPath = archive + ".partial";

        using (new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(() => Archive.Pack(source, archive), Throws.TypeOf<IOException>().With.Message.Contains(tempPath));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(archive), Is.EqualTo("\t"u8.ToArray()));
            Assert.That(File.Exists(tempPath), Is.True);
        }

        File.Move(tempPath, archive, overwrite: true);
        string destination = Path.Combine(_folder.Root, "destination");
        Archive.Unpack(archive, destination);

        Assert.That(File.ReadAllBytes(Path.Combine(destination, "a.txt")), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    // A folder link is refused before the enumeration recurses into it, so a link to a parent cannot loop.
    [Test]
    [Platform("Win")]
    public void Pack_FolderLinkToParent_ThrowsIOException()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", [1]);

        string link = Path.Combine(source, "loop");

        ProcessStartInfo info = new("cmd", ["/c", "mklink", "/J", link, source]) { UseShellExecute = false, RedirectStandardOutput = true };

        using (var mklink = Process.Start(info)!)
        {
            mklink.WaitForExit();
        }

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");

        Assert.That(() => Archive.Pack(source, archive), Throws.TypeOf<IOException>().With.Message.Contains(link));
        Assert.That(File.Exists(archive + ".partial"), Is.False);
    }

    // A trailing separator is the same folder, so the temp folder must sit next to it and not inside it.
    [TestCase(true)]
    [TestCase(false)]
    public void Unpack_DestinationWithTrailingSeparator_Unpacks(bool exists)
    {
        string archive = PackOneFile();
        string destination = Path.Combine(_folder.Root, "destination");

        if (exists)
        {
            _ = Directory.CreateDirectory(destination);
        }

        Archive.Unpack(archive, destination + Path.DirectorySeparatorChar);

        string[] expected = [Path.Combine(destination, "a.txt")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFileSystemEntries(destination), Is.EqualTo(expected));
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // 'tar -C folder .' writes the entries './' and './a.txt', which resolve to the destination itself and not outside it.
    [Test]
    public void Unpack_DotSlashEntries_Unpacks()
    {
        string archive = Path.Combine(_folder.NewFolder(), "dot.tar.zst");
        CraftArchive(archive, writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
            writer.WriteEntry(NewFile("./a.txt", [1, 2]));
        });

        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Unpack(archive, destination);

        Assert.That(File.ReadAllBytes(Path.Combine(destination, "a.txt")), Is.EqualTo(new byte[] { 1, 2 }));
    }

    // A file in the way of the destination is refused before any extraction, so no temp folder is made and the file stays.
    [Test]
    public void Unpack_DestinationIsFile_ThrowsIOExceptionAndLeavesNoTempFolder()
    {
        string archive = PackOneFile();
        string destination = Path.Combine(_folder.Root, "destination");
        File.WriteAllBytes(destination, [9]);

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<IOException>().With.Message.Contains(destination));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo("\t"u8.ToArray()));
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // A failed final move keeps the complete temp folder and puts its path in the message, so a large unpack is not lost.
    [Test]
    [Platform("Win")]
    public void Unpack_DestinationCannotBeReplaced_ThrowsIOExceptionAndKeepsTempFolder()
    {
        string archive = PackOneFile();
        string destination = Path.Combine(_folder.Root, "destination");
        string tempDirectory = destination + ".partial";

        // Mark the empty destination read-only, because Windows then refuses to delete it.
        var folder = Directory.CreateDirectory(destination);
        folder.Attributes |= FileAttributes.ReadOnly;

        try
        {
            Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<IOException>().With.Message.Contains(tempDirectory));
        }
        finally
        {
            folder.Attributes &= ~FileAttributes.ReadOnly;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(Path.Combine(tempDirectory, "a.txt")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(Directory.GetFileSystemEntries(destination), Is.Empty);
        }
    }

    // A temp folder from a crashed run is deleted first, so its files do not show up in the destination.
    [Test]
    public void Unpack_StaleTempFolder_IsReplaced()
    {
        string archive = PackOneFile();
        string destination = Path.Combine(_folder.Root, "destination");
        WriteBytes(destination + ".partial", "stale/old.txt", [7]);

        Archive.Unpack(archive, destination);

        string[] expected = [Path.Combine(destination, "a.txt")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFileSystemEntries(destination), Is.EqualTo(expected));
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // A missing archive fails after the temp folder is made, so the temp folder is deleted again.
    [Test]
    public void Unpack_MissingArchive_ThrowsFileNotFoundExceptionAndLeavesNoDestination()
    {
        string archive = Path.Combine(_folder.Root, "missing.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<FileNotFoundException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // Unpack creates every missing parent of the destination.
    [Test]
    public void Unpack_MissingParents_CreatesDestination()
    {
        string archive = PackOneFile();
        string destination = Path.Combine(_folder.Root, "x", "y", "destination");

        Archive.Unpack(archive, destination);

        Assert.That(File.Exists(Path.Combine(destination, "a.txt")), Is.True);
    }

    // A bad entry after good ones fails mid-way, so the files already written are deleted with the temp folder.
    [Test]
    public void Unpack_RefusedEntryAfterGoodEntries_LeavesNoDestination()
    {
        string archive = Path.Combine(_folder.NewFolder(), "evil.tar.zst");
        CraftArchive(archive, writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "good/"));
            writer.WriteEntry(NewFile("good/a.txt", [1]));
            writer.WriteEntry(NewFile("good/../../evil", [2]));
        });

        string parent = _folder.NewFolder();
        string destination = Path.Combine(parent, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains("good/../../evil"));

        Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
    }

    // Windows reads '\' and drive letters as path parts, so these entries are refused too.
    [TestCase(@"..\evil")]
    [TestCase(@"a\..\..\evil")]
    [TestCase(@"C:\evil")]
    [TestCase("C:evil")]
    [TestCase(@"\\server\share\evil")]
    [Platform("Win")]
    public void Unpack_WindowsEscapeEntry_ThrowsInvalidDataException(string name)
    {
        string archive = Path.Combine(_folder.NewFolder(), "evil.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(NewFile(name, [1])));

        string parent = _folder.NewFolder();
        string destination = Path.Combine(parent, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains(name));

        Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
    }

    // A folder entry that climbs out is refused before any folder is created outside.
    [Test]
    public void Unpack_DotDotFolderEntry_ThrowsInvalidDataExceptionAndCreatesNothingOutside()
    {
        string archive = Path.Combine(_folder.NewFolder(), "evil.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "../evil/")));

        string parent = _folder.NewFolder();
        string destination = Path.Combine(parent, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains("../evil/"));

        Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
    }

    // A name next to the temp folder that only shares its prefix is still outside, so it is refused.
    [Test]
    public void Unpack_SiblingWithSamePrefixEntry_ThrowsInvalidDataException()
    {
        string archive = Path.Combine(_folder.NewFolder(), "evil.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(NewFile("../destination.partial2/evil", [1])));

        string parent = _folder.NewFolder();
        string destination = Path.Combine(parent, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>());

        Assert.That(Directory.GetFileSystemEntries(parent), Is.Empty);
    }

    // The unpack writes into a fresh folder, so a path that an earlier entry took means a damaged archive, such as the
    // same name twice, a name in other case on a case-insensitive disk or a file where a folder must go.
    [TestCase("a.txt", "a.txt")]
    [TestCase("A.txt", "a.txt")]
    [TestCase("a", "a/b")]
    public void Unpack_EntryOnTakenPath_ThrowsInvalidDataExceptionAndLeavesNoDestination(string first, string second)
    {
        string archive = Path.Combine(_folder.NewFolder(), "taken.tar.zst");
        CraftArchive(archive, writer =>
        {
            writer.WriteEntry(NewFile(first, [1]));
            writer.WriteEntry(NewFile(second, [2]));
        });

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(
            () => Archive.Unpack(archive, destination),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"'{second}'"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // A file entry on the path of an earlier folder entry is a damaged archive too.
    [Test]
    public void Unpack_FileOnFolderPath_ThrowsInvalidDataException()
    {
        string archive = Path.Combine(_folder.NewFolder(), "taken.tar.zst");
        CraftArchive(archive, writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "a"));
            writer.WriteEntry(NewFile("a", [1]));
        });

        Assert.That(
            () => Archive.Unpack(archive, Path.Combine(_folder.Root, "destination")),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("'a'"));
    }

    // Every entry kind other than a file or a folder is refused, not only links.
    [TestCase(TarEntryType.Fifo)]
    [TestCase(TarEntryType.CharacterDevice)]
    [TestCase(TarEntryType.BlockDevice)]
    public void Unpack_SpecialEntry_ThrowsInvalidDataException(TarEntryType type)
    {
        string archive = Path.Combine(_folder.NewFolder(), "special.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(new PaxTarEntry(type, "special")));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains("special"));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    private string PackOneFile()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", [1, 2, 3]);

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        Archive.Pack(source, archive);

        return archive;
    }

    private static List<string> ReadEntryNames(string archive)
    {
        List<string> names = [];

        using var file = File.OpenRead(archive);
        using DecompressionStream decompression = new(file);
        using TarReader reader = new(decompression);

        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        return names;
    }
}
