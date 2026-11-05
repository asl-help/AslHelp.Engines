using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using ZstdSharp;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class ArchiveTests
{
    private static readonly DateTime _fileTime = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTime _folderTime = new(2023, 6, 7, 8, 9, 10, DateTimeKind.Utc);

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

    // A tree comes back with the same paths, bytes, empty folders and last write times.
    [Test]
    public void Pack_ThenUnpack_KeepsPathsBytesAndTimes()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", Pattern(10));
        WriteBytes(source, "sub/b.bin", Pattern(300));
        WriteBytes(source, "sub/deeper/c.txt", Pattern(7));
        _ = Directory.CreateDirectory(Path.Combine(source, "empty"));
        _ = Directory.CreateDirectory(Path.Combine(source, "sub", "empty2"));

        string[] files = ["a.txt", "sub/b.bin", "sub/deeper/c.txt"];
        string[] folders = ["empty", "sub", "sub/deeper", "sub/empty2"];

        for (int i = 0; i < files.Length; i++)
        {
            File.SetLastWriteTimeUtc(Path.Combine(source, files[i]), _fileTime.AddSeconds(i));
        }

        for (int i = 0; i < folders.Length; i++)
        {
            Directory.SetLastWriteTimeUtc(Path.Combine(source, folders[i]), _folderTime.AddSeconds(i));
        }

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAll(destination), Is.EqualTo(ReadAll(source)));
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);

            for (int i = 0; i < files.Length; i++)
            {
                Assert.That(File.GetLastWriteTimeUtc(Path.Combine(destination, files[i])), Is.EqualTo(_fileTime.AddSeconds(i)), files[i]);
            }

            for (int i = 0; i < folders.Length; i++)
            {
                string folder = Path.Combine(destination, folders[i]);

                Assert.That(Directory.Exists(folder), Is.True, folders[i]);
                Assert.That(Directory.GetLastWriteTimeUtc(folder), Is.EqualTo(_folderTime.AddSeconds(i)), folders[i]);
            }
        }
    }

    // A folder with no entries packs into a valid archive that unpacks into an empty folder.
    [Test]
    public void Pack_EmptyFolder_UnpacksToEmptyFolder()
    {
        string source = _folder.NewFolder();
        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.True);
            Assert.That(Directory.GetFileSystemEntries(destination), Is.Empty);
        }
    }

    // PAX stores paths as UTF-8, so paths outside ASCII and longer than 100 bytes come back.
    [Test]
    public void Pack_UnicodeAndLongFileNames_RoundTrips()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "ä/ü 1.txt", Pattern(3));
        WriteBytes(source, "\U0001F600.bin", Pattern(5));
        WriteBytes(source, new string('n', 120) + ".txt", Pattern(2));

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        Assert.That(ReadAll(destination), Is.EqualTo(ReadAll(source)));
    }

    // 2 folders with the same files and times give equal bytes, whatever order the files were written in.
    [Test]
    public void Pack_TwoFoldersWithSameContent_GivesIdenticalBytes()
    {
        string first = _folder.NewFolder();
        WriteBytes(first, "a.txt", Pattern(10));
        WriteBytes(first, "b/c.txt", Pattern(20));
        WriteBytes(first, "d.txt", Pattern(30));

        string second = _folder.NewFolder();
        WriteBytes(second, "d.txt", Pattern(30));
        WriteBytes(second, "b/c.txt", Pattern(20));
        WriteBytes(second, "a.txt", Pattern(10));

        foreach (string folder in new[] { first, second })
        {
            File.SetLastWriteTimeUtc(Path.Combine(folder, "a.txt"), _fileTime);
            File.SetLastWriteTimeUtc(Path.Combine(folder, "b", "c.txt"), _fileTime);
            File.SetLastWriteTimeUtc(Path.Combine(folder, "d.txt"), _fileTime);
            Directory.SetLastWriteTimeUtc(Path.Combine(folder, "b"), _folderTime);
        }

        string output = _folder.NewFolder();
        string firstArchive = Path.Combine(output, "first.tar.zst");
        string secondArchive = Path.Combine(output, "second.tar.zst");

        Archive.Pack(first, firstArchive);
        Archive.Pack(second, secondArchive);

        Assert.That(File.ReadAllBytes(secondArchive), Is.EqualTo(File.ReadAllBytes(firstArchive)));
    }

    // A file larger than the 81920-byte copy buffer comes back whole, with no tar padding at the end.
    [Test]
    public void Pack_FileLargerThanCopyBuffer_RoundTrips()
    {
        string source = _folder.NewFolder();
        byte[] bytes = Pattern(200_001);
        WriteBytes(source, "large.bin", bytes);

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        string destination = Path.Combine(_folder.Root, "destination");

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        Assert.That(File.ReadAllBytes(Path.Combine(destination, "large.bin")), Is.EqualTo(bytes));
    }

    // A missing source throws before the temp file is opened.
    [Test]
    public void Pack_MissingSource_ThrowsDirectoryNotFoundException()
    {
        string source = Path.Combine(_folder.Root, "missing");
        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");

        Assert.That(
            () => Archive.Pack(source, archive),
            Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(source));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(archive), Is.False);
            Assert.That(File.Exists(archive + ".partial"), Is.False);
        }
    }

    // Pack writes a temp file and moves it over the archive, so only the archive is left and a 2nd Pack replaces it.
    [Test]
    public void Pack_ExistingArchive_ReplacesItAndLeavesNoTempFile()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", Pattern(10));

        string output = _folder.NewFolder();
        string archive = Path.Combine(output, "out.tar.zst");

        Archive.Pack(source, archive);
        WriteBytes(source, "a.txt", Pattern(20));
        Archive.Pack(source, archive);

        string destination = Path.Combine(_folder.Root, "destination");
        Archive.Unpack(archive, destination);

        string[] expected = [archive];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFileSystemEntries(output), Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "a.txt")), Is.EqualTo(Pattern(20)));
        }
    }

    // The non-empty check runs before any write, so the destination stays as it was.
    [Test]
    public void Unpack_ExistingNonEmptyDestination_ThrowsIOExceptionAndLeavesIt()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", Pattern(10));

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        Archive.Pack(source, archive);

        string destination = _folder.NewFolder();
        WriteBytes(destination, "keep.txt", Pattern(4));

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<IOException>().With.Message.Contains(destination));

        string[] expected = [Path.Combine(destination, "keep.txt")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFileSystemEntries(destination), Is.EqualTo(expected));
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "keep.txt")), Is.EqualTo(Pattern(4)));
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // An entry that climbs out with '..' keeps its own message with no inner exception and writes nothing next to the destination.
    [Test]
    public void Unpack_DotDotEntry_ThrowsInvalidDataExceptionAndWritesNothingOutside()
    {
        string archive = Path.Combine(_folder.NewFolder(), "evil.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(NewFile("../evil", Pattern(4))));

        string parent = _folder.NewFolder();
        string destination = Path.Combine(parent, "destination");

        Assert.That(
            () => Archive.Unpack(archive, destination),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("../evil").And.InnerException.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(parent, "evil")), Is.False);
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // Path.Combine drops the root for a rooted entry, so the prefix check refuses absolute paths.
    [Test]
    public void Unpack_AbsoluteEntry_ThrowsInvalidDataException()
    {
        string archive = Path.Combine(_folder.NewFolder(), "evil.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(NewFile("/evil", Pattern(4))));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains("/evil"));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    // Only files and folders are extracted, so every link kind is refused.
    [TestCase(TarEntryType.SymbolicLink)]
    [TestCase(TarEntryType.HardLink)]
    public void Unpack_LinkEntry_ThrowsInvalidDataException(TarEntryType type)
    {
        string archive = Path.Combine(_folder.NewFolder(), "link.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(new PaxTarEntry(type, "evil-entry") { LinkName = "../x" }));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains("'evil-entry'"));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    // A cut at 0 bytes, inside the frame header, inside the file data or inside the 4-byte checksum throws InvalidDataException
    // with the archive path and leaves no destination and no .partial.
    [TestCase(0.0, 0)]
    [TestCase(0.0, 3)]
    [TestCase(0.5, 0)]
    [TestCase(1.0, -1)]
    public void Unpack_TruncatedArchive_ThrowsWithArchivePathAndLeavesNoDestination(double share, int offset)
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "file.bin", Noise(64 * 1024));

        string output = _folder.NewFolder();
        string archive = Path.Combine(output, "out.tar.zst");
        Archive.Pack(source, archive);

        byte[] bytes = File.ReadAllBytes(archive);
        string truncated = Path.Combine(output, "truncated.tar.zst");
        File.WriteAllBytes(truncated, bytes.AsSpan(0, (int)(bytes.Length * share) + offset));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(
            () => Archive.Unpack(truncated, destination),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(truncated).And.InnerException.TypeOf<EndOfStreamException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // A valid zstd frame whose 1st 512-byte block is not a tar header throws InvalidDataException with the archive path and
    // the reader's exception inside.
    [Test]
    public void Unpack_GarbageTarHeader_ThrowsWithArchivePathAndLeavesNoDestination()
    {
        string archive = Path.Combine(_folder.NewFolder(), "garbage.tar.zst");
        WriteFrame(archive, Pattern(1024));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(
            () => Archive.Unpack(archive, destination),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(archive).And.InnerException.TypeOf<InvalidDataException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // Bytes that are not a zstd frame throw the same exception type as a bad entry, with the archive path in the message.
    [Test]
    public void Unpack_CorruptArchive_ThrowsInvalidDataException()
    {
        string archive = Path.Combine(_folder.NewFolder(), "corrupt.tar.zst");
        File.WriteAllBytes(archive, Pattern(4096));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains(archive));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // The frame header descriptor after the 4-byte magic has bit 2 set, so every unpack checks the content.
    [Test]
    public void Pack_OneFile_SetsChecksumFlagInFrameHeader()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", Pattern(10));

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        Archive.Pack(source, archive);

        byte[] header = ReadFrameHeader(archive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(header.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { 0x28, 0xB5, 0x2F, 0xFD }));
            Assert.That(header[4] & 0x04, Is.Not.Zero);
        }
    }

    // 256 KB of noise is stored as raw blocks, so a flipped byte in the middle decodes with no zstd error and only the checksum catches it.
    [Test]
    public void Unpack_FlippedByte_ThrowsInvalidDataExceptionAndLeavesNoDestination()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "file.bin", Noise(256 * 1024));

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        Archive.Pack(source, archive);

        using (FileStream file = new(archive, FileMode.Open, FileAccess.ReadWrite))
        {
            file.Position = file.Length / 2;
            int value = file.ReadByte();
            file.Position--;
            file.WriteByte((byte)(value ^ 0x01));
        }

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => Archive.Unpack(archive, destination), Throws.TypeOf<InvalidDataException>().With.Message.Contains(archive));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // The checksum is optional in the frame format, so an archive without checksums still unpacks.
    [Test]
    public void Unpack_ArchiveWithoutChecksum_Unpacks()
    {
        byte[] bytes = Noise(4096);
        string archive = Path.Combine(_folder.NewFolder(), "plain.tar.zst");
        CraftArchive(archive, writer => writer.WriteEntry(NewFile("file.bin", bytes)));

        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(ReadFrameHeader(archive)[4] & 0x04, Is.Zero);

        Archive.Unpack(archive, destination);

        Assert.That(File.ReadAllBytes(Path.Combine(destination, "file.bin")), Is.EqualTo(bytes));
    }

    // An existing empty destination is filled in place.
    [Test]
    public void Unpack_ExistingEmptyDestination_FillsIt()
    {
        string source = _folder.NewFolder();
        WriteBytes(source, "a.txt", Pattern(10));

        string archive = Path.Combine(_folder.NewFolder(), "out.tar.zst");
        string destination = _folder.NewFolder();

        Archive.Pack(source, archive);
        Archive.Unpack(archive, destination);

        Assert.That(ReadAll(destination), Is.EqualTo(ReadAll(source)));
    }

    private static byte[] Pattern(int length)
    {
        byte[] bytes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static void WriteFrame(string path, byte[] bytes)
    {
        using FileStream file = new(path, FileMode.CreateNew);
        using CompressionStream compression = new(file, Archive.CompressionLevel);

        compression.Write(bytes);
    }

    private static byte[] ReadFrameHeader(string archive)
    {
        byte[] header = new byte[5];

        using var file = File.OpenRead(archive);
        file.ReadExactly(header);

        return header;
    }

    private static SortedDictionary<string, byte[]> ReadAll(string folder)
    {
        SortedDictionary<string, byte[]> files = [with(StringComparer.Ordinal)];

        foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
            files.Add(relativePath, File.ReadAllBytes(path));
        }

        return files;
    }
}
