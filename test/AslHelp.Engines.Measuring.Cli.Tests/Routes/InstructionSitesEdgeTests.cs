using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class InstructionSitesEdgeTests
{
    private const uint Global = 0x2000;
    private const uint Other = 0x3000;
    private const ulong X64Base = 0x0001_4000_0000;

    private static readonly int[] _load = [2, 3, 4, 5];
    private static readonly int[] _loadAndLargeOperand = [3, 4, 5, 6, 9, 10, 11, 12, 13, 14, 15, 16];
    private static readonly int[] _loadAndPush = [2, 3, 4, 5, 7, 8, 9, 10];
    private static readonly int[] _ripLoad = [3, 4, 5, 6];
    private static readonly HashSet<ulong> _wanted = [CodeBuilder.DefaultBase + Global];
    private static readonly HashSet<ulong> _wantedX64 = [X64Base + Global];

    // 2 wanted addresses in 1 instruction make 2 sites, because each global needs its own operand.
    [Test]
    public void Find_TwoWantedAddressesInOneInstruction_ReturnsBothSites()
    {
        var image = new CodeBuilder(false).Raw(0xC7, 0x05).Absolute(Global).Absolute(Other).Build();
        HashSet<ulong> wanted = [CodeBuilder.DefaultBase + Global, CodeBuilder.DefaultBase + Other];

        var windows = InstructionSites.Find(image, wanted);

        Assert.That(windows, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(windows[0].Address, Is.EqualTo(CodeBuilder.DefaultBase + Global));
            Assert.That(windows[0].OperandAt, Is.EqualTo(2));
            Assert.That(windows[0].RegisterAt, Is.EqualTo(1));
            Assert.That(windows[1].Address, Is.EqualTo(CodeBuilder.DefaultBase + Other));
            Assert.That(windows[1].OperandAt, Is.EqualTo(6));
            Assert.That(windows[1].RegisterAt, Is.EqualTo(-1));
        }
    }

    // An 8-byte immediate with an address of the image gets masked, because it differs between versions.
    [Test]
    public void Find_X64ImmediateInsideImage_MasksIt()
    {
        var image = new CodeBuilder(true, X64Base).MovRax(Global).Raw(0x48, 0xB9).Raw(BitConverter.GetBytes(X64Base + Other)).Build();

        var window = InstructionSites.Find(image, _wantedX64).Single();

        Assert.That(Masked(window), Is.EqualTo(_loadAndLargeOperand));
    }

    // An 8-byte absolute address in a load gets masked, because it differs between versions.
    [Test]
    public void Find_X64AbsoluteLoadInsideImage_MasksIt()
    {
        var image = new CodeBuilder(true, X64Base).MovRax(Global).Raw(0x48, 0xA1).Raw(BitConverter.GetBytes(X64Base + Other)).Build();

        var window = InstructionSites.Find(image, _wantedX64).Single();

        Assert.That(Masked(window), Is.EqualTo(_loadAndLargeOperand));
    }

    // An 8-byte immediate outside the image is a plain value and stays literal.
    [Test]
    public void Find_X64ImmediateOutsideImage_StaysLiteral()
    {
        var image = new CodeBuilder(true, X64Base).MovRax(Global).Raw(0x48, 0xB9).Raw(BitConverter.GetBytes(0x1234ul)).Build();

        var window = InstructionSites.Find(image, _wantedX64).Single();

        Assert.That(Masked(window), Is.EqualTo(_ripLoad));
    }

    // The 1st byte of the image is inside it and the byte after its last is outside it.
    [TestCase(CodeBuilder.DefaultBase - 1, false)]
    [TestCase(CodeBuilder.DefaultBase, true)]
    [TestCase(CodeBuilder.DefaultBase + CodeBuilder.ImageSize - 1, true)]
    [TestCase(CodeBuilder.DefaultBase + CodeBuilder.ImageSize, false)]
    public void Find_X86ImmediateAtImageEdge_MasksOnlyInside(ulong value, bool masked)
    {
        var image = new CodeBuilder(false).MovEcx(Global).Raw(0x68).Raw(BitConverter.GetBytes((uint)value)).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(Masked(window), Is.EqualTo(masked ? _loadAndPush : _load));
    }

    // An image above 2 GB keeps its addresses unsigned, so its load and its immediate both count.
    [Test]
    public void Find_X86ImageAbove2Gb_FindsAndMasksAddresses()
    {
        const ulong HighBase = 0xF000_0000;
        var image = new CodeBuilder(false, HighBase).MovEcx(Global).Push(Other).Build();

        var window = InstructionSites.Find(image, new HashSet<ulong> { HighBase + Global }).Single();

        Assert.That(Masked(window), Is.EqualTo(_loadAndPush));
    }

    // A RIP-relative operand can point backward, past the start of the section.
    [Test]
    public void Find_X64LoadOfAddressBeforeTheSection_FindsIt()
    {
        var image = new CodeBuilder(true, X64Base, 0x8000).MovRax(Global).Build();

        Assert.That(InstructionSites.Find(image, _wantedX64), Has.Count.EqualTo(1));
    }

    // The 8 instructions before a site come from a ring of positions that has wrapped several times by then.
    [Test]
    public void Find_SiteAfterManyInstructions_StartsAtThe8thBefore()
    {
        CodeBuilder builder = new(false);

        for (int index = 0; index < 12; index++)
        {
            _ = builder.Nops(1).Raw(0x6A, 0x01);
        }

        var image = builder.MovEcx(Global).Ret().Build();
        byte[] expected = [.. image.Sections[0].Bytes[(12 * 3)..]];

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Site, Is.EqualTo(RouteCandidates.MostNeighbors));
            Assert.That(window.Starts[window.Site], Is.EqualTo(4 * 3));
            Assert.That(window.OperandAt, Is.EqualTo((4 * 3) + 2));
            Assert.That(window.Bytes[(4 * 3)..], Is.EqualTo(expected));
        }
    }

    // A window never reaches into the section before it and a RIP-relative operand resolves against its own section.
    [Test]
    public void Find_SiteFirstInSecondSection_HasNoNeighborsBefore()
    {
        CodeImage image = new(
            true,
            X64Base,
            CodeBuilder.ImageSize,
            [new CodeBuilder(true, X64Base).Nops(3).Section(), new CodeBuilder(true, X64Base, 0x5000).MovRax(Global).Section()]);

        var window = InstructionSites.Find(image, _wantedX64).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Site, Is.Zero);
            Assert.That(window.Starts, Has.Length.EqualTo(1));
        }
    }

    // An instruction cut off by the end of the section ends the window and does not throw.
    [Test]
    public void Find_CutInstructionAtSectionEnd_EndsTheWindow()
    {
        var image = new CodeBuilder(false).MovEcx(Global).Raw(0x8B, 0x0D, 0x00).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(window.Starts, Has.Length.EqualTo(1));
    }

    // The ModRM byte comes right after the opcode, so 0x25 is the SIB byte after 8B 04 and the ModRM byte after 0F 14.
    [TestCase(new byte[] { 0x8B, 0x04, 0x25 }, 1)]
    [TestCase(new byte[] { 0x0F, 0x14, 0x25 }, 2)]
    public void Find_ByteBeforeTheOperandIs0x25_RegisterAtIsTheModRmByte(byte[] load, int registerAt)
    {
        var image = new CodeBuilder(false).Raw(load).Absolute(Global).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(window.RegisterAt, Is.EqualTo(registerAt));
    }

    // An image without code doesn't have sites, whether its section is missing or empty.
    [Test]
    public void Find_ImageWithoutCode_ReturnsNothing()
    {
        CodeImage noSection = new(false, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, []);
        var emptySection = new CodeBuilder(false).Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(InstructionSites.Find(noSection, _wanted), Is.Empty);
            Assert.That(InstructionSites.Find(emptySection, _wanted), Is.Empty);
        }
    }

    private static IEnumerable<int> Masked(SiteWindow window)
    {
        return Enumerable.Range(0, window.Operands.Length).Where(position => window.Operands[position] != 0);
    }
}
