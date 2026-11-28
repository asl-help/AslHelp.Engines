using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

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

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(ScanResult.Reached(Global)));
    }

    // An x64 match reaches relative to the byte after the operand, in both directions.
    [TestCase(0x2000u, 0x1000u)]
    [TestCase(0x1000u, 0x8000u)]
    public void Search_OneX64Match_AddsRelativeToTheNextByte(uint target, uint sectionRva)
    {
        RouteCandidate candidate = new("48 8B 05 ????????", 3, 1, 7);
        var image = new CodeBuilder(true, 0x0001_4000_0000, sectionRva).Nops(1).MovRax(target).Build();

        Assert.That(new SignatureScan(candidate).Search(image).Rva, Is.EqualTo(target));
    }

    // A nibble token matches any value of its masked half and nothing outside its fixed half.
    [TestCase((byte)0x48, true)]
    [TestCase((byte)0x4C, true)]
    [TestCase((byte)0x58, false)]
    public void Search_NibbleToken_MatchesOnlyItsFixedHalf(byte prefix, bool matched)
    {
        RouteCandidate candidate = new("4? 8B 0D ????????", 3, 1, 7);
        var image = new CodeBuilder(true, 0x0001_4000_0000).Raw(prefix, 0x8B, 0x0D).Relative(Global).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(matched ? ScanResult.Reached(Global) : ScanResult.None));
    }

    // mov ecx, [ebp + 8] also matches the nibble token, so the route conflicts, because a run time scan reads its bytes as an address.
    [Test]
    public void Search_NibbleTokenAlsoMatchesFieldLoad_IsConflicting()
    {
        RouteCandidate candidate = new("8B ?D ????????", 2, 1, 6);
        var image = new CodeBuilder(false).MovEcx(Global).Raw(0x8B, 0x4D, 0x08).Nops(4).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(ScanResult.Conflicting));
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

        Assert.That(new SignatureScan(_load).Search(image).Rva, Is.EqualTo(Global));
    }

    // 2 matches that reach 2 RVAs don't have a result, because a whole-module search then fails at run time.
    [Test]
    public void Search_TwoMatchesDifferentRvas_IsConflicting()
    {
        var image = new CodeBuilder(false).MovEcx(Global).MovEcx(Other).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(ScanResult.Conflicting));
    }

    // Code without the bytes doesn't have a match.
    [Test]
    public void Search_NoMatch_IsNone()
    {
        var image = new CodeBuilder(false).MovEdx(Global).Build();

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(ScanResult.None));
    }

    // A masked register byte can match a register move, where the offset points at the next instruction and not at an operand.
    [Test]
    public void Search_MatchWhoseOffsetIsNotAnOperand_ReturnsNull()
    {
        RouteCandidate candidate = new("8B ?? ????????", 2, 1, 6);
        var image = new CodeBuilder(false).Raw(0x8B, 0xC1).Nops(4).Build();

        Assert.That(new SignatureScan(candidate).Search(image).Rva, Is.Null);
    }

    // A base register makes the masked bytes a field offset and not an address.
    [Test]
    public void Search_MatchWithBaseRegister_ReturnsNull()
    {
        RouteCandidate candidate = new("8B ?? ????????", 2, 1, 6);
        var image = new CodeBuilder(false).Raw(0x8B, 0x89).Absolute(Global).Build();

        Assert.That(new SignatureScan(candidate).Search(image).Rva, Is.Null);
    }

    // An address at or past the end of the image doesn't have an RVA, like an address below the image base.
    [TestCase(CodeBuilder.ImageSize - 1, true)]
    [TestCase(CodeBuilder.ImageSize, false)]
    public void Search_AddressAtImageEnd_ReachesOnlyInside(uint target, bool reaches)
    {
        var image = new CodeBuilder(false).MovEcx(target).Build();

        Assert.That(new SignatureScan(_load).Search(image).Rva, Is.EqualTo(reaches ? target : null));
    }

    // A branch target at the offset is no address operand.
    [Test]
    public void Search_MatchOnBranchTarget_ReturnsNull()
    {
        RouteCandidate candidate = new("E8 ????????", 1, 1, 5);
        var image = new CodeBuilder(false).Call(0x100).Build();

        Assert.That(new SignatureScan(candidate).Search(image).Rva, Is.Null);
    }

    // A section shorter than the pattern doesn't have a match and does not throw.
    [Test]
    public void Search_PatternLongerThanSection_ReturnsNull()
    {
        var image = new CodeBuilder(false).Raw(0x8B).Build();

        Assert.That(new SignatureScan(_load).Search(image).Rva, Is.Null);
    }

    // A match that ends with the section counts and a match 1 byte longer than the section reads into the zeros after it and reaches nothing.
    [Test]
    public void Search_MatchEndsAtSectionEnd_Reads()
    {
        var whole = new CodeBuilder(false).MovEcx(Global).Build();
        CodeSection code = new(CodeBuilder.CodeRva, whole.Sections[0].Bytes[..^1]);
        CodeImage cut = new(false, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, [code]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new SignatureScan(_load).Search(whole).Rva, Is.EqualTo(Global));
            Assert.That(new SignatureScan(_load).Search(cut), Is.EqualTo(ScanResult.Conflicting));
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
            Assert.That(new SignatureScan(candidate).Search(same).Rva, Is.EqualTo(Global));
            Assert.That(new SignatureScan(candidate).Search(different).Rva, Is.Null);
        }
    }

    // The literal run after a masked operand also matches, at a position before the run.
    [Test]
    public void Search_LongestRunAfterTheOperand_FindsTheMatchBeforeIt()
    {
        RouteCandidate candidate = new("8B 0D ???????? 6A 01 6A 02", 2, 3, 10);
        var image = new CodeBuilder(false).MovEcx(Global).Raw(0x6A, 0x01, 0x6A, 0x02).Build();

        Assert.That(new SignatureScan(candidate).Search(image).Rva, Is.EqualTo(Global));
    }

    // A match in a part without code that reaches another RVA conflicts, because a scan of the whole module at run time can find it first.
    [Test]
    public void Search_MatchInDataReachesOtherRva_IsConflicting()
    {
        var image = new CodeBuilder(false).MovEcx(Global).Build(new CodeBuilder(false, rva: 0x5000).MovEcx(Other).Section());

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(ScanResult.Conflicting));
    }

    // A match in a part without code that reaches the same RVA is fine, like a 2nd match in the code.
    [Test]
    public void Search_MatchInDataReachesSameRva_ReachesIt()
    {
        var image = new CodeBuilder(false).MovEcx(Global).Build(new CodeBuilder(false, rva: 0x5000).MovEcx(Global).Section());

        Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(ScanResult.Reached(Global)));
    }

    // A match in the headers of a real file conflicts, because the loader maps them at the image base.
    [Test]
    public void Search_MatchInHeadersOfFile_IsConflicting()
    {
        TestFolder folder = new();
        string path = Path.Combine(folder.Root, "file.bin");

        try
        {
            CodeBuilder.WritePe(path, new CodeBuilder(false).MovEcx(Global).Build());
            using var image = CodeImage.Read(path).Unwrap();

            // Rebuild the headers with a load in their zeros, because the written headers only have the fields of the format.
            byte[] headers = image.Data[0].Bytes.ToArray();
            new CodeBuilder(false).MovEcx(Other).Section().Bytes.Span.CopyTo(headers.AsSpan(headers.Length - 8));
            CodeSection[] data = [new CodeSection(0, headers), .. image.Data.Skip(1)];
            CodeImage withLoad = new(image.Is64Bit, image.ImageBase, image.ImageSize, image.Sections, data);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(new SignatureScan(_load).Search(image), Is.EqualTo(ScanResult.Reached(Global)));
                Assert.That(new SignatureScan(_load).Search(withLoad), Is.EqualTo(ScanResult.Conflicting));
            }
        }
        finally
        {
            folder.Delete();
        }
    }

    // A pattern whose literal bytes are all 0 matches the zeros that the loader fills in after a part, which reach nothing.
    [Test]
    public void Search_PatternOfZerosInTheFill_IsConflicting()
    {
        RouteCandidate candidate = new("00 00 ????????", 2, 1, 6);
        var image = new CodeBuilder(false).Ret().Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(ScanResult.Conflicting));
    }

    // A pattern of zeros matches the fill of a part without bytes, like uninitialized data, even when that fill is the only zeros of the image.
    [Test]
    public void Search_PatternOfZerosInTheFillOfAnEmptyPart_IsConflicting()
    {
        const uint ImageSize = 0x3000;

        RouteCandidate candidate = new("00 00 00 00 00 00", 2, 1, 6);
        CodeImage image = new(
            false,
            CodeBuilder.DefaultBase,
            ImageSize,
            [new CodeSection(CodeBuilder.CodeRva, Filled(0x1000))],
            [new CodeSection(0, Filled(0x1000)), new CodeSection(0x2000, ReadOnlyMemory<byte>.Empty), new CodeSection(0x2100, Filled(0xF00))]);

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(ScanResult.Conflicting));
    }

    // A match that starts in the zeros before a part and ends in its bytes conflicts, because it reaches nothing.
    [Test]
    public void Search_MatchFromTheZerosIntoAPart_IsConflicting()
    {
        RouteCandidate candidate = new("00 8B 0D ????????", 3, 2, 7);
        var image = new CodeBuilder(false).MovEcx(Global).Build();

        Assert.That(new SignatureScan(candidate).Search(image), Is.EqualTo(ScanResult.Conflicting));
    }

    private static byte[] Filled(int length)
    {
        byte[] bytes = new byte[length];
        bytes.AsSpan().Fill(0xCC);

        return bytes;
    }
}
