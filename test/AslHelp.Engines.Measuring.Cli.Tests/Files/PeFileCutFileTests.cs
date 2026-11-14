using AslHelp.Engines.Measuring.Cli.Files;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Files;

public sealed class PeFileCutFileTests
{
    private const int SectionHeaderSize = 40;

    private static readonly string _testAssembly = typeof(PeFileCutFileTests).Assembly.Location;

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

    // A PE file cut off inside its headers fails as no PE file at every cut and does not throw.
    [Test]
    public void Open_FileCutInsideItsHeaders_FailsAsNotPe()
    {
        byte[] image = File.ReadAllBytes(_testAssembly);
        string file = Path.Combine(_folder.Root, "file.bin");

        foreach (int length in CutPositions(image))
        {
            File.WriteAllBytes(file, image.AsSpan(0, length).ToArray());

            Assert.That(PeFile.Open(file).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNotPe(file)), $"length: {length}");
        }
    }

    private static IEnumerable<int> CutPositions(byte[] image)
    {
        using PEReader reader = new(new MemoryStream(image));
        var headers = reader.PEHeaders;
        int sections = headers.PEHeaderStartOffset + headers.CoffHeader.SizeOfOptionalHeader;

        yield return 2;
        yield return 0x3C;
        yield return 0x40;
        yield return headers.CoffHeaderStartOffset - 2;
        yield return headers.CoffHeaderStartOffset + 4;
        yield return headers.PEHeaderStartOffset;
        yield return headers.PEHeaderStartOffset + 2;
        yield return sections;
        yield return sections + SectionHeaderSize - 1;
        yield return sections + (headers.CoffHeader.NumberOfSections * SectionHeaderSize) - 1;
    }
}
