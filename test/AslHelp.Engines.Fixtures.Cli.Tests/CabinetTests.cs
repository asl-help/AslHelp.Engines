using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class CabinetTests
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

    // A names-only cabinet with file.bin and other.bin gives both in cabinet order.
    [Test]
    public void ReadNames_FakeCabinet_ListsEveryName()
    {
        using MemoryStream cabinet = new(FakeCabinet.Names("file.bin", "other.bin"));

        Assert.That(Cabinet.ReadNames(cabinet), Is.EqualTo(["file.bin", "other.bin"]));
    }

    // The same bytes through a zip entry stream, which cannot seek, give the same names, so the reader is forward-only.
    [Test]
    public void ReadNames_ZipEntryStream_ListsEveryName()
    {
        using MemoryStream zip = new();

        using (ZipArchive writer = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = writer.CreateEntry("files.cab").Open())
        {
            entry.Write(FakeCabinet.Names("file.bin", "other.bin"));
        }

        zip.Position = 0;
        using ZipArchive reader = new(zip, ZipArchiveMode.Read);
        using var cabinet = reader.GetEntry("files.cab")!.Open();

        Assert.That(Cabinet.ReadNames(cabinet), Is.EqualTo(["file.bin", "other.bin"]));
    }

    // Bytes that do not start with MSCF are refused.
    [Test]
    public void ReadNames_WrongSignature_ThrowsInvalidDataException()
    {
        byte[] bytes = FakeCabinet.Names("file.bin");
        bytes[0] = (byte)'X';

        using MemoryStream cabinet = new(bytes);

        Assert.That(() => Cabinet.ReadNames(cabinet), Throws.TypeOf<InvalidDataException>());
    }

    // A header that counts 3 files over 1 entry throws InvalidDataException and not EndOfStreamException.
    [Test]
    public void ReadNames_CutShort_ThrowsInvalidDataException()
    {
        byte[] bytes = FakeCabinet.Names("file.bin");
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 3);

        using MemoryStream cabinet = new(bytes);

        Assert.That(() => Cabinet.ReadNames(cabinet), Throws.TypeOf<InvalidDataException>());
    }

    // A cabinet from makecab lists its source file names in order, so the header offsets match a real cabinet.
    [Test]
    [Platform("Win")]
    public void ReadNames_MakecabCabinet_ListsEveryName()
    {
        string sources = _folder.NewFolder();
        WriteBytes(sources, "file.bin", [1, 2, 3]);
        WriteBytes(sources, "other.bin", [4]);
        string path = Path.Combine(_folder.NewFolder(), "files.cab");
        FakeCabinet.Make(path, Path.Combine(sources, "file.bin"), Path.Combine(sources, "other.bin"));

        using var cabinet = File.OpenRead(path);

        Assert.That(Cabinet.ReadNames(cabinet), Is.EqualTo(["file.bin", "other.bin"]));
    }
}
