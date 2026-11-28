using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class CodeImageTests
{
    private static readonly uint[] _headersAndData = [0, 0x3000];

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

    // The test assembly is a real PE file, so its executable sections, base and machine come from real headers.
    [Test]
    public void Read_TestAssembly_ReturnsExecutableSectionsBaseAndMachine()
    {
        string path = typeof(CodeImageTests).Assembly.Location;
        using PEReader reader = new(File.OpenRead(path));
        var headers = reader.PEHeaders;
        uint[] expected =
        [
            .. headers.SectionHeaders
                .Where(static section => (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0)
                .Select(static section => (uint)section.VirtualAddress),
        ];

        using var image = CodeImage.Read(path).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Sections.Select(static section => section.Rva), Is.EqualTo(expected));
            Assert.That(image.Sections, Is.Not.Empty);
            Assert.That(image.Sections[0].Bytes.IsEmpty, Is.False);
            Assert.That(image.ImageBase, Is.EqualTo(headers.PEHeader!.ImageBase));
            Assert.That(image.ImageSize, Is.EqualTo((uint)headers.PEHeader.SizeOfImage));
            Assert.That(image.Is64Bit, Is.EqualTo(headers.CoffHeader.Machine == Machine.Amd64));
        }
    }

    // The bytes of a section come back from the file, at the base and machine that the headers list, for both PE formats.
    [TestCase(false, 0x0040_0000ul)]
    [TestCase(true, 0x0001_4000_0000ul)]
    public void Read_BuiltFile_ReturnsTheSectionBytes(bool is64Bit, ulong imageBase)
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        var written = new CodeBuilder(is64Bit, imageBase).Fillers(3).Ret().Build();
        CodeBuilder.WritePe(path, written);

        using var image = CodeImage.Read(path).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Is64Bit, Is.EqualTo(is64Bit));
            Assert.That(image.ImageBase, Is.EqualTo(imageBase));
            Assert.That(image.ImageSize, Is.EqualTo(CodeBuilder.ImageSize));
            Assert.That(image.Sections.Single().Rva, Is.EqualTo(CodeBuilder.CodeRva));
            Assert.That(image.Sections.Single().Bytes.ToArray(), Is.EqualTo(written.Sections[0].Bytes.ToArray()));
        }
    }

    // The headers and the sections without code come back as data, the headers at RVA 0 with the size that the headers list.
    [Test]
    public void Read_BuiltFileWithData_ReturnsHeadersAndDataSections()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        var data = new CodeBuilder(false, rva: 0x3000).Fillers(5).Section();
        CodeBuilder.WritePe(path, new CodeBuilder(false).Ret().Build(data));

        using PEReader reader = new(File.OpenRead(path));
        using var image = CodeImage.Read(path).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Sections.Single().Rva, Is.EqualTo(CodeBuilder.CodeRva));
            Assert.That(image.Data.Select(static part => part.Rva), Is.EqualTo(_headersAndData));
            Assert.That(image.Data[0].Bytes, Has.Length.EqualTo(reader.PEHeaders.PEHeader!.SizeOfHeaders));
            Assert.That(image.Data[0].Bytes[..2].ToArray(), Is.EqualTo("MZ"u8.ToArray()));
            Assert.That(image.Data[1].Bytes.ToArray(), Is.EqualTo(data.Bytes.ToArray()));
        }
    }

    // A file at the size that PEReader maps stays open until the image is disposed and then closes, so the file can be deleted.
    [Test]
    public void Dispose_MappedFile_ReleasesTheFile()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Fillers(0x10000).Ret().Build());

        var image = CodeImage.Read(path).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => File.Delete(path), Throws.InstanceOf<IOException>());
            Assert.That(image.Sections.Single().Bytes.Length, Is.EqualTo(0x10001));
            Assert.That(image.Sections.Single().Bytes.Span[^1], Is.EqualTo(0xC3));
        }

        image.Dispose();

        Assert.That(() => File.Delete(path), Throws.Nothing);
    }

    // The bytes of a disposed image throw instead of reading freed memory, for the code and for the data.
    [Test]
    public void Dispose_ThenBytesRead_Throws()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Ret().Build(new CodeBuilder(false, rva: 0x3000).Fillers(5).Section()));

        var image = CodeImage.Read(path).Unwrap();
        var code = image.Sections.Single().Bytes;
        var data = image.Data[1].Bytes;

        image.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => code.Span[0], Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => data.ToArray(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(code.Pin, Throws.TypeOf<ObjectDisposedException>());
        }
    }

    // A machine other than x86 or x64 fails with the path, because the search decodes only those 2.
    [Test]
    public void Read_Arm64Machine_FailsWithThePath()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(true).Ret().Build(), Machine.Arm64);

        Assert.That(CodeImage.Read(path).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableMachineUnsupported(path)));
    }
}
