using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class SignatureScanTests
{
    private const uint Global = 0x2000;
    private const uint Other = 0x3000;

    private static readonly RouteCandidate _load = new("8B 0D ????????", 2, 1, 6);

    // An x86 match reaches the absolute address minus the image base.
    [Test]
    public void Search_OneX86Match_ReachesTheAbsoluteAddress()
    {
        var image = new CodeBuilder(false).Nops(3).MovEcx(Global).Ret().Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(Global));
    }

    // An x64 match reaches relative to the byte after the operand, in both directions.
    [TestCase(0x2000u, 0x1000u)]
    [TestCase(0x1000u, 0x8000u)]
    public void Search_OneX64Match_AddsRelativeToTheNextByte(uint target, uint sectionRva)
    {
        RouteCandidate candidate = new("48 8B 05 ????????", 3, 1, 7);
        var image = new CodeBuilder(true, 0x0001_4000_0000, sectionRva).Nops(1).MovRax(target).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(target));
    }

    // 2 matches in 2 sections that reach 1 RVA are fine.
    [Test]
    public void Search_TwoMatchesSameRva_ReachesIt()
    {
        CodeImage image = new(
            false,
            CodeBuilder.DefaultBase,
            CodeBuilder.ImageSize,
            [new CodeBuilder(false).MovEcx(Global).Section(), new CodeBuilder(false, rva: 0x5000).MovEcx(Global).Section()]);

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(Global));
    }

    // 2 matches that reach 2 RVAs don't have a result, because a whole-module search then fails at run time.
    [Test]
    public void Search_TwoMatchesDifferentRvas_ReturnsNull()
    {
        var image = new CodeBuilder(false).MovEcx(Global).MovEcx(Other).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.Null);
    }

    // Code without the bytes doesn't have a match.
    [Test]
    public void Search_NoMatch_ReturnsNull()
    {
        var image = new CodeBuilder(false).MovEdx(Global).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.Null);
    }

    // A masked register byte can match a register move, where the offset points at the next instruction and not at an operand.
    [Test]
    public void Search_MatchWhoseOffsetIsNotAnOperand_ReturnsNull()
    {
        RouteCandidate candidate = new("8B ?? ????????", 2, 1, 6);
        var image = new CodeBuilder(false).Raw(0x8B, 0xC1).Nops(4).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.Null);
    }

    // A base register makes the masked bytes a field offset and not an address.
    [Test]
    public void Search_MatchWithBaseRegister_ReturnsNull()
    {
        RouteCandidate candidate = new("8B ?? ????????", 2, 1, 6);
        var image = new CodeBuilder(false).Raw(0x8B, 0x89).Absolute(Global).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.Null);
    }

    // A branch target at the offset is no address operand.
    [Test]
    public void Search_MatchOnBranchTarget_ReturnsNull()
    {
        RouteCandidate candidate = new("E8 ????????", 1, 1, 5);
        var image = new CodeBuilder(false).Call(0x100).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.Null);
    }

    // A section shorter than the pattern doesn't have a match and does not throw.
    [Test]
    public void Search_PatternLongerThanSection_ReturnsNull()
    {
        var image = new CodeBuilder(false).Raw(0x8B).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.Null);
    }

    // A match that ends with the section counts and a match 1 byte longer than the section does not.
    [Test]
    public void Search_MatchEndsAtSectionEnd_Reads()
    {
        var whole = new CodeBuilder(false).MovEcx(Global).Build();
        var cut = whole with { Sections = [new CodeSection(CodeBuilder.CodeRva, whole.Sections[0].Bytes[..^1])] };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new SignatureScan(_load).Search(whole), Is.EqualTo(Global));
            Assert.That(new SignatureScan(_load).Search(cut), Is.Null);
        }
    }

    // A pattern with 1 literal byte still finds every match, so the search on the longest literal run works at its shortest.
    [Test]
    public void Search_AllBytesMaskedExceptOne_UsesTheLiteralRun()
    {
        RouteCandidate candidate = new("68 ????????", 1, 1, 5);
        var same = new CodeBuilder(false).Push(Global).Nops(2).Push(Global).Build();
        var different = new CodeBuilder(false).Push(Global).Nops(2).Push(Other).Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new SignatureScan(candidate).Search(same), Is.EqualTo(Global));
            Assert.That(new SignatureScan(candidate).Search(different), Is.Null);
        }
    }

    // The literal run after a masked operand also matches, at a position before the run.
    [Test]
    public void Search_LongestRunAfterTheOperand_FindsTheMatchBeforeIt()
    {
        RouteCandidate candidate = new("8B 0D ???????? 6A 01 6A 02", 2, 3, 10);
        var image = new CodeBuilder(false).MovEcx(Global).Raw(0x6A, 0x01, 0x6A, 0x02).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(Global));
    }
}
