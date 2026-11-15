using AslHelp.Engines.Measuring.Cli.Pdb;
using AsmResolver.Symbols.Pdb.Leaves;
using NUnit.Framework;
using System;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class ConstantIndexTests
{
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

    // MSVC keeps an enum value that a class declares in an unnamed enum nested in the class, so the class name finds it.
    [Test]
    public void ConstantValue_UnnamedEnumInTheScope_ReturnsTheValue()
    {
        var written = Pdb();
        written.AddEnum("Table::<unnamed-enum-PerChunk>", written.AddFieldList(FakePdb.Enumerate("PerChunk", 0x10000)));

        using var pdb = Open(written);

        Assert.That(pdb.ConstantValue("Table::PerChunk").Unwrap(), Is.EqualTo(0x10000));
    }

    // An enum with a name gives its values under that name and under its scope, as C++ reads them.
    [TestCase("Table::EKind::Second")]
    [TestCase("Table::Second")]
    public void ConstantValue_NamedEnumInTheScope_ReturnsTheValue(string name)
    {
        var written = Pdb();
        written.AddEnum("Table::EKind", written.AddFieldList(FakePdb.Enumerate("First", 0), FakePdb.Enumerate("Second", 1)));

        using var pdb = Open(written);

        Assert.That(pdb.ConstantValue(name).Unwrap(), Is.EqualTo(1));
    }

    // Templates of other classes declare values with the same name, so a value of another scope doesn't count.
    [Test]
    public void ConstantValue_SameNameInAnotherScope_Fails()
    {
        var written = Pdb();
        written.AddEnum("Other<Item,16384>::<unnamed-enum-PerChunk>", written.AddFieldList(FakePdb.Enumerate("PerChunk", 102)));

        using var pdb = Open(written);

        Assert.That(
            pdb.ConstantValue("Table::PerChunk").UnwrapErr().Message,
            Is.EqualTo(SR.FormatPdbConstantNotFound("Table::PerChunk")));
    }

    // A forward reference is without values, so it doesn't hide that the PDB lacks the enum.
    [Test]
    public void ConstantValue_ForwardEnumOnly_Fails()
    {
        var written = Pdb();
        written.AddEnum("Table::<unnamed-enum-PerChunk>", 0, StructureAttributes.FwdRef);

        using var pdb = Open(written);

        Assert.That(
            pdb.ConstantValue("Table::PerChunk").UnwrapErr().Message,
            Is.EqualTo(SR.FormatPdbConstantNotFound("Table::PerChunk")));
    }

    // A static constexpr member is a constant symbol, read with every byte of its value past the 2 bytes that AsmResolver reads.
    [TestCase(new byte[] { 0x10, 0x00 }, 0x10)]
    [TestCase(new byte[] { 0x02, 0x80, 0xFF, 0xFF }, 0xFFFF)]
    [TestCase(new byte[] { 0x04, 0x80, 0x00, 0x00, 0x01, 0x00 }, 0x10000)]
    [TestCase(new byte[] { 0x00, 0x80, 0xFF }, -1)]
    [TestCase(new byte[] { 0x03, 0x80, 0x00, 0x00, 0x00, 0x80 }, int.MinValue)]
    [TestCase(new byte[] { 0x04, 0x80, 0x00, 0x00, 0x00, 0x80 }, 0x80000000L)]
    [TestCase(new byte[] { 0x09, 0x80, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, -2L)]
    [TestCase(new byte[] { 0x0A, 0x80, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }, long.MaxValue)]
    public void ConstantValue_ConstantSymbol_ReadsTheWholeValue(byte[] value, long expected)
    {
        var written = Pdb();
        written.AddConstant("Visualizer::Mask", value);

        using var pdb = Open(written);

        Assert.That(pdb.ConstantValue("Visualizer::Mask").Unwrap(), Is.EqualTo(expected));
    }

    // A float or an unsigned value past the signed range doesn't fit a long. The records behind it still read.
    [TestCase(new byte[] { 0x05, 0x80, 0x00, 0x00, 0x80, 0x3F })]
    [TestCase(new byte[] { 0x0A, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 })]
    public void ConstantValue_ConstantSymbolPastLong_FailsAndReadsTheNextConstant(byte[] value)
    {
        var written = Pdb();
        written.AddConstant("Visualizer::Scale", value);
        written.AddConstant("Visualizer::Bits", 0x10, 0x00);
        using var pdb = Open(written);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdb.ConstantValue("Visualizer::Bits").Unwrap(), Is.EqualTo(0x10));
            Assert.That(pdb.ConstantValue("Visualizer::Scale").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbConstantDoesNotFit("Visualizer::Scale")));
        }
    }

    // An enum value past the signed range doesn't fit a long either, so the lookup fails the same way.
    [Test]
    public void ConstantValue_EnumValuePastLong_Fails()
    {
        var written = Pdb();
        written.AddEnum("Table::<unnamed-enum-Mask>", written.AddFieldList(FakePdb.Enumerate("Mask", 0x8000_0000_0000_0000)));

        using var pdb = Open(written);

        Assert.That(pdb.ConstantValue("Table::Mask").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbConstantDoesNotFit("Table::Mask")));
    }

    // A constant symbol and an enum value with the same name but other values leave the value open, so the lookup fails with both.
    [Test]
    public void ConstantValue_SymbolAndEnumDisagree_FailsWithBoth()
    {
        var written = Pdb();
        written.AddEnum("Table::<unnamed-enum-PerChunk>", written.AddFieldList(FakePdb.Enumerate("PerChunk", 0x10400)));
        written.AddConstant("Table::PerChunk", 0x04, 0x80, 0x00, 0x00, 0x01, 0x00);

        using var pdb = Open(written);

        Assert.That(pdb.ConstantValue("Table::PerChunk").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbConstantAmbiguous("Table::PerChunk", 0x10000, 0x10400)));
    }

    // Compile units repeat an enum, so equal copies give 1 value.
    [Test]
    public void ConstantValue_RepeatedEnum_ReturnsTheValue()
    {
        var written = Pdb();
        written.AddEnum("Table::<unnamed-enum-PerChunk>", written.AddFieldList(FakePdb.Enumerate("PerChunk", 0x10000)));
        written.AddEnum("Table::<unnamed-enum-PerChunk>", written.AddFieldList(FakePdb.Enumerate("PerChunk", 0x10000)));

        using var pdb = Open(written);

        Assert.That(pdb.ConstantValue("Table::PerChunk").Unwrap(), Is.EqualTo(0x10000));
    }

    private static FakePdb Pdb()
    {
        return new FakePdb(_guid, _sections);
    }

    private PdbFile Open(FakePdb written)
    {
        written.Write(FilePath);

        return PdbFile.Open(FilePath).Unwrap();
    }
}
