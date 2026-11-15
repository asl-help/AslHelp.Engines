using AslHelp.Engines.Measuring.Cli.Pdb;
using AsmResolver.Symbols.Pdb.Leaves;
using AsmResolver.Symbols.Pdb.Msf;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class PdbFileEdgeTests
{
    private const uint FirstType = 0x1000;
    private const uint IntType = 0x74;
    private const uint MissingType = 0xFFFF;

    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly uint[] _sections = [0x1000, 0x3000];

    private TestFolder _folder = null!;

    private string FilePath => Path.Combine(_folder.Root, "file.pdb");

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
    public void Open_OddPath_ReadsTheFile()
    {
        string folder = Path.Combine(_folder.Root, "バー {0} 1");
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "file ü.pdb");
        Pdb().Write(path);

        using var pdb = PdbFile.Open(path).Unwrap();

        Assert.That(pdb.Identity.Guid, Is.EqualTo(_guid));
    }

    // A failure prints the path as it is, so braces in it don't end up as format items.
    [Test]
    public void Open_MissingOddPath_FailsWithThePathAsIs()
    {
        string path = Path.Combine(_folder.Root, "バー {0} {1}.pdb");

        Assert.That(PdbFile.Open(path).UnwrapErr().Message, Does.StartWith($"Can't read '{path}': "));
    }

    // A path that the file APIs refuse fails as unreadable, with the path.
    [TestCase("")]
    [TestCase("file\0.pdb")]
    public void Open_InvalidPath_FailsAsUnreadable(string path)
    {
        Assert.That(PdbFile.Open(path).UnwrapErr().Message, Does.StartWith(SR.FormatFileUnreadable(path, "")));
    }

    // A folder fails with its path and the reason of the open.
    [Test]
    public void Open_Folder_FailsWithPath()
    {
        Assert.That(PdbFile.Open(_folder.Root).UnwrapErr().Message, Does.StartWith($"Can't read '{_folder.Root}': "));
    }

    // Reading doesn't need write access, so a read-only file from an archive opens.
    [Test]
    public void Open_ReadOnlyFile_ReadsTheFile()
    {
        Pdb().Write(FilePath);
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);

        try
        {
            using var pdb = PdbFile.Open(FilePath).Unwrap();

            Assert.That(pdb.Identity.Guid, Is.EqualTo(_guid));
        }
        finally
        {
            File.SetAttributes(FilePath, FileAttributes.Normal);
        }
    }

    // 2 readers can share a file, so 2 measurers can read 1 PDB at once.
    [Test]
    public void Open_SameFileTwice_BothRead()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.Write(FilePath);

        using var first = PdbFile.Open(FilePath).Unwrap();
        using var second = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.FindLayouts("Item").Unwrap().Single().Size, Is.EqualTo(8));
            Assert.That(second.FindLayouts("Item").Unwrap().Single().Size, Is.EqualTo(8));
        }
    }

    // A file without any types or symbols opens and each lookup fails as absent.
    [Test]
    public void Open_NoRecordsAtAll_FailsLookupsAsAbsent()
    {
        Pdb().Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("Item").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeNotFound("Item")));
            Assert.That(pdb.GlobalRva("Value").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound("Value")));
            Assert.That(pdb.FunctionRva("Value").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound("Value")));
        }
    }

    // A PDB cut at any block fails with its path, so a partial download doesn't read as a smaller PDB.
    [Test]
    public void Open_EveryCutOfAFile_FailsWithPath()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.AddPublic("?Value@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        byte[] file = File.ReadAllBytes(FilePath);

        for (int length = 0; length < file.Length; length += length % 0x1000 == 0 ? 0xFFF : 1)
        {
            File.WriteAllBytes(FilePath, file[..length]);

            Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.Contain(FilePath), $"length: {length}");
        }
    }

    // A failed open closes the file, so the next step can replace it.
    [Test]
    public void Open_FailureAfterTheMapping_ReleasesTheFile()
    {
        new FakePdb(_guid, null).Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).IsErr, Is.True);
        Assert.That(() => File.Delete(FilePath), Throws.Nothing);
    }

    // A block index past the block count fails with the path, instead of an index exception out of the open.
    [Test]
    public void Open_DirectoryBlockPastTheBlockCount_FailsWithPath()
    {
        const int StreamCount = 7;
        const int InfoStreamBlockAt = sizeof(uint) + (StreamCount * sizeof(uint));

        Pdb().Write(FilePath);
        Patch(FakePdb.DirectoryAt(File.ReadAllBytes(FilePath)) + InfoStreamBlockAt, int.MaxValue);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A stream size near 4 GiB wraps the block count in the reader, so the open fails with the path instead of an argument exception out of it.
    [Test]
    public void Open_StreamSizeNearFourGiB_FailsWithPath()
    {
        const int InfoStreamSizeAt = 2 * sizeof(uint);

        Pdb().Write(FilePath);
        Patch(FakePdb.DirectoryAt(File.ReadAllBytes(FilePath)) + InfoStreamSizeAt, -16);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // A negative stream count fails with the path, instead of an overflow out of the open.
    [Test]
    public void Open_NegativeStreamCount_FailsWithPath()
    {
        Pdb().Write(FilePath);
        Patch(FakePdb.DirectoryAt(File.ReadAllBytes(FilePath)), -1);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A stream count past the end of the directory fails with the path.
    [Test]
    public void Open_StreamCountPastTheDirectory_FailsWithPath()
    {
        Pdb().Write(FilePath);
        Patch(FakePdb.DirectoryAt(File.ReadAllBytes(FilePath)), 0x10000);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A block count that doesn't fit an array fails with the path, instead of an out of memory exception out of the open.
    [Test]
    public void Open_HugeBlockCount_FailsWithPath()
    {
        const int BlockCountAt = 40;

        Pdb().Write(FilePath);
        Patch(BlockCountAt, -1);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A type count that doesn't fit an array fails with the path, instead of an out of memory exception out of the open.
    [Test]
    public void Open_TypeCountPastTheArrayLimit_FailsWithPath()
    {
        const int TypeStream = 2;
        const int TypeIndexEndAt = 12;

        Pdb().Write(FilePath);
        Patch(FakePdb.StreamAt(File.ReadAllBytes(FilePath), TypeStream) + TypeIndexEndAt, unchecked((int)(FirstType + int.MaxValue)));

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // The reader only supports blocks up to 4096 bytes, so a PDB linked with a larger page size fails with that reason.
    [Test]
    public void Open_BlockSize8192_FailsWithTheReason()
    {
        const int BlockSizeAt = 32;

        Pdb().Write(FilePath);
        Patch(BlockSizeAt, 8192);

        string message = PdbFile.Open(FilePath).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain(FilePath));
            Assert.That(message, Does.Contain("Block size"));
        }
    }

    // A file with too few streams fails with the path, for an empty stream list too.
    [TestCase(0)]
    [TestCase(2)]
    public void Open_TooFewStreams_FailsWithPath(int streams)
    {
        MsfFile msf = new();

        for (int index = 0; index < streams; index++)
        {
            msf.Streams.Add(new MsfStream([]));
        }

        msf.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // An empty section header stream isn't a PDB without sections, because every symbol would read as absent.
    [Test]
    public void Open_EmptySectionHeaderStream_FailsWithPath()
    {
        new FakePdb(_guid, []).Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSectionHeadersMissing(FilePath)));
    }

    // A cut section header stream would lose the sections behind the cut and each symbol in them.
    [Test]
    public void Open_CutSectionHeaderStream_FailsWithPath()
    {
        new FakePdb(_guid, _sections) { SectionStreamCut = 7 }.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSectionHeadersMissing(FilePath)));
    }

    // Streams keep their directory index around a nil stream, so the DBI stream finds the real section headers and symbols.
    [Test]
    public void Open_NilStreamBeforeTheSections_KeepsStreamIndexes()
    {
        var written = new FakePdb(_guid, _sections) { NilStreamBeforeSections = true };
        written.AddPublic("?Value@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.Identity.Guid, Is.EqualTo(_guid));
            Assert.That(pdb.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
        }
    }

    // The block sizes older linkers use read like the 4096 of the other tests, with records that cross blocks.
    [TestCase(512u)]
    [TestCase(1024u)]
    [TestCase(2048u)]
    public void Open_SmallerBlockSize_ReadsTheSameValues(uint blockSize)
    {
        var written = new FakePdb(_guid, _sections) { BlockSize = blockSize };
        byte[][] members = [.. Enumerable.Range(0, 300).Select(static offset => FakePdb.Member($"M{offset}", (ulong)offset))];
        written.AddStruct("Item", 0x8000, written.AddFieldList(members));
        written.AddPublic("?Value@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("Item").Unwrap().Single().Member("M299")!.Offset, Is.EqualTo(299));
            Assert.That(pdb.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
        }
    }

    // A class past 4 GiB doesn't fit the reader, so the open fails with the path instead of an overflow out of it.
    [Test]
    public void Open_ClassPastFourGiB_FailsWithPath()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x1_0000_0000, written.AddFieldList());
        written.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // With OMAP streams each symbol offset points at the image before the rewrite, so the open fails instead of returning wrong RVAs.
    [TestCase(3)]
    [TestCase(4)]
    public void Open_OmapStream_FailsWithPath(int slot)
    {
        new FakePdb(_guid, _sections) { OmapSlot = slot }.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbOmapUnsupported(FilePath)));
    }

    // 1 record with a 32-bit property word doesn't block the other types and the symbols of the PDB.
    [Test]
    public void Open_Class2RecordNextToOtherRecords_ReadsAll()
    {
        var written = Pdb();
        _ = written.AddClass2(CodeViewLeafKind.Structure2, "Wide", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x0)));
        written.AddPublic("?Value@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("Item").Unwrap().Single().Member("Value")!.Offset, Is.Zero);
            Assert.That(pdb.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
        }
    }

    // A cut record with a 32-bit property word fails with the path, so a type doesn't read as absent.
    [Test]
    public void Open_CutClass2Record_FailsWithPath()
    {
        var written = Pdb();
        written.AddOpaqueType(CodeViewLeafKind.Structure2, 0, 0, 0, 0);
        written.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // A size behind a kind that isn't an unsigned integer can't be read, so the open fails with the kind instead of a wrong size.
    [Test]
    public void Open_Class2SizeOfOtherKind_FailsWithTheKind()
    {
        const int SizeAt = 18;
        const ushort FloatKind = 0x8005;

        byte[] body = new byte[SizeAt + sizeof(ushort)];
        BitConverter.TryWriteBytes(body.AsSpan(SizeAt), FloatKind);

        var written = Pdb();
        written.AddOpaqueType(CodeViewLeafKind.Structure2, body);
        written.Write(FilePath);

        string message = PdbFile.Open(FilePath).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbInvalid(FilePath, SR.FormatPdbNumericKindUnsupported($"{FloatKind:X4}"))));
    }

    // A record with a 32-bit property word and a field list past the type stream would read as a type without members, so the open fails.
    [Test]
    public void Open_Class2FieldListPastTheStream_FailsWithPath()
    {
        var written = Pdb();
        _ = written.AddClass2(CodeViewLeafKind.Structure2, "Item", 0x8, MissingType);
        written.Write(FilePath);

        string message = PdbFile.Open(FilePath).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbInvalid(FilePath, SR.FormatPdbFieldListMissing("Item"))));
    }

    // A kind without a reader that isn't a class record doesn't hide a layout, so the file still opens.
    [Test]
    public void Open_OtherRecordKindWithoutReader_ReadsTheFile()
    {
        var written = Pdb();
        written.AddOpaqueType(CodeViewLeafKind.Alias);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.Identity.Guid, Is.EqualTo(_guid));
    }

    // An age past the range the image stores can't match any image, so the open fails with the path.
    [Test]
    public void Open_AgePastTheImageRange_FailsWithPath()
    {
        new FakePdb(_guid, _sections) { DbiAge = 0x8000_0000 }.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // An offset past the address space can't be an RVA, so the open fails with the path.
    [Test]
    public void Open_SymbolPastTheAddressSpace_FailsWithPath()
    {
        var written = Pdb();
        written.AddPublic("?Value@@3HA", 2, uint.MaxValue, isFunction: false);
        written.Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // A 2nd Dispose does nothing.
    [Test]
    public void Dispose_Twice_DoesNotThrow()
    {
        Pdb().Write(FilePath);

        var pdb = PdbFile.Open(FilePath).Unwrap();
        pdb.Dispose();

        Assert.That(pdb.Dispose, Throws.Nothing);
    }

    // Field lists read from the mapping, so a lookup after Dispose throws instead of crashing on an unmapped address.
    [Test]
    public void FindLayouts_AfterDispose_Throws()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.Write(FilePath);

        var pdb = PdbFile.Open(FilePath).Unwrap();
        pdb.Dispose();

        Assert.That(() => pdb.FindLayouts("Item"), Throws.TypeOf<ObjectDisposedException>());
    }

    // Symbols live in memory, so a symbol lookup still reads after Dispose.
    [Test]
    public void GlobalRva_AfterDispose_StillReads()
    {
        var written = Pdb();
        written.AddPublic("?Value@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        var pdb = PdbFile.Open(FilePath).Unwrap();
        pdb.Dispose();

        Assert.That(pdb.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
    }

    // MSVC writes a value past 0x7FFF behind a 2-byte kind, for the size of a record and the offset of a member and a base class.
    [Test]
    public void FindLayouts_ValuesPastTwoBytes_ReadsThem()
    {
        var written = Pdb();
        uint forward = written.AddForward("Parent");
        written.AddStruct("Parent", 0x20000, written.AddFieldList(FakePdb.Member("Inner", 0x10000)));
        uint fields = written.AddFieldList(FakePdb.BaseClass(forward, 0x9000), FakePdb.Member("Small", 0x7FFF), FakePdb.Member("Wide", 0x8000));
        written.AddStruct("Item", 0x12345, fields);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var layout = pdb.FindLayouts("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Size, Is.EqualTo(0x12345));
            Assert.That(layout.Member("Small")!.Offset, Is.EqualTo(0x7FFF));
            Assert.That(layout.Member("Wide")!.Offset, Is.EqualTo(0x8000));
            Assert.That(layout.Member("Inner")!.Offset, Is.EqualTo(0x19000));
        }
    }

    // A record with its forward reference in the file folds through the name, with the size of the member from the complete record.
    [Test]
    public void FindLayouts_ForwardReferencesInTheFile_ResolveByName()
    {
        var written = Pdb();
        uint forward = written.AddForward("Parent");
        written.AddStruct("Parent", 0x10, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.AddStruct("Item", 0x40, written.AddFieldList(FakePdb.BaseClass(forward, 0x20), FakePdb.Member("Own", 0x0, forward)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var layout = pdb.FindLayouts("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Value")!.Offset, Is.EqualTo(0x24));
            Assert.That(layout.Member("Own")!.Size, Is.EqualTo(0x10));
            Assert.That(pdb.FindLayouts("Parent").Unwrap().Single().Size, Is.EqualTo(0x10));
        }
    }

    // A name in UTF-8 reads like any other, for a type and a symbol.
    [Test]
    public void FindLayouts_NonAsciiNames_ReadsThem()
    {
        var written = Pdb();
        written.AddStruct("Größe<バー>", 0x8, written.AddFieldList(FakePdb.Member("Länge", 0x4)));
        written.AddPublic("?Größe@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("Größe<バー>").Unwrap().Single().Member("Länge")!.Offset, Is.EqualTo(4));
            Assert.That(pdb.GlobalRva("Größe").Unwrap(), Is.EqualTo(0x3010));
        }
    }

    // A field list of a real size crosses blocks and the record limit and each member stays in order.
    [Test]
    public void FindLayouts_LongContinuedList_ReturnsEveryMemberInOrder()
    {
        const int MembersPerList = 2000;
        const int Lists = 5;

        var written = Pdb();
        uint next = 0;

        for (int list = Lists - 1; list >= 0; list--)
        {
            int start = list * MembersPerList;
            byte[][] members = [.. Enumerable.Range(start, MembersPerList).Select(static offset => FakePdb.Member($"Member{offset}", (ulong)offset))];
            next = next == 0 ? written.AddFieldList(members) : written.AddFieldList([.. members, FakePdb.Continuation(next)]);
        }

        written.AddStruct("Item", 0x10000, next);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var offsets = pdb.FindLayouts("Item").Unwrap().Single().Members.Select(static member => member.Offset);

        Assert.That(offsets, Is.EqualTo(Enumerable.Range(0, Lists * MembersPerList).Select(static offset => (uint)offset)));
    }

    // A list that only points at the next list still counts, because the length check stops right below this size.
    [Test]
    public void FindLayouts_ListWithOnlyAContinuation_ReturnsTheNextMembers()
    {
        var written = Pdb();
        uint last = written.AddFieldList(FakePdb.Member("Value", 0x4));
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.Continuation(last)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FindLayouts("Item").Unwrap().Single().Member("Value")!.Offset, Is.EqualTo(4));
    }

    // A member with type index 0x1404 and a 1-char name ends in 8 bytes that read like a continuation and it stays a member.
    [Test]
    public void FindLayouts_LastMemberLooksLikeAContinuation_KeepsTheMember()
    {
        const uint ContinuationKind = 0x1404;

        var written = Pdb();

        for (uint index = FirstType; index < ContinuationKind; index++)
        {
            _ = written.AddFieldList();
        }

        uint type = written.AddForward("Inner");
        written.AddStruct("Item", 0x20, written.AddFieldList(FakePdb.Member("x", 0x10, type)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(type, Is.EqualTo(ContinuationKind));
            Assert.That(pdb.FindLayouts("Item").Unwrap().Single().Member("x")!.Offset, Is.EqualTo(0x10));
        }
    }

    // An unknown entry in a list that ends in a continuation would hide the members behind it, so the lookup fails with the type.
    [Test]
    public void FindLayouts_UnknownEntryInContinuedList_FailsWithTypeName()
    {
        var written = Pdb();
        uint last = written.AddFieldList(FakePdb.Member("Third", 0x8));
        byte[][] entries = [FakePdb.Member("First", 0x0), FakePdb.UnknownEntry(), FakePdb.Member("Second", 0x4), FakePdb.Continuation(last)];
        written.AddStruct("Item", 0xC, written.AddFieldList(entries));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FindLayouts("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // A record with a 32-bit property word reads like its older form, with a property bit past the 16 bits of that form.
    [TestCase(CodeViewLeafKind.Class2)]
    [TestCase(CodeViewLeafKind.Interface2)]
    [TestCase(CodeViewLeafKind.Structure2)]
    [TestCase(CodeViewLeafKind.Union2)]
    public void FindLayouts_Class2Record_ReturnsTheMembers(CodeViewLeafKind kind)
    {
        var written = Pdb();
        _ = written.AddClass2(kind, "Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var layout = pdb.FindLayouts("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Size, Is.EqualTo(8));
            Assert.That(layout.Member("Value")!.Offset, Is.EqualTo(4));
        }
    }

    // A value past 0x7FFF sits behind a 2-byte kind in a record with a 32-bit property word too, for the size and the member count.
    [TestCase(0x8000ul, 0x8000ul)]
    [TestCase(0x1_0000ul, 0x1_0000ul)]
    [TestCase(0x8ul, 0x1_0000_0000ul)]
    public void FindLayouts_Class2RecordValuesPastTwoBytes_ReadsThem(ulong size, ulong memberCount)
    {
        var written = Pdb();
        _ = written.AddClass2(CodeViewLeafKind.Structure2, "Item", size, written.AddFieldList(), memberCount: memberCount);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FindLayouts("Item").Unwrap().Single().Size, Is.EqualTo(size));
    }

    // A forward reference with a 32-bit property word fails as forward-only and not as absent.
    [Test]
    public void FindLayouts_ForwardOnlyClass2Record_FailsAsForwardOnly()
    {
        var written = Pdb();
        _ = written.AddClass2(CodeViewLeafKind.Structure2, "Item", 0, 0, StructureAttributes.FwdRef);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FindLayouts("Item").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbTypeForwardOnly("Item")));
    }

    // 2 compile units can complete 1 type in both forms, so a different layout in the newer form stays visible.
    [Test]
    public void FindLayouts_Class2AndOlderRecordDiffer_ReturnsBothLayouts()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x0)));
        _ = written.AddClass2(CodeViewLeafKind.Structure2, "Item", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var offsets = pdb.FindLayouts("Item").Unwrap().Select(static layout => layout.Member("Value")!.Offset);

        Assert.That(offsets, Is.EqualTo(new uint[] { 0x0, 0x4 }));
    }

    // An older forward reference resolves by name to a record with a 32-bit property word, as a base class and for the size of a member.
    [Test]
    public void FindLayouts_ForwardToClass2Record_ResolvesByName()
    {
        var written = Pdb();
        uint forward = written.AddForward("Parent");
        _ = written.AddClass2(CodeViewLeafKind.Structure2, "Parent", 0x10, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.AddStruct("Item", 0x40, written.AddFieldList(FakePdb.BaseClass(forward, 0x20), FakePdb.Member("Own", 0x0, forward)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var layout = pdb.FindLayouts("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Member("Value")!.Offset, Is.EqualTo(0x24));
            Assert.That(layout.Member("Own")!.Size, Is.EqualTo(0x10));
        }
    }

    // AsmResolver can't resolve a direct type index to a record with a 32-bit property word, so only the types that use one fail.
    [Test]
    public void FindLayouts_DirectIndexToClass2Record_FailsOnlyThatType()
    {
        var written = Pdb();
        uint wide = written.AddClass2(CodeViewLeafKind.Structure2, "Wide", 0x10, written.AddFieldList(FakePdb.Member("Value", 0x4)));
        written.AddStruct("Derived", 0x20, written.AddFieldList(FakePdb.BaseClass(wide, 0x0)));
        written.AddStruct("Holder", 0x20, written.AddFieldList(FakePdb.Member("Own", 0x0, wide)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("Derived").UnwrapErr().Message, Does.StartWith("Can't read type 'Derived': "));
            Assert.That(pdb.FindLayouts("Holder").UnwrapErr().Message, Does.StartWith("Can't read type 'Holder': "));
            Assert.That(pdb.FindLayouts("Wide").IsOk, Is.True);
        }
    }

    // A record without a field list keeps its size and gets an empty member list.
    [Test]
    public void FindLayouts_NoFieldList_ReturnsAnEmptyLayout()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x8, 0);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var layout = pdb.FindLayouts("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Size, Is.EqualTo(8));
            Assert.That(layout.Members, Is.Empty);
        }
    }

    // A list that continues into itself can't end, so the lookup fails instead of running until memory is gone.
    [Test]
    public void FindLayouts_ListContinuesIntoItself_FailsWithTypeName()
    {
        var written = Pdb();
        uint fields = written.AddFieldList(FakePdb.Member("Value", 0x0), FakePdb.Continuation(FirstType));
        written.AddStruct("Item", 0x8, fields);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields, Is.EqualTo(FirstType));
            Assert.That(
                pdb.FindLayouts("Item").UnwrapErr().Message,
                Is.EqualTo(SR.FormatPdbTypeUnreadable("Item", SR.FormatPdbFieldListCycle("Item"))));
        }
    }

    // A base class typed by a simple type can only come from a bad file, so the lookup fails with the type.
    [Test]
    public void FindLayouts_BaseClassIsASimpleType_FailsWithTypeName()
    {
        var written = Pdb();
        written.AddStruct("Item", 0x8, written.AddFieldList(FakePdb.BaseClass(IntType, 0x0)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FindLayouts("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    // Each kind of type index past the type stream fails the lookup with the type and leaves other types readable.
    [Test]
    public void FindLayouts_TypeIndexPastTheStream_FailsOnlyThatType()
    {
        var written = Pdb();
        written.AddStruct("BadBase", 0x8, written.AddFieldList(FakePdb.BaseClass(MissingType, 0x0)));
        written.AddStruct("BadMember", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x0, MissingType)));
        written.AddStruct("BadList", 0x8, MissingType);
        written.AddStruct("Good", 0x8, written.AddFieldList(FakePdb.Member("Value", 0x0)));
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.FindLayouts("BadBase").UnwrapErr().Message, Does.StartWith("Can't read type 'BadBase': "));
            Assert.That(pdb.FindLayouts("BadMember").UnwrapErr().Message, Does.StartWith("Can't read type 'BadMember': "));
            Assert.That(pdb.FindLayouts("BadList").UnwrapErr().Message, Does.StartWith("Can't read type 'BadList': "));
            Assert.That(pdb.FindLayouts("Good").IsOk, Is.True);
        }
    }

    // A data record in the symbol stream stays out, because the lookups only read publics.
    [Test]
    public void GlobalRva_DataRecordInTheFile_DoesNotMatch()
    {
        var written = Pdb();
        written.AddData("Cache", 2, 0x20);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.GlobalRva("Cache").IsErr, Is.True);
    }

    private static FakePdb Pdb()
    {
        return new FakePdb(_guid, _sections);
    }

    private void Patch(int at, int value)
    {
        byte[] file = File.ReadAllBytes(FilePath);
        BitConverter.TryWriteBytes(file.AsSpan(at), value);
        File.WriteAllBytes(FilePath, file);
    }
}
