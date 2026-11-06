using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkSectionsTests
{
    private const SectionCharacteristics Executable = SectionCharacteristics.MemRead | SectionCharacteristics.MemExecute;
    private const byte Fill = 0xAB;
    private const int Least = 16;
    private const int Most = 100;
    private const SectionCharacteristics ReadOnly = SectionCharacteristics.MemRead;
    private const uint Rva = 0x1000;
    private const SectionCharacteristics Writable = SectionCharacteristics.MemRead | SectionCharacteristics.MemWrite;

    private static readonly Address _base = new(0x0040_0000);
    private static readonly Address _data = new(0x0200_0000);
    private static readonly string _testAssembly = typeof(UdkSectionsTests).Assembly.Location;

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

    // Only the 2nd page of a section reads, so the 1st stays zeros and the bytes of the 2nd stay at their position.
    [Test]
    public void Read_SecondPageReadable_KeepsItAtItsPosition()
    {
        string file = WritePe((Rva, 0x2000, ReadOnly));
        FakeMemory memory = new();
        memory.Add(_base + Rva + 0x1000, Filled(0x1000));

        byte[] bytes = UdkSections.Read(file, memory, _base).Unwrap()[0].Bytes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes.Take(0x1000), Is.All.Zero);
            Assert.That(bytes.Skip(0x1000), Is.All.EqualTo(Fill));
        }
    }

    // The last read stops at the end of the section, because the page behind a section can be unreadable.
    [Test]
    public void Read_SectionEndsInsideAPage_ReadsTheShortRest()
    {
        string file = WritePe((Rva, 0x1800, ReadOnly));
        FakeMemory memory = new();
        memory.Add(_base + Rva, Filled(0x1800));

        byte[] bytes = UdkSections.Read(file, memory, _base).Unwrap()[0].Bytes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes, Has.Length.EqualTo(0x1800));
            Assert.That(bytes, Is.All.EqualTo(Fill));
        }
    }

    // Every section without code comes back in file order and an empty section stays in the list.
    [Test]
    public void Read_ThreeSections_SkipsCodeAndKeepsFileOrder()
    {
        string file = WritePe((0x1000, 0x200, Executable), (0x2000, 0, ReadOnly), (0x3000, 0x200, Writable));

        var sections = UdkSections.Read(file, new FakeMemory(), _base).Unwrap();

        uint[] expected = [0x2000, 0x3000];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.Select(static section => section.Rva), Is.EqualTo(expected));
            Assert.That(sections[0].Bytes, Is.Empty);
            Assert.That(sections[1].Bytes, Has.Length.EqualTo(0x200));
        }
    }

    // A section flagged as executable and writable has code, so it stays out.
    [Test]
    public void Read_ExecutableAndWritableSection_SkipsIt()
    {
        string file = WritePe((Rva, 0x200, Executable | Writable));

        Assert.That(UdkSections.Read(file, new FakeMemory(), _base).Unwrap(), Is.Empty);
    }

    // A file with no sections has nothing to search.
    [Test]
    public void Read_NoSections_ReturnsNothing()
    {
        Assert.That(UdkSections.Read(WritePe(), new FakeMemory(), _base).Unwrap(), Is.Empty);
    }

    // A path with spaces and letters outside ASCII reads like any other.
    [Test]
    public void Read_PathWithSpacesAndUnicode_Reads()
    {
        string folder = Path.Combine(_folder.Root, "pl\u00E4yer \u30D5\u30A9\u30EB\u30C0");
        _ = Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "file.bin");
        File.Copy(WritePe((Rva, 0x200, ReadOnly)), file);

        Assert.That(UdkSections.Read(file, new FakeMemory(), _base).Unwrap(), Has.Count.EqualTo(1));
    }

    // A file that is not a PE file fails with its path.
    [TestCase(0)]
    [TestCase(16)]
    [TestCase(0x1000)]
    public void Read_FileThatIsNoPeFile_FailsWithPath(int length)
    {
        string file = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(file, new byte[length]);

        Assert.That(UdkSections.Read(file, new FakeMemory(), _base).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNotPe(file)));
    }

    // A data pointer of 0 is no array.
    [Test]
    public void Arrays_DataPointerZero_IsNoArray()
    {
        byte[] bytes = new byte[0x20];
        WriteHeader(bytes, 0, Least, Least);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);

        Assert.That(Arrays(bytes), Is.Empty);
    }

    // A count that reads as negative or as the largest int stays out of the range.
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void Arrays_CountFarOutOfRange_IsNoArray(int count)
    {
        byte[] bytes = new byte[0x20];
        WriteHeader(bytes, 0, count, int.MaxValue);

        Assert.That(Arrays(bytes), Is.Empty);
    }

    // An empty list and an empty section have nothing to list.
    [Test]
    public void Arrays_NoBytes_ListsNothing()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UdkSections.Arrays([], MemoryLayout.X86, Least, Most), Is.Empty);
            Assert.That(Arrays([]), Is.Empty);
        }
    }

    // A header at the exact end of a section counts, because its capacity is the last int of the section.
    [Test]
    public void Arrays_HeaderEndsAtSectionEnd_FindsIt()
    {
        byte[] bytes = new byte[12];
        WriteHeader(bytes, 0, Least, Least);

        Assert.That(Arrays(bytes).Count(), Is.EqualTo(1));
    }

    // Bytes too short for a pointer list nothing and the tail behind the last pointer does not throw.
    [Test]
    public void Pointers_SectionLengthNotAligned_SkipsTheTail()
    {
        byte[] bytes = new byte[7];
        bytes.AsSpan().Fill(0x11);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(UdkSections.Pointers([new UdkSection(Rva, bytes)], MemoryLayout.X86).Count(), Is.EqualTo(1));
            Assert.That(UdkSections.Pointers([new UdkSection(Rva, bytes)], MemoryLayout.X64), Is.Empty);
            Assert.That(UdkSections.Pointers([new UdkSection(Rva, new byte[3])], MemoryLayout.X86), Is.Empty);
        }
    }

    // A pointer above the signed 4-byte range comes back positive, because large-address-aware players use that memory.
    [Test]
    public void Pointers_HighX86Pointer_DoesNotSignExtend()
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xF000_0000);

        (uint, Address)[] expected = [(Rva, new Address(0xF000_0000))];

        Assert.That(UdkSections.Pointers([new UdkSection(Rva, bytes)], MemoryLayout.X86), Is.EqualTo(expected));
    }

    // The sections of a real PE file come back without its code, each at the RVA of the section table.
    [Test]
    public void Read_TestAssembly_SkipsExecutableSections()
    {
        using PEReader reader = new(File.OpenRead(_testAssembly));
        var headers = reader.PEHeaders.SectionHeaders;
        uint[] expected =
        [
            .. headers
                .Where(static section => (section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0)
                .Select(static section => (uint)section.VirtualAddress),
        ];

        var sections = UdkSections.Read(_testAssembly, new FakeMemory(), _base).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.Select(static section => section.Rva), Is.EqualTo(expected));
            Assert.That(sections, Is.Not.Empty);
        }
    }

    // A page that fails stays zeros and does not throw, because a section often has pages the process has not committed.
    [Test]
    public void Read_MemoryWithNoPages_KeepsZeros()
    {
        var sections = UdkSections.Read(_testAssembly, new FakeMemory(), _base).Unwrap();

        Assert.That(sections.SelectMany(static section => section.Bytes), Is.All.Zero);
    }

    // A readable page comes back from the base plus the RVA of its section.
    [Test]
    public void Read_OnePageReadable_FillsOnlyThatPage()
    {
        using PEReader reader = new(File.OpenRead(_testAssembly));
        var section = reader.PEHeaders.SectionHeaders.First(
            static section => (section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0);
        byte[] page = new byte[Math.Min(section.VirtualSize, 4096)];
        page.AsSpan().Fill(0xAB);
        FakeMemory memory = new();
        memory.Add(_base + section.VirtualAddress, page);

        var bytes = UdkSections.Read(_testAssembly, memory, _base).Unwrap().First(read => read.Rva == (uint)section.VirtualAddress);

        Assert.That(bytes.Bytes.Take(page.Length), Is.All.EqualTo((byte)0xAB));
    }

    // A file that does not exist fails with its path, because the caller writes the message into the row.
    [Test]
    public void Read_MissingFile_FailsWithThePath()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-file.bin");

        Assert.That(UdkSections.Read(missing, new FakeMemory(), _base).UnwrapErr().Message, Does.StartWith($"Can't read '{missing}': "));
    }

    // A file that another program has open fails with its path and the message of the open.
    [Test]
    public void Read_FileOpenInAnotherProgram_FailsWithThePath()
    {
        string file = WritePe();

        using FileStream locked = new(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(UdkSections.Read(file, new FakeMemory(), _base).UnwrapErr().Message, Does.StartWith($"Can't read '{file}': "));
    }

    // A header at 2 does not count because it is not pointer-aligned. The same header at 4 does.
    [Test]
    public void Arrays_HeaderAtEveryAlignment_FindsOnlyAlignedHeaders()
    {
        byte[] misaligned = new byte[0x20];
        WriteHeader(misaligned, 2, Least, Least);
        byte[] aligned = new byte[0x20];
        WriteHeader(aligned, 4, Least, Least);
        (uint, Address, int)[] expected = [(Rva + 4, _data, Least)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Arrays(misaligned), Is.Empty);
            Assert.That(Arrays(aligned), Is.EqualTo(expected));
        }
    }

    // A capacity below the count is no array, because an engine array never has more entries than room.
    [Test]
    public void Arrays_CapacityBelowCount_IsNoArray()
    {
        byte[] bytes = new byte[0x20];
        WriteHeader(bytes, 0, Least + 1, Least);

        Assert.That(Arrays(bytes), Is.Empty);
    }

    // Both ends of the range count and the numbers next to them do not.
    [TestCase(Least - 1, false)]
    [TestCase(Least, true)]
    [TestCase(Most, true)]
    [TestCase(Most + 1, false)]
    public void Arrays_CountAroundRange_FindsOnlyInRange(int count, bool found)
    {
        byte[] bytes = new byte[0x20];
        WriteHeader(bytes, 0, count, count);

        Assert.That(Arrays(bytes).Any(), Is.EqualTo(found));
    }

    // A header at the end of a section with its capacity past the end does not throw.
    [Test]
    public void Arrays_HeaderCutOffAtSectionEnd_IsNoArray()
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)_data.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), Least);

        Assert.That(Arrays(bytes), Is.Empty);
    }

    // On x64 a header is an 8-byte pointer and then 2 ints, at positions 8 apart, with no pointer read cut to 4 bytes.
    [Test]
    public void Arrays_X64_ReadsEightBytePointers()
    {
        var data = new Address(0x7FF6_0000_0000);
        byte[] bytes = new byte[0x20];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), data.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), Least);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), Least);

        var found = UdkSections.Arrays([new UdkSection(Rva, bytes)], MemoryLayout.X64, Least, Most).ToList();

        (uint, Address, int)[] expected = [(Rva + 8, data, Least)];

        Assert.That(found, Is.EqualTo(expected));
    }

    // Every value that is not 0 comes back with its RVA across sections, in the order of the sections.
    [Test]
    public void Pointers_TwoSections_ListsEveryValueWithItsRva()
    {
        byte[] first = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(first.AsSpan(4), 0x1234);
        byte[] second = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(second, 0x5678);

        var pointers = UdkSections.Pointers([new UdkSection(Rva, first), new UdkSection(0x2000, second)], MemoryLayout.X86);

        (uint, Address)[] expected = [(Rva + 4, new Address(0x1234)), (0x2000u, new Address(0x5678))];

        Assert.That(pointers, Is.EqualTo(expected));
    }

    private static IEnumerable<(uint Rva, Address Data, int Count)> Arrays(byte[] bytes)
    {
        return UdkSections.Arrays([new UdkSection(Rva, bytes)], MemoryLayout.X86, Least, Most);
    }

    private static byte[] Filled(int length)
    {
        byte[] bytes = new byte[length];
        bytes.AsSpan().Fill(Fill);

        return bytes;
    }

    private static void WriteHeader(byte[] bytes, int at, int count, int capacity)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), (uint)_data.Value);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 4), count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 8), capacity);
    }

    private string WritePe(params (uint Rva, int VirtualSize, SectionCharacteristics Flags)[] sections)
    {
        const int CoffAt = 0x44;
        const int OptionalSize = 0xE0;
        const int SectionsAt = CoffAt + 20 + OptionalSize;

        byte[] image = new byte[SectionsAt + (sections.Length * 40)];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), CoffAt - 4);
        image[CoffAt - 4] = (byte)'P';
        image[CoffAt - 3] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(CoffAt), 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(CoffAt + 2), (ushort)sections.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(CoffAt + 16), OptionalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(CoffAt + 20), 0x10B);

        for (int index = 0; index < sections.Length; index++)
        {
            int at = SectionsAt + (index * 40);
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(at + 8), sections[index].VirtualSize);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + 12), sections[index].Rva);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(at + 36), (uint)sections[index].Flags);
        }

        string file = Path.Combine(_folder.Root, "image.bin");
        File.WriteAllBytes(file, image);

        return file;
    }
}
