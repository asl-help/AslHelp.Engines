using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class PeImportsTests
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

    // A hand-made PE32 that imports file.dll then other.dll gives both names in table order.
    [Test]
    public void Read_FakeExecutable_ListsImportsInTableOrder()
    {
        string path = Path.Combine(_folder.Root, "player.exe");
        FakeExecutable.Write(path, "file.dll", "other.dll");

        Assert.That(PeImports.Read(path), Is.EqualTo(["file.dll", "other.dll"]));
    }

    // A PE32 with a zero import directory gives an empty list instead of throwing.
    [Test]
    public void Read_NoImportDirectory_ReturnsEmpty()
    {
        string path = Path.Combine(_folder.Root, "player.exe");
        FakeExecutable.Write(path);

        Assert.That(PeImports.Read(path), Is.Empty);
    }

    // Bytes that are not a PE file throw with the path, so the message points at the file to replace.
    [Test]
    public void Read_NotAnExecutable_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(path, TestFolder.Noise(1024));

        Assert.That(
            () => PeImports.Read(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // A descriptor whose name RVA points outside every section throws with the path instead of reading past the file.
    [Test]
    public void Read_NameOutsideEverySection_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_folder.Root, "player.exe");
        FakeExecutable.Write(path, "file.dll");

        byte[] bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(FakeExecutable.ImportTableOffset + FakeExecutable.DescriptorNameOffset), 0x9000);
        File.WriteAllBytes(path, bytes);

        Assert.That(
            () => PeImports.Read(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // A section whose raw data pointer is negative or near the end of the int range throws with the path, so a crafted header
    // never escapes as another exception.
    [TestCase(-4096)]
    [TestCase(0x7FFFFFF0)]
    public void Read_SectionPointerOutOfRange_ThrowsInvalidDataException(int rawDataOffset)
    {
        string path = Path.Combine(_folder.Root, "player.exe");
        FakeExecutable.Write(path, "file.dll");

        byte[] bytes = File.ReadAllBytes(path);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(FakeExecutable.SectionPointerOffset), rawDataOffset);
        File.WriteAllBytes(path, bytes);

        Assert.That(
            () => PeImports.Read(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // A table whose null descriptor has a name RVA runs past its directory size, so it throws instead of reading the bytes after it as names.
    [Test]
    public void Read_NoNullDescriptor_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_folder.Root, "player.exe");
        FakeExecutable.Write(path, "file.dll");

        byte[] bytes = File.ReadAllBytes(path);
        var descriptors = bytes.AsSpan(FakeExecutable.ImportTableOffset);
        int nameRva = BinaryPrimitives.ReadInt32LittleEndian(descriptors[FakeExecutable.DescriptorNameOffset..]);
        BinaryPrimitives.WriteInt32LittleEndian(descriptors[(FakeExecutable.DescriptorSize + FakeExecutable.DescriptorNameOffset)..], nameRva);
        File.WriteAllBytes(path, bytes);

        Assert.That(
            () => PeImports.Read(path),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(path));
    }

    // A missing file throws FileNotFoundException, so it differs from the InvalidDataException of a damaged one.
    [Test]
    public void Read_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.That(
            () => PeImports.Read(Path.Combine(_folder.Root, "player.exe")),
            Throws.TypeOf<FileNotFoundException>());
    }

    // ping.exe from System32 gives DLL names only, so the reader works on a real linker layout.
    [Test]
    [Platform("Win")]
    public void Read_SystemExecutable_ListsDllNames()
    {
        var imports = PeImports.Read(Path.Combine(Environment.SystemDirectory, "PING.EXE"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(imports, Is.Not.Empty);
            Assert.That(imports, Has.All.EndsWith(".dll").IgnoreCase);
        }
    }
}
