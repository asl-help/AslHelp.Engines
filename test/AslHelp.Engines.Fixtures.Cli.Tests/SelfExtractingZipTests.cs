using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class SelfExtractingZipTests
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

    // The offsets in the zip count from the end of the stub, which a plain zip reader cannot open, so the entries open as written.
    [Test]
    public void Extract_ZipAfterStub_WritesEveryFile()
    {
        string path = WriteSelfExtractingZip(Stub(4096), ("player.exe", [1, 2, 3]), ("data/file.bin", [4, 5]));
        string destination = Path.Combine(_folder.Root, "destination");

        SelfExtractingZip.Extract(path, destination);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => ReadEntryCount(path), Throws.InstanceOf<InvalidDataException>());
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "player.exe")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "data", "file.bin")), Is.EqualTo(new byte[] { 4, 5 }));
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
        }
    }

    // A plain zip has a stub of 0 bytes.
    [Test]
    public void Extract_ZipWithoutStub_WritesEveryFile()
    {
        string path = WriteSelfExtractingZip([], ("player.exe", [1]));
        string destination = Path.Combine(_folder.Root, "destination");

        SelfExtractingZip.Extract(path, destination);

        Assert.That(File.ReadAllBytes(Path.Combine(destination, "player.exe")), Is.EqualTo(new byte[] { 1 }));
    }

    // 65535 entries make the writer add the zip64 records, whose offsets also count from the end of the stub.
    [Test]
    public void Open_Zip64AfterStub_ListsEveryEntry()
    {
        const int Count = ushort.MaxValue;
        string path = Path.Combine(_folder.Root, "player-setup.exe");

        using (MemoryStream zipBytes = new())
        {
            using (ZipArchive zip = new(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                for (int i = 0; i < Count; i++)
                {
                    _ = zip.CreateEntry($"{i}", CompressionLevel.NoCompression);
                }
            }

            File.WriteAllBytes(path, [.. Stub(4096), .. zipBytes.ToArray()]);
        }

        using var archive = SelfExtractingZip.Open(path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(archive.Entries, Has.Count.EqualTo(Count));
            Assert.That(archive.Entries[^1].FullName, Is.EqualTo($"{Count - 1}"));
        }
    }

    // A file with no end of central directory record is refused.
    [Test]
    public void Open_NotAZip_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_folder.Root, "player-setup.exe");
        File.WriteAllBytes(path, Stub(4096));

        Assert.That(
            () => SelfExtractingZip.Open(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // Offsets that point past the start of the file are refused instead of read as a negative stub.
    [Test]
    public void Open_OffsetsPastStart_ThrowsInvalidDataException()
    {
        string zip = WriteSelfExtractingZip(Stub(4096), ("player.exe", [1]));
        string path = Path.Combine(_folder.Root, "cut.exe");
        File.WriteAllBytes(path, File.ReadAllBytes(zip)[4100..]);

        Assert.That(
            () => SelfExtractingZip.Open(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // Zip64 sizes past the range of the math are refused as damaged instead of overflowing.
    [TestCase(long.MinValue, 0L)]
    [TestCase(long.MaxValue, long.MaxValue)]
    public void Open_Zip64OffsetsPastRange_ThrowsInvalidDataException(long directorySize, long directoryOffset)
    {
        string path = WriteZip64Tail(directorySize, directoryOffset, extensibleData: 0);

        Assert.That(
            () => SelfExtractingZip.Open(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // A zip64 end record with extensible data is refused, because its start cannot be found from the locator.
    [Test]
    public void Open_Zip64EndRecordWithExtensibleData_ThrowsInvalidDataException()
    {
        string path = WriteZip64Tail(directorySize: 0, directoryOffset: 0, extensibleData: 8);

        Assert.That(
            () => SelfExtractingZip.Open(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // An existing destination is refused, so an earlier extraction is never mixed with this one.
    [Test]
    public void Extract_ExistingDestination_ThrowsIOException()
    {
        string path = WriteSelfExtractingZip(Stub(16), ("player.exe", [1]));
        string destination = _folder.NewFolder();

        Assert.That(
            () => SelfExtractingZip.Extract(path, destination),
            Throws.TypeOf<IOException>().With.Message.Contains(destination));
    }

    // A temp folder from a crashed run is deleted first, so its files do not end up in the destination.
    [Test]
    public void Extract_LeftoverTempFolder_DeletesItFirst()
    {
        string path = WriteSelfExtractingZip(Stub(16), ("player.exe", [1]));
        string destination = Path.Combine(_folder.Root, "destination");
        WriteBytes(destination + ".partial", "stale.bin", [9]);

        SelfExtractingZip.Extract(path, destination);

        string[] expected = ["player.exe"];
        Assert.That(Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Select(Path.GetFileName), Is.EqualTo(expected));
    }

    // An entry that resolves outside the destination fails the extraction and leaves no destination behind.
    [Test]
    public void Extract_EntryOutsideDestination_ThrowsAndLeavesNoFolder()
    {
        string path = WriteSelfExtractingZip(Stub(16), ("../outside.bin", [1]));
        string destination = Path.Combine(_folder.Root, "destination");

        Assert.That(() => SelfExtractingZip.Extract(path, destination), Throws.InstanceOf<IOException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(Directory.Exists(destination + ".partial"), Is.False);
            Assert.That(File.Exists(Path.Combine(_folder.Root, "outside.bin")), Is.False);
        }
    }

    // Write the zip on its own before the stub goes in front, so its offsets count from the end of the stub as in an installer.
    private string WriteSelfExtractingZip(byte[] stub, params (string Name, byte[] Bytes)[] files)
    {
        string path = Path.Combine(_folder.Root, "player-setup.exe");

        using MemoryStream zipBytes = new();

        using (ZipArchive zip = new(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in files)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        File.WriteAllBytes(path, [.. stub, .. zipBytes.ToArray()]);

        return path;
    }

    // Write only the records that Open reads at the end of a zip64 file: the zip64 end record, its locator and the end record.
    private string WriteZip64Tail(long directorySize, long directoryOffset, int extensibleData)
    {
        string path = Path.Combine(_folder.Root, "player-setup.exe");
        byte[] tail = new byte[56 + extensibleData + 20 + 22];
        var record = tail.AsSpan(0, 56 + extensibleData);
        var locator = tail.AsSpan(record.Length, 20);
        var end = tail.AsSpan(record.Length + 20);

        "PK\u0006\u0006"u8.CopyTo(record);
        BinaryPrimitives.WriteInt64LittleEndian(record[4..], 44 + extensibleData);
        BinaryPrimitives.WriteInt64LittleEndian(record[40..], directorySize);
        BinaryPrimitives.WriteInt64LittleEndian(record[48..], directoryOffset);

        "PK\u0006\u0007"u8.CopyTo(locator);
        BinaryPrimitives.WriteInt32LittleEndian(locator[16..], 1);

        "PK\u0005\u0006"u8.CopyTo(end);
        end[8..20].Fill(0xFF);

        File.WriteAllBytes(path, [.. Stub(64), .. tail]);

        return path;
    }

    // Read the entries with a plain zip reader, which reads the central directory only on the 1st access.
    private static int ReadEntryCount(string path)
    {
        using ZipArchive zip = new(File.OpenRead(path));

        return zip.Entries.Count;
    }

    // Fill the stub with zip signatures, so a reader that searches for them instead of following the offsets fails.
    private static byte[] Stub(int length)
    {
        byte[] stub = new byte[length];

        for (int i = 0; i + 4 <= length; i += 4)
        {
            "PK\u0003\u0004"u8.CopyTo(stub.AsSpan(i));
        }

        return stub;
    }
}
