using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class CodeImageEdgeTests
{
    private static readonly uint[] _firstAndThird = [0x1000, 0x7000];

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

    // A path with spaces, braces and non-ASCII characters reads like any other.
    [Test]
    public void Read_OddPath_ReturnsTheSection()
    {
        string folder = Path.Combine(_folder.Root, "バー {0} 1");
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "file ü.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Ret().Build());

        Assert.That(CodeImage.Read(path).Sections.Single().Bytes, Has.Length.EqualTo(1));
    }

    // A section without the execute flag stays out, even between 2 executable sections.
    [Test]
    public void Read_DataSectionBetweenCode_SkipsIt()
    {
        const int SectionHeadersAt = 0x138;
        const int SectionHeaderSize = 40;
        const int CharacteristicsAt = 36;

        string path = Path.Combine(_folder.Root, "file.bin");
        CodeImage written = new(
            false,
            CodeBuilder.DefaultBase,
            CodeBuilder.ImageSize,
            [
                new CodeBuilder(false).Ret().Section(),
                new CodeBuilder(false, rva: 0x5000).Ret().Section(),
                new CodeBuilder(false, rva: 0x7000).Ret().Section(),
            ]);

        CodeBuilder.WritePe(path, written);
        byte[] file = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(SectionHeadersAt + SectionHeaderSize + CharacteristicsAt),
            (uint)SectionCharacteristics.MemRead);
        File.WriteAllBytes(path, file);

        var image = CodeImage.Read(path);

        Assert.That(image.Sections.Select(static section => section.Rva), Is.EqualTo(_firstAndThird));
    }

    // An executable section with no bytes in the file comes back empty, so the search has nothing to decode.
    [Test]
    public void Read_ExecutableSectionWithoutBytes_ReturnsItEmpty()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Build());

        Assert.That(CodeImage.Read(path).Sections.Single().Bytes, Is.Empty);
    }

    // A file that ends inside its headers is no PE file and the message has its path.
    [Test]
    public void Read_FileCutInsideTheHeaders_ThrowsWithThePath()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Ret().Build());
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..0x60]);

        Assert.That(() => CodeImage.Read(path), Throws.TypeOf<BadImageFormatException>().With.Message.Contain(path));
    }
}
