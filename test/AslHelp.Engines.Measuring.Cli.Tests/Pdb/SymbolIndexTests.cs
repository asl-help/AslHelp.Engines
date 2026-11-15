using AslHelp.Engines.Measuring.Cli.Pdb;
using AsmResolver.Symbols.Pdb;
using AsmResolver.Symbols.Pdb.Records;
using NUnit.Framework;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class SymbolIndexTests
{
    private const string ValueDecorated = "?Value@@3HA";

    private static readonly uint[] _sections = [0x1000, 0x3000, 0x8000];

    // The name without its decoration finds the public, at the RVA of its section plus its offset.
    [Test]
    public void GlobalRva_UndecoratedData_ResolvesThroughPublic()
    {
        var index = Index(PublicData(ValueDecorated, 2, 0x10));

        Assert.That(index.GlobalRva("Value").Unwrap(), Is.EqualTo(0x3010));
    }

    // The full decorated name finds the same RVA.
    [Test]
    public void GlobalRva_DecoratedName_ResolvesExactly()
    {
        var index = Index(PublicData(ValueDecorated, 2, 0x10));

        Assert.That(index.GlobalRva(ValueDecorated).Unwrap(), Is.EqualTo(0x3010));
    }

    // A name without decoration, such as a C name, only matches itself.
    [Test]
    public void GlobalRva_PlainName_ResolvesExactly()
    {
        var index = Index(PublicData("_value", 1, 0x20));

        Assert.That(index.GlobalRva("_value").Unwrap(), Is.EqualTo(0x1020));
    }

    // A nested scope reads outer scope first in C++ and last in the decoration.
    [Test]
    public void GlobalRva_NestedScope_ResolvesByScopeSyntax()
    {
        var index = Index(PublicData("?Value@Inner@Outer@@2HA", 3, 0x4));

        Assert.That(index.GlobalRva("Outer::Inner::Value").Unwrap(), Is.EqualTo(0x8004));
    }

    // A data public counts at its RVA, while a function and any other RVA don't.
    [Test]
    public void HasGlobalAt_DataAndFunctionPublics_FindsOnlyTheData()
    {
        var index = Index(PublicData(ValueDecorated, 2, 0x10), PublicFunction("?Get@Table@@SAHXZ", 1, 0x40));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.HasGlobalAt(0x3010), Is.True);
            Assert.That(index.HasGlobalAt(0x3011), Is.False);
            Assert.That(index.HasGlobalAt(0x1040), Is.False);
        }
    }

    // A function resolves through FunctionRva and stays out of GlobalRva.
    [Test]
    public void FunctionRva_ScopedFunction_ResolvesByScopeSyntax()
    {
        var index = Index(PublicFunction("?Get@Table@@SAHXZ", 1, 0x40));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.FunctionRva("Table::Get").Unwrap(), Is.EqualTo(0x1040));
            Assert.That(index.FunctionRva("?Get@Table@@SAHXZ").Unwrap(), Is.EqualTo(0x1040));
            Assert.That(index.GlobalRva("Table::Get").IsErr, Is.True);
        }
    }

    // Data stays out of FunctionRva.
    [Test]
    public void FunctionRva_Data_FailsWithName()
    {
        var index = Index(PublicData(ValueDecorated, 2, 0x10));

        Assert.That(index.FunctionRva("Value").UnwrapErr().Message, Does.Contain("'Value'"));
    }

    // 2 overloads share a C++ name, so that name fails with both RVAs and each decorated name still works.
    [Test]
    public void FunctionRva_TwoOverloads_FailsListingBoth()
    {
        var index = Index(PublicFunction("?Get@Table@@SAHXZ", 1, 0x40), PublicFunction("?Get@Table@@SAHH@Z", 1, 0x80));

        string message = index.FunctionRva("Table::Get").UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("0x1040"));
            Assert.That(message, Does.Contain("0x1080"));
            Assert.That(index.FunctionRva("?Get@Table@@SAHH@Z").Unwrap(), Is.EqualTo(0x1080));
        }
    }

    // A name doesn't find a longer name that starts with it.
    [Test]
    public void GlobalRva_PrefixOfLongerName_DoesNotMatch()
    {
        var index = Index(PublicData("?EngineIni@@3HA", 2, 0x10));

        Assert.That(index.GlobalRva("Engine").UnwrapErr().Message, Does.Contain("'Engine'"));
    }

    // A template scope stays out of the C++ spelling, so only the decorated name finds it.
    [Test]
    public void GlobalRva_TemplateScope_FindsOnlyTheDecoratedName()
    {
        const string Decorated = "?Value@?$List@H@@2HA";
        var index = Index(PublicData(Decorated, 2, 0x10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.GlobalRva(Decorated).Unwrap(), Is.EqualTo(0x3010));
            Assert.That(index.GlobalRva("List::Value").IsErr, Is.True);
            Assert.That(index.GlobalRva("H::?$List::Value").IsErr, Is.True);
        }
    }

    // A special member starts with 2 question marks and only the decorated name finds it.
    [Test]
    public void FunctionRva_Constructor_FindsOnlyTheDecoratedName()
    {
        const string Decorated = "??0Table@@QEAA@XZ";
        var index = Index(PublicFunction(Decorated, 1, 0x40));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(index.FunctionRva(Decorated).Unwrap(), Is.EqualTo(0x1040));
            Assert.That(index.FunctionRva("Table").IsErr, Is.True);
        }
    }

    // A name without a symbol fails with the name.
    [Test]
    public void GlobalRva_Absent_FailsWithName()
    {
        Assert.That(Index().GlobalRva("Value").UnwrapErr().Message, Does.Contain("'Value'"));
    }

    // An absolute symbol or one from a section the PDB doesn't list stays out instead of throwing.
    [TestCase((ushort)0)]
    [TestCase((ushort)4)]
    [TestCase((ushort)9)]
    public void GlobalRva_SectionIndexOutOfRange_SkipsSymbol(ushort section)
    {
        var index = Index(PublicData(ValueDecorated, section, 0x10));

        Assert.That(index.GlobalRva("Value").IsErr, Is.True);
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
}
