using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class CodeImageTests
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

        var image = CodeImage.Read(path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Sections.Select(static section => section.Rva), Is.EqualTo(expected));
            Assert.That(image.Sections, Is.Not.Empty);
            Assert.That(image.Sections[0].Bytes, Is.Not.Empty);
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
        var written = new CodeBuilder(is64Bit, imageBase).Nops(3).Ret().Build();
        CodeBuilder.WritePe(path, written);

        var image = CodeImage.Read(path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Is64Bit, Is.EqualTo(is64Bit));
            Assert.That(image.ImageBase, Is.EqualTo(imageBase));
            Assert.That(image.ImageSize, Is.EqualTo(CodeBuilder.ImageSize));
            Assert.That(image.Sections.Single().Rva, Is.EqualTo(CodeBuilder.CodeRva));
            Assert.That(image.Sections.Single().Bytes, Is.EqualTo(written.Sections[0].Bytes));
        }
    }

    // A file that is not a PE file fails with its path, because the reason ends up in the report without any other context.
    [TestCase(0)]
    [TestCase(16)]
    [TestCase(0x1000)]
    public void Read_FileThatIsNoPeFile_ThrowsBadImageFormat(int length)
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(path, new byte[length]);

        Assert.That(() => CodeImage.Read(path), Throws.TypeOf<BadImageFormatException>().With.Message.Contain(path));
    }

    // A machine other than x86 or x64 fails with the path, because the search decodes only those 2.
    [Test]
    public void Read_Arm64Machine_ThrowsBadImageFormat()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(true).Ret().Build(), Machine.Arm64);

        Assert.That(() => CodeImage.Read(path), Throws.TypeOf<BadImageFormatException>().With.Message.Contain(path));
    }

    // A missing file fails with its path.
    [Test]
    public void Read_MissingFile_ThrowsFileNotFound()
    {
        string path = Path.Combine(_folder.Root, "missing.bin");

        Assert.That(() => CodeImage.Read(path), Throws.TypeOf<FileNotFoundException>().With.Message.Contain(path));
    }
}
