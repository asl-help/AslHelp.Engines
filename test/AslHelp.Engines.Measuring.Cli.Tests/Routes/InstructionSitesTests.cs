using AslHelp.Engines.Measuring.Cli.Routes;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class InstructionSitesTests
{
    private const uint Global = 0x2000;
    private const uint Other = 0x3000;

    private static readonly int[] _branchAndLoad = [1, 4, 5, 6, 7];
    private static readonly int[] _callAndLoads = [1, 2, 3, 4, 7, 8, 9, 10, 13, 14, 15, 16];
    private static readonly int[] _load = [2, 3, 4, 5];
    private static readonly int[] _loadAndImmediate = [2, 3, 4, 5, 12, 13, 14, 15];
    private static readonly int[] _push = [1, 2, 3, 4];
    private static readonly HashSet<ulong> _wanted = [CodeBuilder.DefaultBase + Global];

    // An absolute load has its 4 displacement bytes masked and its ModRM byte as the register byte.
    [Test]
    public void Find_X86AbsoluteLoad_MarksTheDisplacementBytes()
    {
        var image = new CodeBuilder(false).MovEcx(Global).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Address, Is.EqualTo(CodeBuilder.DefaultBase + Global));
            Assert.That(window.OperandAt, Is.EqualTo(2));
            Assert.That(window.RegisterAt, Is.EqualTo(1));
            Assert.That(Masked(window), Is.EqualTo(_load));
        }
    }

    // A load of eax in the form without ModRM doesn't have a register byte, because the byte before its operand is the opcode.
    [Test]
    public void Find_X86LoadWithoutModRm_HasNoRegisterByte()
    {
        var image = new CodeBuilder(false).MovEax(Global).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.OperandAt, Is.EqualTo(1));
            Assert.That(window.RegisterAt, Is.EqualTo(-1));
        }
    }

    // A RIP-relative operand reaches its address from the next instruction, so equal bytes at another RVA are no site.
    [Test]
    public void Find_X64RipRelativeLoad_ResolvesAgainstTheNextInstruction()
    {
        var image = new CodeBuilder(true).MovRax(Global).Build();
        var moved = image with { Sections = [new CodeSection(CodeBuilder.CodeRva + 0x10, image.Sections[0].Bytes)] };

        var windows = InstructionSites.Find(image, _wanted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(windows, Has.Count.EqualTo(1));
            Assert.That(windows[0].OperandAt, Is.EqualTo(3));
            Assert.That(windows[0].RegisterAt, Is.EqualTo(2));
            Assert.That(InstructionSites.Find(moved, _wanted), Is.Empty);
        }
    }

    // An operand with an immediate after it is no site, because the read step of a route adds 4 to the operand and reaches the wrong address.
    [Test]
    public void Find_X64LoadWithImmediateAfterDisplacement_IsNoSite()
    {
        var image = new CodeBuilder(true).Raw(0x83, 0x3D).Relative(Global, trailing: 1).Raw(0x00).Build();

        Assert.That(InstructionSites.Find(image, _wanted), Is.Empty);
    }

    // An immediate with the address is a site on x86 and it doesn't have a register byte.
    [Test]
    public void Find_X86PushImmediateOfTheAddress_IsASiteWithoutRegisterByte()
    {
        var image = new CodeBuilder(false).Push(Global).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.OperandAt, Is.EqualTo(1));
            Assert.That(window.RegisterAt, Is.EqualTo(-1));
            Assert.That(Masked(window), Is.EqualTo(_push));
        }
    }

    // On x64 an immediate equal to the address is no site, because x64 code reaches a global RIP-relative.
    [Test]
    public void Find_X64ImmediateEqualToAddress_IsNoSite()
    {
        var image = new CodeBuilder(true).Raw(0xB9).Absolute(Global).Build();

        Assert.That(InstructionSites.Find(image, _wanted), Is.Empty);
    }

    // A displacement off a register stays literal, so a changed field breaks the match.
    [Test]
    public void Find_RegisterRelativeDisplacement_StaysLiteral()
    {
        var image = new CodeBuilder(false).MovEcx(Global).MovFromField().Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(Masked(window), Is.EqualTo(_load));
    }

    // A plain immediate outside the image stays literal and an immediate inside it gets masked like an address.
    [Test]
    public void Find_X86Immediates_MasksOnlyThoseInsideTheImage()
    {
        var image = new CodeBuilder(false).MovEcx(Global).Raw(0xB9, 0x10, 0, 0, 0).Push(Other).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(Masked(window), Is.EqualTo(_loadAndImmediate));
    }

    // A call and a load of another address in the window get their operands masked and keep their opcodes.
    [Test]
    public void Find_CallAndOtherAbsoluteLoadInWindow_MasksBoth()
    {
        var image = new CodeBuilder(false).Call(0x100).MovEcx(Global).MovEdx(Other).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Site, Is.EqualTo(1));
            Assert.That(Masked(window), Is.EqualTo(_callAndLoads));
        }
    }

    // A short branch has its 1 target byte masked.
    [Test]
    public void Find_ShortBranchInWindow_MasksItsTarget()
    {
        var image = new CodeBuilder(false).Raw(0x74, 0x05).MovEcx(Global).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(Masked(window), Is.EqualTo(_branchAndLoad));
    }

    // 2 operands next to each other are 2 masked operands, so the text can keep them apart.
    [Test]
    public void Find_AddressThenImmediateInOneInstruction_NumbersTwoOperands()
    {
        var image = new CodeBuilder(false).Raw(0xC7, 0x05).Absolute(Global).Absolute(Other).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        Assert.That(window.Operands[2], Is.Not.EqualTo(window.Operands[6]));
    }

    // A site at the start of a section has only the instructions it can have before it.
    [Test]
    public void Find_SiteNearSectionStart_HasFewerInstructionsBefore()
    {
        var image = new CodeBuilder(false).Nops(1).MovEcx(Global).Nops(10).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Site, Is.EqualTo(1));
            Assert.That(window.Starts, Has.Length.EqualTo(10));
        }
    }

    // A site before the end of a section has only the instructions it can have after it.
    [Test]
    public void Find_SiteAtSectionEnd_HasFewerInstructionsAfter()
    {
        var image = new CodeBuilder(false).Nops(10).MovEcx(Global).Nops(2).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Site, Is.EqualTo(RouteCandidates.MostNeighbors));
            Assert.That(window.Starts, Has.Length.EqualTo(RouteCandidates.MostNeighbors + 1 + 2));
        }
    }

    // Undecodable bytes end the window on both sides, because a signature over them cannot decode at a match.
    [Test]
    public void Find_InvalidBytesAroundTheSite_EndTheWindow()
    {
        var image = new CodeBuilder(true).Raw(0x06).Nops(3).MovRax(Global).Nops(1).Raw(0x06).Nops(2).Build();

        var window = InstructionSites.Find(image, _wanted).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(window.Site, Is.EqualTo(2));
            Assert.That(window.Starts, Has.Length.EqualTo(4));
        }
    }

    // 2 sites close to each other each get their own window with the other inside it.
    [Test]
    public void Find_TwoSitesSixInstructionsApart_ReturnsBothWindows()
    {
        var image = new CodeBuilder(false).MovEcx(Global).Nops(5).MovEdx(Global).Build();

        var windows = InstructionSites.Find(image, _wanted);

        Assert.That(windows, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(windows[0].Site, Is.Zero);
            Assert.That(windows[1].Site, Is.EqualTo(6));
            Assert.That(windows[1].Starts[0], Is.Zero);
        }
    }

    // A base register makes the same 4 bytes no address operand.
    [Test]
    public void Find_WantedAddressOffABaseRegister_ReturnsNothing()
    {
        var image = new CodeBuilder(false).Raw(0x8B, 0x81).Absolute(Global).Build();

        Assert.That(InstructionSites.Find(image, _wanted), Is.Empty);
    }

    private static IEnumerable<int> Masked(SiteWindow window)
    {
        return Enumerable.Range(0, window.Operands.Length).Where(position => window.Operands[position] != 0);
    }
}
