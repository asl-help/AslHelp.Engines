using AslHelp.Engines.Measuring.Cli.Routes;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class SharedRoutesTests
{
    private const string Global = "Global";
    private const uint GlobalA = 0x2000;
    private const uint GlobalB = 0x3000;
    private const uint Other = 0x4000;

    private static readonly string[] _a = ["a"];
    private static readonly string[] _aAndB = ["a", "b"];
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

    // A match that reaches an RVA outside the list of the version leaves the version uncovered.
    [Test]
    public void Find_RvaReachedNotInVersionList_IsNotCovered()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(Other), GlobalB));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_a));
            Assert.That(found[Global].Uncovered!.Keys, Is.EqualTo(_b));
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

        Assert.That(() => SharedRoutes.Find([version], NullLogger.Instance, source.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    private static IReadOnlyDictionary<string, GlobalRoutes> Find(params RouteVersion[] versions)
    {
        return SharedRoutes.Find(versions, NullLogger.Instance, CancellationToken.None);
    }

    private static Dictionary<string, IReadOnlyList<uint>> Globals(params uint[] rvas)
    {
        return new Dictionary<string, IReadOnlyList<uint>> { [Global] = rvas };
    }

    private static string Json(IReadOnlyDictionary<string, GlobalRoutes> routes)
    {
        VersionGroup group = new(["a"], new Dictionary<string, IReadOnlyDictionary<string, string>>(), routes);

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
