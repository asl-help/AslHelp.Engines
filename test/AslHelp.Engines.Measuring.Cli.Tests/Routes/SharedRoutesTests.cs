using AslHelp.Engines.Measuring.Cli.Routes;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

// Runs alone, because the worker counter listens to the Parallel events of the whole process.
[NonParallelizable]
public sealed class SharedRoutesTests
{
    private const string Global = "Global";
    private const uint GlobalA = 0x2000;
    private const uint GlobalB = 0x3000;
    private const uint Other = 0x4000;
    private const int PaddingBytes = 0x8_0000;

    private static readonly string[] _a = ["a"];
    private static readonly string[] _aAndB = ["a", "b"];
    private static readonly string[] _aAndC = ["a", "c"];
    private static readonly string[] _b = ["b"];
    private static readonly string[] _bAndA = ["b", "a"];
    private static readonly string[] _c = ["c"];
    private static readonly string[] _threeVersions = ["a", "b", "c"];
    private static readonly string[] _twoForwards = ["8B 0D ????????", "8B 15 ????????"];

    private TestFolder _folder = null!;

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

    // Equal code that loads the global from 2 addresses shares 1 route, with the RVA of each version.
    [Test]
    public void Find_TwoVersionsSameWindow_OneRouteCoversBoth()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).Nops(3).MovEcx(GlobalB).Ret(), GlobalB));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(route.Offset, Is.EqualTo(2));
            Assert.That(route.Versions, Is.EqualTo(_aAndB));
            Assert.That(route.Reached["a"], Is.EqualTo("0x2000"));
            Assert.That(route.Reached["b"], Is.EqualTo("0x3000"));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // 2 sites that reach the 1 RVA of a version count as covered.
    [Test]
    public void Find_VersionWithTwoMatchesSameRva_IsCovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB).Nops(1).MovEcx(GlobalB), GlobalB));

        Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_aAndB));
    }

    // The shortest pattern also loads another global in b, so a wider pattern that matches only the load of the global becomes the route.
    [Test]
    public void Find_VersionWithTwoMatchesDifferentRvas_IsNotCovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(Other).Nops(1).MovEcx(GlobalB).Ret(), GlobalB));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Forward, Is.EqualTo("8B 0D ???????? C3"));
            Assert.That(route.Versions, Is.EqualTo(_aAndB));
        }
    }

    // A match that reaches an RVA outside the list of b refuses the only candidate of a too, because a reader of b would read the wrong value.
    [Test]
    public void Find_RvaReachedNotInVersionList_RefusesTheCandidate()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(Other), GlobalB));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes, Is.Empty);
            Assert.That(found[Global].Uncovered!.Keys, Is.EqualTo(_aAndB));
        }
    }

    // A match in a version without the global refuses the candidate, because that version does not have a right address to reach.
    [Test]
    public void Find_MatchInVersionWithoutTheGlobal_RefusesTheCandidate()
    {
        const string Second = "Second";
        string first = Write("a", new CodeBuilder(false).MovEcx(GlobalA).Ret());
        string second = Write("b", new CodeBuilder(false).MovEcx(Other).Ret());

        var found = Find(
            new RouteVersion("a", first, Globals(GlobalA)),
            new RouteVersion("b", second, new Dictionary<string, IReadOnlyList<uint>> { [Second] = [Other] }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes, Is.Empty);
            Assert.That(found[Global].Uncovered!["a"], Is.EqualTo(SR.UncoveredNoCandidate));
            Assert.That(found[Second].Routes, Is.Empty);
            Assert.That(found[Second].Uncovered!["b"], Is.EqualTo(SR.UncoveredNoCandidate));
        }
    }

    // The route of b also matches in a, which an earlier route covers. The match is fine, because it reaches the global of a.
    [Test]
    public void Find_MatchInCoveredVersionAtItsGlobal_KeepsTheCandidate()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Nops(1).MovEax(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB), GlobalB),
            Version("c", new CodeBuilder(false).MovEax(GlobalA), GlobalA));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[0].Forward, Is.EqualTo("A1 ????????"));
            Assert.That(routes[0].Versions, Is.EqualTo(_aAndC));
            Assert.That(routes[1].Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(routes[1].Versions, Is.EqualTo(_b));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // The list of a global can have 2 statics and only the search shows which 1 the route reaches.
    [Test]
    public void Find_RvaListWithTwoStatics_ReachedShowsWhichOne()
    {
        var found = Find(Version("a", new CodeBuilder(false).MovEcx(GlobalB), GlobalA, GlobalB));

        Assert.That(found[Global].Routes.Single().Reached["a"], Is.EqualTo("0x3000"));
    }

    // A version with other code gets the next route, so a global can have several.
    [Test]
    public void Find_NoCandidateCoversAll_RepeatsOverTheUncovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB), GlobalB),
            Version("c", new CodeBuilder(false).MovEax(GlobalA), GlobalA));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[0].Versions, Is.EqualTo(_aAndB));
            Assert.That(routes[1].Versions, Is.EqualTo(_c));
            Assert.That(routes[1].Forward, Is.EqualTo("A1 ????????"));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // A version with no load of the global ends in the uncovered map and the others keep their route.
    [Test]
    public void Find_VersionNoCandidateReaches_GoesToUncovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB), GlobalB),
            Version("c", new CodeBuilder(false).Nops(8), GlobalA));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_aAndB));
            Assert.That(found[Global].Uncovered!["c"], Is.EqualTo(SR.UncoveredNoCandidate));
        }
    }

    // A 1-instruction candidate that covers 2 of 3 versions loses to a 3-instruction candidate that covers all 3.
    [Test]
    public void Find_ShortCandidateCoversFewer_PrefersTheLongerThatCoversAll()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Nops(1).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB).Nops(1).Ret(), GlobalB),
            Version("c", new CodeBuilder(false).MovEcx(Other).Nops(2).Ret().MovEcx(GlobalA).Nops(1).Ret(), GlobalA));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Forward, Is.EqualTo("8B 0D ???????? 90 C3"));
            Assert.That(route.Versions, Is.EqualTo(_threeVersions));
        }
    }

    // With equal coverage 1 instruction beats 2.
    [Test]
    public void Find_EqualCoverage_PrefersFewerInstructions()
    {
        var found = Find(Version("a", new CodeBuilder(false).Nops(1).MovEcx(GlobalA).Ret(), GlobalA));

        Assert.That(found[Global].Routes.Single().Forward, Is.EqualTo("8B 0D ????????"));
    }

    // With equal coverage and 2 instructions each, 7 bytes beat 8 bytes.
    [Test]
    public void Find_EqualInstructions_PrefersFewerBytes()
    {
        var found = Find(Version("a", new CodeBuilder(false).MovEcx(Other).Ret().Nops(1).MovEcx(GlobalA).Raw(0x6A, 0x01), GlobalA));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Forward, Is.EqualTo("90 8B 0D ????????"));
            Assert.That(route.Offset, Is.EqualTo(3));
        }
    }

    // The ordinal text decides between 2 candidates with equal versions, instructions and bytes, whatever order their sites have in the code.
    [Test]
    public void Find_EqualRank_PrefersOrdinalText()
    {
        var found = Find(Version("a", new CodeBuilder(false).MovEdx(GlobalA).Nops(1).MovEcx(GlobalA), GlobalA));

        Assert.That(found[Global].Routes.Single().Forward, Is.EqualTo("8B 0D ????????"));
    }

    // Linear decoding of b misses its load behind the opcode of a 5-byte move, so only the register tier of the route of a reaches it.
    [Test]
    public void Find_LoadMissedByLinearDecoding_UsesTheRegisterTier()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).Raw(0xB8).MovEdx(GlobalB).Ret(), GlobalB));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[0].Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(routes[0].Versions, Is.EqualTo(_a));
            Assert.That(routes[1].Forward, Is.EqualTo("8B ?? ????????"));
            Assert.That(routes[1].Versions, Is.EqualTo(_b));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // b loads into r9 where a loads into rcx, so only the REX prefix differs and the route of a masks just its low nibble.
    [Test]
    public void Find_RcxAndR9_MasksTheRexNibble()
    {
        var found = Find(
            Version("a", new CodeBuilder(true).Raw(0x48, 0x8B, 0x0D).Relative(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(true).Raw(0xB8, 0x4C, 0x8B, 0x0D).Relative(GlobalB).Ret(), GlobalB));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[1].Forward, Is.EqualTo("4? 8B 0D ????????"));
            Assert.That(routes[1].Versions, Is.EqualTo(_b));
            Assert.That(routes[1].Reached["b"], Is.EqualTo("0x3000"));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // ecx and ebx share the lowest register bit, so the route masks only the high nibble of the ModRM byte.
    [Test]
    public void Find_RegistersWithEqualLowestBit_MasksTheModRmNibble()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).Raw(0xB8, 0x8B, 0x1D).Absolute(GlobalB).Ret(), GlobalB));

        var routes = found[Global].Routes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[1].Forward, Is.EqualTo("8B ?D ????????"));
            Assert.That(routes[1].Versions, Is.EqualTo(_b));
        }
    }

    // mov ecx, [ebp + 8] in b also matches the nibble route, and the load of a has no neighbor to grow by, so b stays uncovered.
    [Test]
    public void Find_NibbleRouteAlsoMatchesFieldLoad_StaysUncovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).Raw(0x8B, 0x4D, 0x08).Nops(4).Raw(0xB8, 0x8B, 0x1D).Absolute(GlobalB).Ret(), GlobalB));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_a));
            Assert.That(found[Global].Uncovered!["b"], Is.EqualTo(SR.UncoveredNoCandidate));
        }
    }

    // The neighbor after the load of b differs from the bytes after mov ecx, [ebp + 8], so the nibble route grows by 1 instruction.
    [Test]
    public void Find_NeighborSetsNibbleRouteApartFromFieldLoad_GrowsTheRoute()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).Raw(0x8B, 0x4D, 0x08).Nops(4).Raw(0xB8, 0x8B, 0x1D).Absolute(GlobalB).Ret(), GlobalB));

        var routes = found[Global].Routes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[1].Forward, Is.EqualTo("8B ?D ???????? C3"));
            Assert.That(routes[1].Versions, Is.EqualTo(_b));
            Assert.That(routes[1].Reached["b"], Is.EqualTo("0x3000"));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // A strict route exists for every version, so the register tier never runs even though 1 loose route could cover both.
    [Test]
    public void Find_StrictRouteForEveryVersion_DoesNotLoosen()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEdx(GlobalB), GlobalB));

        Assert.That(found[Global].Routes.Select(static route => route.Forward), Is.EqualTo(_twoForwards));
    }

    // The register tier also matches 2 RVAs in b, so b stays uncovered.
    [Test]
    public void Find_RegisterTierStillFails_StaysUncovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).Raw(0xB8).MovEdx(GlobalB).Ret().Raw(0xB8).MovEdx(Other).Ret(), GlobalB));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_a));
            Assert.That(found[Global].Uncovered!["b"], Is.EqualTo(SR.UncoveredNoCandidate));
        }
    }

    // 2 runs give the same JSON. Another version order gives the same route with the versions in that order.
    [Test]
    public void Find_SameInputTwice_ReturnsEqualJson()
    {
        var a = Version("a", new CodeBuilder(false).MovEdx(GlobalA).Nops(1).MovEcx(GlobalA).Ret(), GlobalA);
        var b = Version("b", new CodeBuilder(false).MovEcx(GlobalB).Nops(1).MovEdx(GlobalB).Ret(), GlobalB);

        string first = Json(Find(a, b));
        string second = Json(Find(a, b));
        var reversed = Find(b, a)[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(reversed.Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(reversed.Versions, Is.EqualTo(_bAndA));
        }
    }

    // An executable that fails to read fails only its version, with the reason in the map and 1 warning.
    [Test]
    public void Find_ExecutableUnreadable_FailsOnlyThatVersion()
    {
        string missing = Path.Combine(_folder.Root, "missing.bin");
        using StringWriter output = new();
        WriterLogger logger = new(output, TextWriter.Null);

        var found = SharedRoutes.Find(
            [
                Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
                new RouteVersion("b", missing, Globals(GlobalB)),
            ],
            1,
            2,
            logger,
            CancellationToken.None);

        string log = output.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_a));
            Assert.That(found[Global].Uncovered!["b"], Does.Contain(missing));
            Assert.That(log.Split("executable not read").Length - 1, Is.EqualTo(1));
            Assert.That(log, Does.Contain("b: executable not read").And.Contain(missing));
        }
    }

    // A file that is not a PE file fails its version the same way as a missing file.
    [Test]
    public void Find_ExecutableNotPe_FailsOnlyThatVersion()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(path, new byte[16]);

        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            new RouteVersion("b", path, Globals(GlobalB)));

        Assert.That(found[Global].Uncovered!["b"], Does.Contain(path));
    }

    // A global that 1 version lacks lists that version as not found, for that global only.
    [Test]
    public void Find_GlobalMissingInOneVersion_ListsThatVersionAsNotFound()
    {
        const string Second = "Second";
        string first = Write("a", new CodeBuilder(false).MovEcx(GlobalA).Nops(1).MovEdx(Other));
        string second = Write("b", new CodeBuilder(false).MovEdx(Other));

        var found = Find(
            new RouteVersion("a", first, new Dictionary<string, IReadOnlyList<uint>> { [Global] = [GlobalA], [Second] = [Other] }),
            new RouteVersion("b", second, new Dictionary<string, IReadOnlyList<uint>> { [Second] = [Other] }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_a));
            Assert.That(found[Global].Uncovered!["b"], Is.EqualTo(SR.UncoveredNotFound));
            Assert.That(found[Second].Routes.Single().Versions, Is.EqualTo(_aAndB));
            Assert.That(found[Second].Uncovered, Is.Null);
        }
    }

    // 2 x64 images at different bases share 1 route, because the RIP-relative bytes are masked and read per version.
    [Test]
    public void Find_X64Images_RoutesAcrossBases()
    {
        var found = Find(
            Version("a", new CodeBuilder(true, 0x0001_4000_0000).MovRax(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(true, 0x0001_5000_0000).Nops(2).MovRax(GlobalB).Ret(), GlobalB));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Forward, Is.EqualTo("48 8B 05 ????????"));
            Assert.That(route.Offset, Is.EqualTo(3));
            Assert.That(route.Reached["b"], Is.EqualTo("0x3000"));
        }
    }

    // A canceled search throws, because the search of a group can take minutes.
    [Test]
    public void Find_Canceled_Throws()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        var version = Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA);

        Assert.That(() => SharedRoutes.Find([version], 1, 2, NullLogger.Instance, source.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    // Counts the workers of each parallel step, because a step without the limit starts more workers as long as items are left.
    [Test]
    public void Find_OneJob_RunsOneWorkerPerStep()
    {
        // Pad the code, because a scan of a few bytes ends before a 2nd worker of a step without the limit starts. Give e
        // another global and no load, because the conflict check then scans a version that the search of the route left uncached.
        var padded = new CodeBuilder(false).Nops(PaddingBytes).Ret();
        RouteVersion[] versions =
        [
            Version("a", new CodeBuilder(false).Nops(PaddingBytes).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).Nops(PaddingBytes).MovEcx(GlobalB).Ret(), GlobalB),
            Version("c", new CodeBuilder(false).Nops(PaddingBytes).MovEcx(GlobalA).Ret(), GlobalA),
            Version("d", new CodeBuilder(false).Nops(PaddingBytes).MovEcx(GlobalB).Ret(), GlobalB),
            new RouteVersion("e", Write("e", padded), new Dictionary<string, IReadOnlyList<uint>> { ["Y"] = [GlobalA] }),
        ];

        using ParallelWorkers workers = new();
        var found = SharedRoutes.Find(versions, 1, 1, NullLogger.Instance, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Has.Count.EqualTo(4));
            Assert.That(workers.Steps, Is.GreaterThanOrEqualTo(3));
            Assert.That(workers.MostAtOnce, Is.EqualTo(1));
        }
    }

    // A route keeps at least the fewest unmasked bytes, so it grows by the instructions around the load until it has enough.
    [TestCase(1, "8B 0D ????????")]
    [TestCase(3, "8B 0D ???????? C3")]
    [TestCase(5, "33 C0 8B 0D ???????? C3")]
    public void Find_LeastLiterals_GrowsTheRouteUntilItHasEnough(int leastLiterals, string forward)
    {
        RouteVersion[] versions = [Version("a", new CodeBuilder(false).Raw(0x33, 0xC0).MovEcx(GlobalA).Ret(), GlobalA)];

        var found = SharedRoutes.Find(versions, leastLiterals, 2, NullLogger.Instance, CancellationToken.None);

        Assert.That(found[Global].Routes.Single().Forward, Is.EqualTo(forward));
    }

    // A load whose whole window keeps fewer unmasked bytes than the least stays uncovered, because a short route can match random data.
    [Test]
    public void Find_WindowShorterThanLeastLiterals_LeavesTheVersionUncovered()
    {
        RouteVersion[] versions = [Version("a", new CodeBuilder(false).Raw(0x33, 0xC0).MovEcx(GlobalA).Ret(), GlobalA)];

        var found = SharedRoutes.Find(versions, 6, 2, NullLogger.Instance, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes, Is.Empty);
            Assert.That(found[Global].Uncovered, Does.ContainKey("a"));
        }
    }

    private static IReadOnlyDictionary<string, GlobalRoutes> Find(params RouteVersion[] versions)
    {
        return SharedRoutes.Find(versions, 1, 2, NullLogger.Instance, CancellationToken.None);
    }

    private static Dictionary<string, IReadOnlyList<uint>> Globals(params uint[] rvas)
    {
        return new Dictionary<string, IReadOnlyList<uint>> { [Global] = rvas };
    }

    private static string Json(IReadOnlyDictionary<string, GlobalRoutes> routes)
    {
        VersionGroup group = new(null, ["a"], new Dictionary<string, IReadOnlyDictionary<string, string>>(), null, routes);

        return JsonSerializer.Serialize(new MeasureReport("engine", [], [group]), MeasureReportContext.Default.MeasureReport);
    }

    private RouteVersion Version(string version, CodeBuilder builder, params uint[] rvas)
    {
        return new RouteVersion(version, Write(version, builder), Globals(rvas));
    }

    private string Write(string version, CodeBuilder builder)
    {
        string path = Path.Combine(_folder.Root, $"{version}.bin");
        CodeBuilder.WritePe(path, builder.Build());

        return path;
    }
}

// Counts the parallel steps and the most workers of 1 step at once, from the fork and join events of Parallel.
file sealed class ParallelWorkers : EventListener
{
    private const string SourceName = "System.Threading.Tasks.Parallel.EventSource";

    private readonly Lock _lock = new();
    private int _mostAtOnce;
    private readonly Dictionary<int, int> _workers = [];

    public int MostAtOnce
    {
        get
        {
            lock (_lock)
            {
                return _mostAtOnce;
            }
        }
    }

    public int Steps
    {
        get
        {
            lock (_lock)
            {
                return _workers.Count;
            }
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == SourceName)
        {
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        int change = eventData.EventName switch
        {
            "ParallelFork" => 1,
            "ParallelJoin" => -1,
            _ => 0,
        };

        if (change == 0)
        {
            return;
        }

        int step = (int)eventData.Payload![eventData.PayloadNames!.IndexOf("ForkJoinContextID")]!;

        lock (_lock)
        {
            int atOnce = _workers.GetValueOrDefault(step) + change;
            _workers[step] = atOnce;
            _mostAtOnce = Math.Max(_mostAtOnce, atOnce);
        }
    }
}
