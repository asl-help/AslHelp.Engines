using AslHelp.Engines.Measuring.Cli.Pdb;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class PdbFileTests
{
    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly uint[] _sections = [0x1000, 0x3000];
    private static readonly string[] _threeMembers = ["First", "Second", "Third"];

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

    // A missing file fails with its path and the reason of the open.
    [Test]
    public void Open_MissingFile_FailsWithPath()
    {
        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"Can't read '{FilePath}': "));
    }

    // A file that isn't a PDB fails with its path, including an empty file that can't be mapped.
    [TestCase(0)]
    [TestCase(16)]
    [TestCase(0x1000)]
    public void Open_FileThatIsNoPdb_FailsWithPath(int length)
    {
        File.WriteAllBytes(FilePath, new byte[length]);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"'{FilePath}' is not a valid PDB: "));
    }

    // A file another program has open fails with its path and the reason of the open.
    [Test]
    public void Open_FileOpenInAnotherProgram_FailsWithPath()
    {
        File.WriteAllBytes(FilePath, new byte[0x1000]);
        using FileStream locked = new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.StartWith($"Can't read '{FilePath}': "));
    }

    // A file cut off in the middle fails with its path and leaves the file closed.
    [Test]
    public void Open_FileCutShort_FailsWithPath()
    {
        Pdb().Write(FilePath);
        File.WriteAllBytes(FilePath, File.ReadAllBytes(FilePath)[..0x2000]);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A PDB without section headers can't turn a section and offset into an RVA, so it fails and leaves the file closed.
    [Test]
    public void Open_NoSectionHeaderStream_FailsWithPath()
    {
        new FakePdb(_guid, null).Write(FilePath);

        Assert.That(PdbFile.Open(FilePath).UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSectionHeadersMissing(FilePath)));
    }

    // Dispose closes the mapping, so the file can go away.
    [Test]
    public void Dispose_OpenFile_ReleasesTheFile()
    {
        Pdb().Write(FilePath);

        PdbFile.Open(FilePath).Unwrap().Dispose();

        Assert.That(() => File.Delete(FilePath), Throws.Nothing);
    }

    // The age the executable records is the age of the DBI stream, which can differ from the age of the info stream.
    [Test]
    public void Identity_InfoAndDbiAgesDiffer_UsesTheDbiAge()
    {
        new FakePdb(_guid, _sections) { InfoAge = 2, DbiAge = 5 }.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.Identity, Is.EqualTo(new PdbIdentity(_guid, 5)));
    }

    // The section header stream lists the RVAs that sections and offsets add up with.
    [Test]
    public void GlobalRva_PublicInLaterSection_AddsTheSectionRva()
    {
        var written = Pdb();
        written.AddPublic("?Value@@3HA", 2, 0x10, isFunction: false);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.Identity.Guid, Is.EqualTo(_guid));
            Assert.That(pdb.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
        }
    }

    // A function public reads through the file like a data public.
    [Test]
    public void FunctionRva_PublicFunction_AddsTheSectionRva()
    {
        var written = Pdb();
        written.AddPublic("?Get@Table@@SAHXZ", 1, 0x40, isFunction: true);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FunctionRva("Table::Get").Unwrap(), Is.EqualTo(0x1040));
    }

    // A complete record comes back with its members, read from the file.
    [Test]
    public void FindLayouts_CompleteRecord_ReturnsTheMembers()
    {
        var written = Pdb();
        uint fields = written.AddFieldList(FakePdb.Member("First", 0x0), FakePdb.Member("Second", 0x4));
        written.AddStruct("Item", 0x8, fields);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        var layout = pdb.FindLayouts("Item").Unwrap().Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Size, Is.EqualTo(8));
            Assert.That(layout.Member("Second")!.Offset, Is.EqualTo(4));
            Assert.That(layout.Member("Second")!.Size, Is.EqualTo(4));
        }
    }

    // A field list past the record limit ends in an index entry that points at the rest of the members.
    [Test]
    public void FindLayouts_ContinuedFieldList_ReturnsMembersOfEveryPart()
    {
        var written = Pdb();
        uint third = written.AddFieldList(FakePdb.Member("Third", 0x8));
        uint second = written.AddFieldList(FakePdb.Member("Second", 0x4), FakePdb.Continuation(third));
        uint first = written.AddFieldList(FakePdb.Member("First", 0x0), FakePdb.Continuation(second));
        written.AddStruct("Item", 0xC, first);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(
            pdb.FindLayouts("Item").Unwrap().Single().Members.Select(static member => member.Name),
            Is.EqualTo(_threeMembers));
    }

    // A field list entry of an unknown kind would hide every member behind it, so the lookup fails with the type.
    [Test]
    public void FindLayouts_UnknownFieldEntry_FailsWithTypeName()
    {
        var written = Pdb();
        uint fields = written.AddFieldList(FakePdb.Member("First", 0x0), FakePdb.UnknownEntry(), FakePdb.Member("Second", 0x4));
        written.AddStruct("Item", 0x8, fields);
        written.Write(FilePath);

        using var pdb = PdbFile.Open(FilePath).Unwrap();

        Assert.That(pdb.FindLayouts("Item").UnwrapErr().Message, Does.StartWith("Can't read type 'Item': "));
    }

    private static FakePdb Pdb()
    {
        return new FakePdb(_guid, _sections);
    }
}
