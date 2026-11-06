using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class RouteCandidatesTests
{
    private const uint Global = 0x2000;
    private const uint Other = 0x3000;

    private static readonly HashSet<ulong> _wanted = [CodeBuilder.DefaultBase + Global];

    // The shortest candidate is the site alone, with the offset of the operand in its instruction.
    [Test]
    public void Enumerate_SiteAlone_FirstCandidateIsTheSiteInstruction()
    {
        var first = All(Window(new CodeBuilder(false).MovEcx(Global).Ret())).First();

        Assert.That(first, Is.EqualTo(new RouteCandidate("8B 0D ????????", 2, 1, 6)));
    }

    // Growth takes 1 instruction at a time, after the site 1st and before it 2nd. A neighbor before the site moves the offset.
    [Test]
    public void Enumerate_NeighborOnEachSide_GrowsOneInstructionAtATime()
    {
        var candidates = All(Window(new CodeBuilder(false).Raw(0x6A, 0x01).MovEcx(Global).Ret())).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates[1], Is.EqualTo(new RouteCandidate("8B 0D ???????? C3", 2, 2, 7)));
            Assert.That(candidates[2], Is.EqualTo(new RouteCandidate("6A 01 8B 0D ????????", 4, 2, 8)));
            Assert.That(candidates[3], Is.EqualTo(new RouteCandidate("6A 01 8B 0D ???????? C3", 4, 3, 9)));
        }
    }

    // Growth stops at 8 instructions on each side, even with more instructions in reach of the 64 bytes.
    [Test]
    public void Enumerate_MoreThan8EachWay_StopsAt8()
    {
        var window = Window(new CodeBuilder(false).Nops(10).MovEcx(Global).Nops(10));

        var candidates = All(window).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates.Max(static candidate => candidate.Instructions), Is.EqualTo(17));
            Assert.That(candidates.Max(static candidate => candidate.Offset), Is.EqualTo(8 + 2));
            Assert.That(candidates.Max(static candidate => candidate.Bytes - candidate.Offset), Is.EqualTo(4 + 8));
        }
    }

    // Enumerate skips a candidate above 64 bytes, so 9 instructions of 7 bytes fit and 10 do not.
    [Test]
    public void Enumerate_WindowOver64Bytes_IsSkipped()
    {
        CodeBuilder builder = new(true);

        for (int index = 0; index < 5; index++)
        {
            _ = builder.LeaRcx(Other);
        }

        _ = builder.MovRax(Global);

        for (int index = 0; index < 5; index++)
        {
            _ = builder.LeaRcx(Other);
        }

        var candidates = All(InstructionSites.Find(builder.Build(), _wanted).Single()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates.Max(static candidate => candidate.Bytes), Is.EqualTo(63));
            Assert.That(candidates.Max(static candidate => candidate.Instructions), Is.EqualTo(9));
        }
    }

    // Each masked operand is 1 token and 2 next to each other keep a space. The parser of the memory library reads every text.
    [Test]
    public void Enumerate_TwoOperandsNextToEachOther_RendersOneTokenEach()
    {
        var window = Window(new CodeBuilder(false).Raw(0xC7, 0x05).Absolute(Global).Absolute(Other).Call(0x10));

        var candidates = All(window).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates[0].Forward, Is.EqualTo("C7 05 ???????? ????????"));
            Assert.That(candidates[1].Forward, Is.EqualTo("C7 05 ???????? ???????? E8 ????????"));

            foreach (var candidate in candidates)
            {
                Assert.That(ScanPattern.TryParse(candidate.Forward, out var pattern), Is.True, candidate.Forward);
                Assert.That(pattern.Length, Is.EqualTo(candidate.Bytes), candidate.Forward);
            }
        }
    }

    // The register tier differs from the strict tier only in the ModRM byte and keeps the offset.
    [Test]
    public void Enumerate_RegisterTier_MasksOnlyTheModRmByteMore()
    {
        var window = Window(new CodeBuilder(false).MovEcx(Global).Ret());

        var candidate = RouteCandidates.Enumerate(window, MaskTier.Register, 2).Single();

        Assert.That(candidate, Is.EqualTo(new RouteCandidate("8B ?? ???????? C3", 2, 2, 7)));
    }

    // A load through a SIB byte masks its ModRM byte and keeps the SIB byte, because only the ModRM byte has the register.
    [Test]
    public void Enumerate_RegisterTierOnLoadWithSibByte_MasksTheModRmByte()
    {
        var window = Window(new CodeBuilder(false).Raw(0x8B, 0x04, 0x25).Absolute(Global));

        var candidate = RouteCandidates.Enumerate(window, MaskTier.Register, 1).Single();

        Assert.That(candidate, Is.EqualTo(new RouteCandidate("8B ?? 25 ????????", 3, 1, 7)));
    }

    // A site without a ModRM byte has nothing to loosen.
    [Test]
    public void Enumerate_RegisterTierOnImmediateSite_YieldsNothing()
    {
        var window = Window(new CodeBuilder(false).Push(Global).Ret());

        Assert.That(RouteCandidates.Enumerate(window, MaskTier.Register, 1), Is.Empty);
    }

    private static SiteWindow Window(CodeBuilder builder)
    {
        return InstructionSites.Find(builder.Build(), _wanted).Single();
    }

    private static IEnumerable<RouteCandidate> All(SiteWindow window)
    {
        return Enumerable.Range(1, RouteCandidates.MostInstructions)
            .SelectMany(instructions => RouteCandidates.Enumerate(window, MaskTier.Strict, instructions));
    }
}
