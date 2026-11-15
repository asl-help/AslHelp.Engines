using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class ExecutableIdentityEdgeTests
{
    private const int DataPointerAt = 24;
    private const int DataRvaAt = 20;
    private const int DataSizeAt = 16;
    private const int EntrySize = 28;
    private const int SectionRawAt = 0x200;
    private const uint SectionRva = CodeBuilder.CodeRva;
    private const int TypeAt = 12;

    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly Guid _otherGuid = new("11111111-2222-3333-4444-555555555555");

    private TestFolder _folder = null!;

    private string FilePath => Path.Combine(_folder.Root, "file.bin");

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

    // The CodeView entry reads the same from a 32-bit and a 64-bit optional header.
    [TestCase(true)]
    [TestCase(false)]
    public void Read_ImageOfEitherArch_ReturnsGuidAgeAndName(bool is64Bit)
    {
        WriteImage(FilePath, is64Bit, CodeViewEntry(_guid, 7, "C:\\out\\file.pdb"));

        var identity = ExecutableIdentity.Read(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.Guid, Is.EqualTo(_guid));
            Assert.That(identity.Age, Is.EqualTo(7));
            Assert.That(identity.PdbName, Is.EqualTo("C:\\out\\file.pdb"));
        }
    }

    // A PDB and an image of 1 build pass, read from their files.
    [Test]
    public void Check_FilesOfOneBuild_Passes()
    {
        string pdbPath = Path.Combine(_folder.Root, "file.pdb");
        new FakePdb(_guid, [0x1000]) { DbiAge = 7 }.Write(pdbPath);
        WriteImage(FilePath, true, CodeViewEntry(_guid, 7, "file.pdb"));

        using var pdb = PdbFile.Open(pdbPath).Unwrap();

        Assert.That(pdb.Identity.Check(ExecutableIdentity.Read(FilePath).Unwrap()).IsOk, Is.True);
    }

    // An image of another build fails with the values of both files.
    [Test]
    public void Check_ImageOfOtherBuild_FailsWithBothValues()
    {
        string pdbPath = Path.Combine(_folder.Root, "file.pdb");
        new FakePdb(_guid, [0x1000]) { DbiAge = 7 }.Write(pdbPath);
        WriteImage(FilePath, true, CodeViewEntry(_otherGuid, 9, "file.pdb"));

        using var pdb = PdbFile.Open(pdbPath).Unwrap();

        var result = pdb.Identity.Check(ExecutableIdentity.Read(FilePath).Unwrap());

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(SR.FormatPdbIdentityMismatch(_guid, 7, _otherGuid, 9)));
    }

    // The age of the info stream isn't the age the image records, so a matching info age doesn't pass.
    [Test]
    public void Check_ImageMatchesOnlyTheInfoAge_Fails()
    {
        string pdbPath = Path.Combine(_folder.Root, "file.pdb");
        new FakePdb(_guid, [0x1000]) { InfoAge = 9, DbiAge = 7 }.Write(pdbPath);
        WriteImage(FilePath, true, CodeViewEntry(_guid, 9, "file.pdb"));

        using var pdb = PdbFile.Open(pdbPath).Unwrap();

        Assert.That(
            pdb.Identity.Check(ExecutableIdentity.Read(FilePath).Unwrap()).UnwrapErr().Message,
            Is.EqualTo(SR.FormatPdbIdentityMismatch(_guid, 7, _guid, 9)));
    }

    // A path with spaces, braces and non-ASCII characters reads like any other.
    [Test]
    public void Read_OddPath_ReturnsTheIdentity()
    {
        string folder = Path.Combine(_folder.Root, "バー {0} 1");
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "file ü.bin");
        WriteImage(path, true, CodeViewEntry(_guid, 1, "file.pdb"));

        Assert.That(ExecutableIdentity.Read(path).Unwrap().Guid, Is.EqualTo(_guid));
    }

    // The linker writes the PDB path in UTF-8.
    [Test]
    public void Read_NonAsciiPdbName_ReturnsIt()
    {
        WriteImage(FilePath, true, CodeViewEntry(_guid, 1, "C:\\バー\\file ü.pdb"));

        Assert.That(ExecutableIdentity.Read(FilePath).Unwrap().PdbName, Is.EqualTo("C:\\バー\\file ü.pdb"));
    }

    // The CodeView entry doesn't have to come 1st in the directory.
    [Test]
    public void Read_CodeViewAfterOtherEntry_FindsIt()
    {
        WriteImage(FilePath, true, CodeViewEntry(_guid, 3, "file.pdb", otherEntries: 1), EntrySize * 2);

        Assert.That(ExecutableIdentity.Read(FilePath).Unwrap().Age, Is.EqualTo(3));
    }

    // A debug directory with only other entries fails as an image without the entry.
    [Test]
    public void Read_OnlyOtherEntries_FailsAsNoCodeView()
    {
        byte[] other = new byte[EntrySize];
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(TypeAt), (int)DebugDirectoryEntryType.Reproducible);

        WriteImage(FilePath, true, other);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNoCodeView(FilePath)));
    }

    // An entry with the old NB10 format is still a PE file, so the failure doesn't claim it isn't.
    [Test]
    public void Read_OldCodeViewFormat_FailsAsBadEntry()
    {
        byte[] section = CodeViewEntry(_guid, 1, "file.pdb");
        Encoding.ASCII.GetBytes("NB10").CopyTo(section, EntrySize);

        WriteImage(FilePath, true, section);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' has a bad CodeView debug entry: "));
    }

    // An entry too short for its GUID and age fails as a bad entry and doesn't read past its data.
    [Test]
    public void Read_EntryTooShort_FailsAsBadEntry()
    {
        byte[] section = CodeViewEntry(_guid, 1, "file.pdb");
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(DataSizeAt), 8);

        WriteImage(FilePath, true, section);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' has a bad CodeView debug entry: "));
    }

    // An entry with data past the end of the file fails with the path, whatever the reader throws for it.
    [Test]
    public void Read_EntryDataPastTheFile_FailsWithPath()
    {
        byte[] section = CodeViewEntry(_guid, 1, "file.pdb");
        BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(DataPointerAt), 0x10_0000);

        WriteImage(FilePath, true, section);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A directory size that isn't a whole number of entries fails with the path.
    [Test]
    public void Read_DirectorySizeNotWholeEntries_FailsAsBadEntry()
    {
        WriteImage(FilePath, true, CodeViewEntry(_guid, 1, "file.pdb"), EntrySize + 1);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' has a bad CodeView debug entry: "));
    }

    // A directory larger than the file fails with the path.
    [Test]
    public void Read_DirectoryPastTheFile_FailsWithPath()
    {
        WriteImage(FilePath, true, CodeViewEntry(_guid, 1, "file.pdb"), EntrySize * 100);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A file cut in the DOS header, the COFF header, the optional header, the section table or the debug data fails with the path.
    [TestCase(0x2)]
    [TestCase(0x3C)]
    [TestCase(0x40)]
    [TestCase(0x46)]
    [TestCase(0x60)]
    [TestCase(0x100)]
    [TestCase(0x150)]
    [TestCase(SectionRawAt)]
    [TestCase(SectionRawAt + 8)]
    [TestCase(SectionRawAt + EntrySize)]
    [TestCase(SectionRawAt + EntrySize + 10)]
    public void Read_CutImage_FailsWithPath(int length)
    {
        WriteImage(FilePath, true, CodeViewEntry(_guid, 1, "file.pdb"));
        File.WriteAllBytes(FilePath, File.ReadAllBytes(FilePath)[..length]);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A folder fails with the path and the reason of the open.
    [Test]
    public void Read_Folder_FailsWithPath()
    {
        Assert.That(ExecutableIdentity.Read(_folder.Root).UnwrapErr().Message, Does.StartWith($"Can't read '{_folder.Root}': "));
    }

    // A file another program has open fails with the path and the reason of the open.
    [Test]
    public void Read_FileOpenInAnotherProgram_FailsWithPath()
    {
        WriteImage(FilePath, true, CodeViewEntry(_guid, 1, "file.pdb"));
        using FileStream locked = new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(ExecutableIdentity.Read(FilePath).UnwrapErr().Message, Does.StartWith($"Can't read '{FilePath}': "));
    }

    // A read closes the file, so the next step can replace it.
    [Test]
    public void Read_AfterAFailure_ReleasesTheFile()
    {
        File.WriteAllBytes(FilePath, new byte[16]);

        _ = ExecutableIdentity.Read(FilePath);

        Assert.That(() => File.Delete(FilePath), Throws.Nothing);
    }

    private static void WriteImage(string path, bool is64Bit, byte[] section, int directorySize = EntrySize)
    {
        const int DebugDirectoryAt32 = 0xE8;
        const int DebugDirectoryAt64 = 0xF8;

        CodeBuilder.WritePe(path, new CodeBuilder(is64Bit).Raw(section).Build());

        byte[] file = File.ReadAllBytes(path);
        int at = is64Bit ? DebugDirectoryAt64 : DebugDirectoryAt32;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), SectionRva);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + sizeof(uint)), directorySize);
        File.WriteAllBytes(path, file);
    }

    private static byte[] CodeViewEntry(Guid guid, int age, string pdbName, int otherEntries = 0)
    {
        byte[] data = CodeViewData(guid, age, pdbName);
        int dataAt = (otherEntries + 1) * EntrySize;
        byte[] section = new byte[dataAt + data.Length];

        for (int index = 0; index < otherEntries; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan((index * EntrySize) + TypeAt), (int)DebugDirectoryEntryType.Reproducible);
        }

        var entry = section.AsSpan(otherEntries * EntrySize, EntrySize);
        BinaryPrimitives.WriteInt32LittleEndian(entry[TypeAt..], (int)DebugDirectoryEntryType.CodeView);
        BinaryPrimitives.WriteInt32LittleEndian(entry[DataSizeAt..], data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[DataRvaAt..], SectionRva + (uint)dataAt);
        BinaryPrimitives.WriteInt32LittleEndian(entry[DataPointerAt..], SectionRawAt + dataAt);
        data.CopyTo(section, dataAt);

        return section;
    }

    private static byte[] CodeViewData(Guid guid, int age, string pdbName)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(Encoding.ASCII.GetBytes("RSDS"));
        writer.Write(guid.ToByteArray());
        writer.Write(age);
        writer.Write(Encoding.UTF8.GetBytes(pdbName));
        writer.Write((byte)0);
        writer.Flush();

        return stream.ToArray();
    }
}
