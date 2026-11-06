using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkSectionsCutFileTests
{
    private const int SectionHeaderSize = 40;

    private static readonly string _testAssembly = typeof(UdkSectionsCutFileTests).Assembly.Location;

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

    // A PE file cut off inside its headers fails with its path at every step of the read and does not throw.
    [Test]
    public void Read_FileCutInsideItsHeaders_FailsWithPath()
    {
        byte[] image = File.ReadAllBytes(_testAssembly);
        string file = Path.Combine(_folder.Root, "file.bin");

        foreach (int length in CutPositions(image))
        {
            File.WriteAllBytes(file, image.AsSpan(0, length).ToArray());

            Assert.That(
                UdkSections.Read(file, new FakeMemory(), new Address(0x0040_0000)).UnwrapErr().Message,
                Is.EqualTo(SR.FormatExecutableNotPe(file)),
                $"length: {length}");
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
