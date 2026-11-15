using AslHelp.Engines.Measuring.Cli.Pdb;
using AsmResolver.Symbols.Pdb;
using AsmResolver.Symbols.Pdb.Leaves;
using AsmResolver.Symbols.Pdb.Records;
using NUnit.Framework;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class SymbolIndexEdgeTests
{
    private static readonly SimpleTypeRecord _int = new(SimpleTypeKind.Int32);
    private static readonly uint[] _sections = [0x1000, 0x3000, 0x8000];

    // The decorated forms of 1 global in an x86 and an x64 image read the same in C++.
    [TestCase("?Value@@3PAVItem@@A")]
    [TestCase("?Value@@3PEAVItem@@EA")]
    public void GlobalRva_DecorationOfEitherArch_ReadsTheName(string decorated)
    {
        var index = Index(PublicData(decorated, 2, 0x10));

        Assert.That(index.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
    }

    // These decorations don't have a plain name in C++, so only the decorated name finds them.
    [TestCase("?Value", "Value")]
    [TestCase("?Value@0@@3HA", "0::Value")]
    [TestCase("?Value@Inner@1@@2HA", "1::Inner::Value")]
    [TestCase("?Value@?A0x12345678@@3HA", "Value")]
    [TestCase("??_7Table@@6B@", "Table")]
    [TestCase("?@@3HA", "")]
    public void GlobalRva_DecorationWithoutPlainName_FindsOnlyTheDecoratedName(string decorated, string plain)
    {
        var index = Index(PublicData(decorated, 2, 0x10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.GlobalRva(decorated).Unwrap(), Is.EqualTo(0x3010));
            Assert.That(index.GlobalRva(plain).IsErr, Is.True);
        }
    }

    // The last section counts, because sections count from 1.
    [Test]
    public void GlobalRva_LastSection_AddsItsRva()
    {
        var index = Index(PublicData("?Value@@3HA", 3, 0x10));

        Assert.That(index.GlobalRva("Value").Unwrap(), Is.EqualTo(0x8010));
    }

    // The offset is added as it is, also for the 1st byte of a section.
    [Test]
    public void GlobalRva_OffsetZero_ReturnsTheSectionRva()
    {
        var index = Index(PublicData("?Value@@3HA", 1, 0x0));

        Assert.That(index.GlobalRva("Value").Unwrap(), Is.EqualTo(0x1000));
    }

    // A function and a global with 1 name in C++ stay in their own tables, so neither is ambiguous.
    [Test]
    public void GlobalRva_DataAndFunctionWithOneName_KeepsThemApart()
    {
        var index = Index(PublicData("?Value@@3HA", 2, 0x10), PublicFunction("?Value@@YAHXZ", 1, 0x40));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
            Assert.That(index.FunctionRva("Value").Unwrap(), Is.EqualTo(0x1040));
        }
    }

    // The linker folds identical functions, so 2 overloads at 1 RVA aren't ambiguous.
    [Test]
    public void FunctionRva_TwoOverloadsAtOneRva_ReturnsIt()
    {
        var index = Index(PublicFunction("?Get@Table@@SAHXZ", 1, 0x40), PublicFunction("?Get@Table@@SAHH@Z", 1, 0x40));

        Assert.That(index.FunctionRva("Table::Get").Unwrap(), Is.EqualTo(0x1040));
    }

    // A 3rd RVA doesn't replace the pair the failure lists.
    [Test]
    public void FunctionRva_ThreeOverloads_FailsWithTheFirstTwo()
    {
        var index = Index(
            PublicFunction("?Get@Table@@SAHXZ", 1, 0x40),
            PublicFunction("?Get@Table@@SAHH@Z", 1, 0x80),
            PublicFunction("?Get@Table@@SAHM@Z", 1, 0xC0));

        Assert.That(index.FunctionRva("Table::Get").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolAmbiguous("Table::Get", "1040", "1080")));
    }

    // The ambiguity stays when the 1st RVA comes back later in the table.
    [Test]
    public void FunctionRva_FirstRvaAgainAfterAnother_StaysAmbiguous()
    {
        var index = Index(
            PublicFunction("?Get@Table@@SAHXZ", 1, 0x40),
            PublicFunction("?Get@Table@@SAHH@Z", 1, 0x80),
            PublicFunction("?Get@Table@@SAHM@Z", 1, 0x40));

        Assert.That(index.FunctionRva("Table::Get").IsErr, Is.True);
    }

    // A data record of the symbol stream stays out, because the tables only come from publics.
    [Test]
    public void GlobalRva_DataRecord_DoesNotMatch()
    {
        var index = Index(Static("Cache", 2, 0x10));

        Assert.That(index.GlobalRva("Cache").IsErr, Is.True);
    }

    // An empty symbol stream adds nothing, so each lookup fails with its name.
    [Test]
    public void Build_NoSymbols_FailsLookupsWithTheName()
    {
        var index = SymbolIndex.Build([], _sections);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.GlobalRva("Value").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound("Value")));
            Assert.That(index.FunctionRva("Value").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound("Value")));
        }
    }

    // A PDB without sections keeps every symbol out instead of throwing on the 1st.
    [Test]
    public void Build_NoSections_KeepsEverySymbolOut()
    {
        var index = SymbolIndex.Build([PublicData("?Value@@3HA", 1, 0x10), PublicFunction("?Get@@YAHXZ", 1, 0x20)], []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.GlobalRva("Value").IsErr, Is.True);
            Assert.That(index.FunctionRva("?Get@@YAHXZ").IsErr, Is.True);
        }
    }

    // A name in UTF-8 compares like any other.
    [Test]
    public void GlobalRva_NonAsciiName_ResolvesThroughPublic()
    {
        var index = Index(PublicData("?Größe@Bühne@@2HA", 2, 0x10));

        Assert.That(index.GlobalRva("Bühne::Größe").Unwrap(), Is.EqualTo(0x3010));
    }

    // A table of a real size keeps each name at its own RVA.
    [Test]
    public void Build_ManyPublics_FindsTheFirstAndTheLast()
    {
        var symbols = Enumerable.Range(0, 200_000).Select(static offset => (ICodeViewSymbol)PublicData($"?Value{offset}@@3HA", 1, (uint)offset));

        var index = SymbolIndex.Build(symbols, _sections);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.GlobalRva("Value0").Unwrap(), Is.EqualTo(0x1000));
            Assert.That(index.GlobalRva("Value199999").Unwrap(), Is.EqualTo(0x1000 + 199_999));
        }
    }

    private static SymbolIndex Index(params ICodeViewSymbol[] symbols)
    {
        return SymbolIndex.Build(symbols, _sections);
    }

    private static PublicSymbol PublicData(string name, ushort section, uint offset)
    {
        return new PublicSymbol(section, offset, name, PublicSymbolAttributes.None);
    }

    private static PublicSymbol PublicFunction(string name, ushort section, uint offset)
    {
        return new PublicSymbol(section, offset, name, PublicSymbolAttributes.Function);
    }

    private static DataSymbol Static(string name, ushort section, uint offset)
    {
        return new DataSymbol(name, _int)
        {
            SegmentIndex = section,
            Offset = offset,
        };
    }
}
