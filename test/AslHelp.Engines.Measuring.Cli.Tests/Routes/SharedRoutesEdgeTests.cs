using AslHelp.Engines.Measuring.Cli.Routes;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

public sealed class SharedRoutesEdgeTests
{
    private const string Global = "Global";
    private const uint GlobalA = 0x2000;
    private const uint GlobalB = 0x3000;
    private const uint Other = 0x4000;

    private static readonly string[] _a = ["a"];
    private static readonly string[] _aAndB = ["a", "b"];
    private static readonly string[] _aAndCAndD = ["a", "c", "d"];
    private static readonly string[] _b = ["b"];
    private static readonly string[] _c = ["c"];
    private static readonly string[] _d = ["d"];
    private static readonly string[] _firstSeenGlobals = ["X", "Y", "Z"];
    private static readonly string[] _inputOrder = ["c", "a", "b"];
    private static readonly string[] _oddVersions = ["{0}", "バー 1"];
    private static readonly string[] _threeVersions = ["a", "b", "c"];

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

    // A group without versions doesn't have globals to search.
    [Test]
    public void Find_NoVersions_ReturnsNothing()
    {
        Assert.That(Find(), Is.Empty);
    }

    // A version without globals is never read, so its missing file logs nothing.
    [Test]
    public void Find_VersionWithoutGlobals_DoesNotReadItsFile()
    {
        using StringWriter output = new();

        var found = SharedRoutes.Find(
            [
                new RouteVersion("a", Path.Combine(_folder.Root, "missing.bin"), new Dictionary<string, IReadOnlyList<uint>>()),
                Version("b", new CodeBuilder(false).MovEcx(GlobalB), GlobalB),
            ],
            new WriterLogger(output, TextWriter.Null),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_b));
            Assert.That(found[Global].Uncovered!["a"], Is.EqualTo(SR.UncoveredNotFound));
        }
    }

    // A global with an empty RVA list doesn't have sites and its version goes to the uncovered map.
    [Test]
    public void Find_GlobalWithEmptyRvaList_IsUncovered()
    {
        var found = Find(Version("a", new CodeBuilder(false).MovEcx(GlobalA)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes, Is.Empty);
            Assert.That(found[Global].Uncovered!["a"], Is.EqualTo(SR.UncoveredNoCandidate));
        }
    }

    // A file cut inside its section fails only its version, at a size that PEReader reads and at a size that PEReader maps.
    [TestCase(0x40)]
    [TestCase(0x10000)]
    public void Find_FileCutInsideItsSection_FailsOnlyThatVersion(int sectionSize)
    {
        var whole = Version("b", new CodeBuilder(false).Nops(sectionSize), GlobalB);
        File.WriteAllBytes(whole.ExecutablePath, File.ReadAllBytes(whole.ExecutablePath)[..^(sectionSize / 2)]);

        AssertOnlyBFails(whole);
    }

    // A section RVA that does not fit a signed value is out of range for PEReader and fails only its version.
    [Test]
    public void Find_SectionRvaAbove2Gb_FailsOnlyThatVersion()
    {
        AssertOnlyBFails(Version("b", new CodeBuilder(false, rva: 0x8000_0000).MovEcx(GlobalB), GlobalB));
    }

    // A path that the file APIs refuse fails only its version.
    [TestCase("")]
    [TestCase("file\0.bin")]
    public void Find_InvalidPath_FailsOnlyThatVersion(string path)
    {
        AssertOnlyBFails(new RouteVersion("b", path, Globals(GlobalB)));
    }

    // A folder in place of the file fails only its version.
    [Test]
    public void Find_PathOfAFolder_FailsOnlyThatVersion()
    {
        AssertOnlyBFails(new RouteVersion("b", _folder.Root, Globals(GlobalB)));
    }

    // A file that another process keeps open without sharing fails only its version.
    [Test]
    public void Find_FileOpenWithoutSharing_FailsOnlyThatVersion()
    {
        var version = Version("b", new CodeBuilder(false).MovEcx(GlobalB), GlobalB);
        using FileStream locked = new(version.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.None);

        AssertOnlyBFails(version);
    }

    // The read failure of a version is the reason under each global, with 1 log line for the version.
    [Test]
    public void Find_UnreadableVersionWithTwoGlobals_LogsOnceAndListsItUnderBoth()
    {
        string missing = Path.Combine(_folder.Root, "missing.bin");
        using StringWriter output = new();

        var found = SharedRoutes.Find(
            [new RouteVersion("a", missing, new Dictionary<string, IReadOnlyList<uint>> { ["X"] = [GlobalA], ["Y"] = [GlobalB] })],
            new WriterLogger(output, TextWriter.Null),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(output.ToString().Split("executable not read"), Has.Length.EqualTo(2));
            Assert.That(found["X"].Uncovered!["a"], Does.Contain(missing));
            Assert.That(found["Y"].Uncovered!["a"], Does.Contain(missing));
        }
    }

    // A candidate with a site in only 1 version can still cover the most versions, because linear decoding misses the loads of c and d.
    [Test]
    public void Find_CandidateWithFewSitesCoversMost_WinsTheRanking()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret().MovEax(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB).Ret(), GlobalB),
            Version("c", new CodeBuilder(false).Raw(0xB8).MovEax(GlobalA).Ret(), GlobalA),
            Version("d", new CodeBuilder(false).Raw(0xB8).MovEax(GlobalB).Ret(), GlobalB));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[0].Forward, Is.EqualTo("A1 ????????"));
            Assert.That(routes[0].Versions, Is.EqualTo(_aAndCAndD));
            Assert.That(routes[1].Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(routes[1].Versions, Is.EqualTo(_b));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // A version without a site can share a candidate from a site that lost the 1st round, in a version that is already covered.
    [Test]
    public void Find_VersionWithoutSite_UsesASiteOfACoveredVersion()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret().MovEax(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB).Ret(), GlobalB),
            Version("c", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("d", new CodeBuilder(false).Raw(0xB8).MovEax(GlobalB).Ret(), GlobalB));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[0].Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(routes[0].Versions, Is.EqualTo(_threeVersions));
            Assert.That(routes[1].Forward, Is.EqualTo("A1 ????????"));
            Assert.That(routes[1].Versions, Is.EqualTo(_d));
            Assert.That(routes[1].Reached.Keys, Is.EqualTo(_d));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // Growth stops once a route covers the versions with a site, so c gets its own route even though 1 longer route covers all 3.
    [Test]
    public void Find_BestCoversEveryVersionWithASite_LeavesTheOtherVersionToTheNextRoute()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA).Ret(), GlobalA),
            Version("b", new CodeBuilder(false).MovEcx(GlobalB).Ret(), GlobalB),
            Version("c", new CodeBuilder(false).MovEcx(Other).Nops(1).Raw(0xB8).MovEcx(GlobalA).Ret(), GlobalA));

        var routes = found[Global].Routes;

        Assert.That(routes, Has.Count.EqualTo(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(routes[0].Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(routes[0].Versions, Is.EqualTo(_aAndB));
            Assert.That(routes[1].Forward, Is.EqualTo("8B 0D ???????? C3"));
            Assert.That(routes[1].Versions, Is.EqualTo(_c));
            Assert.That(found[Global].Uncovered, Is.Null);
        }
    }

    // The search tries only the first candidates of each instruction count, so b stays uncovered when the 1 candidate for its load sorts after them.
    [TestCase(RouteCandidates.MostCandidates - 1, false)]
    [TestCase(RouteCandidates.MostCandidates, true)]
    public void Find_CandidateForTheLoadAtTheLimit_TriesOnlyTheFirst(int last, bool uncovered)
    {
        CodeBuilder builder = new(false);

        for (byte value = 0; value <= RouteCandidates.MostCandidates; value++)
        {
            _ = builder.Raw(0xC7, 0x05).Absolute(GlobalA).Raw(value, 0x00, 0x00, 0x00);
        }

        var found = Find(
            Version("a", builder, GlobalA),
            Version("b", new CodeBuilder(false).Raw(0xB8, 0xC7, 0x05).Absolute(GlobalB).Raw((byte)last, 0x00, 0x00, 0x00), GlobalB));

        Assert.That(found[Global].Uncovered?.ContainsKey("b") ?? false, Is.EqualTo(uncovered));
    }

    // 1 instruction of 10 bytes beats 2 instructions of 7 bytes, because the instruction count ranks before the byte count.
    [Test]
    public void Find_FewerInstructionsWithMoreBytes_WinsTheRanking()
    {
        var found = Find(Version(
            "a",
            new CodeBuilder(false).MovEcx(Other).Ret().MovEcx(GlobalA).Nops(1).Raw(0xC7, 0x05).Absolute(GlobalA).Raw(0x01, 0x00, 0x00, 0x00),
            GlobalA));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Forward, Is.EqualTo("C7 05 ???????? 01 00 00 00"));
            Assert.That(route.Offset, Is.EqualTo(2));
        }
    }

    // 2 globals in 1 file search apart, each with its own route and RVA.
    [Test]
    public void Find_TwoGlobalsInOneFile_GetTheirOwnRoutes()
    {
        string path = Write("a", new CodeBuilder(false).MovEcx(GlobalA).Nops(1).MovEdx(GlobalB));

        var found = Find(new RouteVersion("a", path, new Dictionary<string, IReadOnlyList<uint>> { ["X"] = [GlobalA], ["Y"] = [GlobalB] }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found["X"].Routes.Single().Forward, Is.EqualTo("8B 0D ????????"));
            Assert.That(found["X"].Routes.Single().Reached["a"], Is.EqualTo("0x2000"));
            Assert.That(found["Y"].Routes.Single().Forward, Is.EqualTo("8B 15 ????????"));
            Assert.That(found["Y"].Routes.Single().Reached["a"], Is.EqualTo("0x3000"));
        }
    }

    // The search result of a candidate is kept per file, so it must still pass the check against the list of the next global.
    [Test]
    public void Find_SameCandidateForTwoGlobals_ChecksEachList()
    {
        string first = Write("a", new CodeBuilder(false).MovEcx(GlobalA));
        string second = Write("b", new CodeBuilder(false).MovEcx(GlobalB));

        var found = Find(
            new RouteVersion("a", first, new Dictionary<string, IReadOnlyList<uint>> { ["X"] = [GlobalA], ["Y"] = [GlobalA] }),
            new RouteVersion("b", second, new Dictionary<string, IReadOnlyList<uint>> { ["X"] = [GlobalB], ["Y"] = [Other] }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found["X"].Routes.Single().Versions, Is.EqualTo(_aAndB));
            Assert.That(found["X"].Uncovered, Is.Null);
            Assert.That(found["Y"].Uncovered!.Keys, Is.EqualTo(_b));
        }
    }

    // An x86 and an x64 executable can share the bytes of a load and each reads the operand its own way.
    [Test]
    public void Find_X86AndX64Versions_ShareARoute()
    {
        var found = Find(
            Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("b", new CodeBuilder(true, 0x0001_4000_0000).Raw(0x8B, 0x0D).Relative(GlobalB), GlobalB));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Versions, Is.EqualTo(_aAndB));
            Assert.That(route.Reached["a"], Is.EqualTo("0x2000"));
            Assert.That(route.Reached["b"], Is.EqualTo("0x3000"));
        }
    }

    // The globals keep the order they first appear in, across versions with different lists.
    [Test]
    public void Find_VersionsWithDifferentGlobals_KeepsFirstSeenOrder()
    {
        string path = Write("a", new CodeBuilder(false).Nops(1));
        Dictionary<string, IReadOnlyList<uint>> first = new() { ["X"] = [GlobalA], ["Y"] = [GlobalA] };
        Dictionary<string, IReadOnlyList<uint>> second = new() { ["Z"] = [GlobalA], ["X"] = [GlobalA] };

        var found = Find(new RouteVersion("a", path, first), new RouteVersion("b", path, second));

        Assert.That(found.Keys, Is.EqualTo(_firstSeenGlobals));
    }

    // The uncovered map keeps the order of the versions of the group, without sorting them.
    [Test]
    public void Find_SeveralUncovered_KeepsVersionOrder()
    {
        var found = Find(
            Version("c", new CodeBuilder(false).Nops(1), GlobalA),
            Version("a", new CodeBuilder(false).Nops(1), GlobalA),
            Version("b", new CodeBuilder(false).Nops(1), GlobalA));

        Assert.That(found[Global].Uncovered!.Keys, Is.EqualTo(_inputOrder));
    }

    // Versions are map keys and message arguments, so braces and non-ASCII characters stay as they are.
    [Test]
    public void Find_OddVersions_KeepsThemAsKeys()
    {
        var found = Find(
            Version("{0}", new CodeBuilder(false).MovEcx(GlobalA), GlobalA),
            Version("バー 1", new CodeBuilder(false).MovEcx(GlobalB), GlobalB));

        var route = found[Global].Routes.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(route.Versions, Is.EqualTo(_oddVersions));
            Assert.That(route.Reached["バー 1"], Is.EqualTo("0x3000"));
        }
    }

    // A canceled search throws before it reads a file, even when no version would get as far as a candidate.
    [Test]
    public void Find_CanceledWithUnreadableFile_Throws()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        RouteVersion version = new("a", Path.Combine(_folder.Root, "missing.bin"), Globals(GlobalA));

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

    // Checks that version a keeps its route next to the version b that fails to read.
    private void AssertOnlyBFails(RouteVersion failing)
    {
        var found = Find(Version("a", new CodeBuilder(false).MovEcx(GlobalA), GlobalA), failing);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found[Global].Routes.Single().Versions, Is.EqualTo(_a));
            Assert.That(found[Global].Uncovered!["b"], Is.Not.Empty.And.Not.EqualTo(SR.UncoveredNoCandidate));
        }
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
