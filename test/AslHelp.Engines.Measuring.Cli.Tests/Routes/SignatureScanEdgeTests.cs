using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class SignatureScanEdgeTests
{
    private const uint Global = 0x2000;
    private const uint Other = 0x3000;

    private static readonly RouteCandidate _anyRegister = new("8B ?? ????????", 2, 1, 6);
    private static readonly RouteCandidate _load = new("8B 0D ????????", 2, 1, 6);

    // A match can start inside the masked bytes of another, so the search moves on 1 byte at a time.
    [Test]
    public void Search_OverlappingMatches_ChecksBoth()
    {
        RouteCandidate candidate = new("68 ????????", 1, 1, 5);
        var image = new CodeBuilder(false).Raw(0x68).Absolute(0x2068).Nops(4).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.Null);
    }

    // 2 sections that reach 2 RVAs fail like 2 matches in 1 section.
    [Test]
    public void Search_TwoSectionsDifferentRvas_ReturnsNull()
    {
        CodeImage image = new(
            false,
            CodeBuilder.DefaultBase,
            CodeBuilder.ImageSize,
            [new CodeBuilder(false).MovEcx(Global).Section(), new CodeBuilder(false, rva: 0x5000).MovEcx(Other).Section()]);

        Assert.That(new SignatureScan(_load).Search(image), Is.Null);
    }

    // A section shorter than the pattern does not stop the search of the next section.
    [Test]
    public void Search_ShortSectionFirst_SearchesTheNext()
    {
        CodeImage image = new(
            false,
            CodeBuilder.DefaultBase,
            CodeBuilder.ImageSize,
            [new CodeBuilder(false).Nops(2).Section(), new CodeBuilder(false, rva: 0x5000).MovEcx(Global).Section()]);

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(Global));
    }

    // An x64 match resolves against its own section, so the same RVA from 2 sections has 2 different displacements.
    [Test]
    public void Search_X64TwoSectionsSameRva_ReachesIt()
    {
        RouteCandidate candidate = new("48 8B 05 ????????", 3, 1, 7);
        CodeImage image = new(
            true,
            0x0001_4000_0000,
            CodeBuilder.ImageSize,
            [new CodeBuilder(true).MovRax(Global).Section(), new CodeBuilder(true, rva: 0x5000).MovRax(Global).Section()]);

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(Global));
    }

    // A match without an address operand fails the version, before or after a match that reaches the global.
    [TestCase(true)]
    [TestCase(false)]
    public void Search_GoodMatchAndMatchWithoutOperand_ReturnsNull(bool goodFirst)
    {
        CodeBuilder builder = new(false);
        _ = goodFirst ? builder.MovEcx(Global).Raw(0x8B, 0xC1).Nops(4) : builder.Raw(0x8B, 0xC1).Nops(4).MovEcx(Global);

        Assert.That(new SignatureScan(_anyRegister).Search(builder.Build()), Is.Null);
    }

    // An x86 address below the image base doesn't have an RVA, so it must not wrap around to a large one.
    [Test]
    public void Search_X86AddressBelowImageBase_ReturnsNull()
    {
        var image = new CodeBuilder(false).Raw(0x8B, 0x0D, 0x10, 0x00, 0x00, 0x00).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.Null);
    }

    // An x64 displacement can resolve below the image base and doesn't have an RVA either.
    [Test]
    public void Search_X64AddressBelowImageBase_ReturnsNull()
    {
        RouteCandidate candidate = new("48 8B 05 ????????", 3, 1, 7);
        var image = new CodeBuilder(true, 0x0001_4000_0000).Raw(0x48, 0x8B, 0x05, 0x00, 0x00, 0x00, 0x80).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.Null);
    }

    // The offset picks the operand when an instruction has 2 addresses.
    [TestCase(2, Global)]
    [TestCase(6, Other)]
    public void Search_TwoOperandsInOneInstruction_OffsetPicksTheOperand(int offset, uint expected)
    {
        RouteCandidate candidate = new("C7 05 ???????? ????????", offset, 1, 10);
        var image = new CodeBuilder(false).Raw(0xC7, 0x05).Absolute(Global).Absolute(Other).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(expected));
    }

    // An x64 operand with an immediate after it fails here as well as in the site search, because the read step reaches the wrong address.
    [Test]
    public void Search_X64OperandBeforeImmediate_ReturnsNull()
    {
        RouteCandidate candidate = new("83 3D ???????? 00", 2, 1, 7);
        var image = new CodeBuilder(true).Raw(0x83, 0x3D).Relative(Global, trailing: 1).Raw(0x00).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.Null);
    }

    // An image without code doesn't have a match, whether its section is missing or empty.
    [Test]
    public void Search_ImageWithoutCode_ReturnsNull()
    {
        CodeImage noSection = new(false, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, []);
        var emptySection = new CodeBuilder(false).Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new SignatureScan(_load).Search(noSection), Is.Null);
            Assert.That(new SignatureScan(_load).Search(emptySection), Is.Null);
        }
    }

    // The only match sits at the very end of a section of 16 MB, about the size of 1 real executable.
    [Test]
    public void Search_LargeSectionMatchAtTheEnd_FindsIt()
    {
        var image = new CodeBuilder(false).Nops(16 * 1024 * 1024).MovEcx(Global).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(Global));
    }
}
