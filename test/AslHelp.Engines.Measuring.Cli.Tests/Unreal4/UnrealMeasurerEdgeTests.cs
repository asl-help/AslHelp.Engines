using AslHelp.Engines.Fixtures.Unreal4;
using AslHelp.Engines.Measuring.Cli.Tests.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Engines.Measuring.Cli.Unreal4;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealMeasurerEdgeTests
{
    private const uint DataRva = 0x2000;
    private const string Decorated = "?GetNames@FName@@CAAEAVNames@@XZ";
    private const string Hash = "0123456789abcdef";
    private const uint OtherDataRva = 0x2010;
    private const string TargetsFile = """
        {
          "GWorld": { "$type": "global" },
          "NameTable": { "$type": "functionStatic", "function": "FName::GetNames" },
          "Player": { "$type": "layout", "members": { "Member": {} } }
        }
        """;

    private const string X64 = "4.19.2-win-x64-shipping";
    private const string X86 = "4.19.2-win-x86-shipping";

    private static readonly string[] _firstGroup = ["4.8.3-win-x64-shipping", "4.9.2-win-x64-shipping", "4.10.4-win-x64-shipping"];
    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly string[] _member = ["Member"];

    private static readonly string[] _order =
    [
        "4.8.3-win-x86-shipping",
        "4.9.2-win-x86-shipping",
        "4.10.4-win-x86-shipping",
        "5.0.3-win-x86-shipping",
    ];

    private static readonly Guid _otherGuid = new("11111111-2222-3333-4444-555555555555");
    private static readonly string[] _secondGroup = ["4.11.2-win-x64-shipping"];
    private static readonly uint[] _sections = [CodeBuilder.CodeRva, DataRva];

    private TestFolder _folder = null!;

    private string Report => Path.Combine(_folder.Root, "report.json");

    private string Targets => Path.Combine(_folder.Root, "targets.example");

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        File.WriteAllText(Targets, TargetsFile);
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // Folders of the engine that aren't player ids get 1 log line each and leave nothing to measure, so the run fails like an empty folder.
    [Test]
    public async Task RunAsync_NoFolderIsAPlayerId_FailsAsNoPlayers()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, $"unreal4-garbage-{Hash}"));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, $"unreal4-{{0}}-{Hash}"));

        var (code, output, error) = await RunAsync(_folder.Root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(output, Does.Contain("skipped: garbage: ").And.Contain("skipped: {0}: "));
            Assert.That(error, Does.Contain("players: 0 unreal4"));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // A filter that drops every real player fails with the folder and doesn't write a report, because an empty report reads like a clean run.
    [Test]
    public async Task RunAsync_FilterDropsEveryPlayer_FailsAsNoPlayers()
    {
        _ = WritePlayer(X64);

        var (code, output, error) = await RunAsync(_folder.Root, "--targets", Targets, "--arch", "x86");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(output, Does.Not.Contain("skipped"));
            Assert.That(error, Does.Contain("players: 0 unreal4").And.Contain(_folder.Root));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // A value in another case or a missing value stops the parse, so a typo never measures the wrong players.
    [TestCase("--arch", "X64")]
    [TestCase("--arch", "x64 ")]
    [TestCase("--config", "Shipping")]
    [TestCase("--arch", null)]
    [TestCase("--config", null)]
    public async Task RunAsync_OptionInAnotherForm_FailsParse(string option, string? value)
    {
        _ = WritePlayer(X64);

        var (code, _, error) = await RunAsync(_folder.Root, value is null ? [option] : [option, value]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // An option written twice stops the parse, because 1 of the 2 values would drop out without an error.
    [Test]
    public async Task RunAsync_OptionTwice_FailsParse()
    {
        _ = WritePlayer(X64);

        var (code, _, _) = await RunAsync(_folder.Root, "--arch", "x64", "--arch", "x86");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // The order on the command line doesn't matter, so options can come before the folder.
    [Test]
    public async Task RunAsync_OptionsBeforeThePlayers_MeasuresThem()
    {
        _ = WritePlayer(X64);
        _ = WritePlayer(X86);
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(
            ["unreal4", "--arch", "x64", "--targets", Targets, "--output", Report, _folder.Root],
            output,
            error,
            [new UnrealMeasurer()],
            new WriterLogger(output, error));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(report.RootElement.GetProperty("players").GetArrayLength(), Is.EqualTo(1));
        }
    }

    // The options belong to 1 engine, so the other engine refuses them before it starts a player.
    [TestCase("--arch", "x64")]
    [TestCase("--config", "shipping")]
    public async Task RunAsync_OptionOnTheOtherEngine_FailsParse(string option, string value)
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(
            ["unreal3", _folder.Root, "--output", Report, option, value],
            output,
            error,
            [new UdkMeasurer(), new UnrealMeasurer()],
            new WriterLogger(output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain(option));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // The help of the other engine stays as it was, without the options of this engine.
    [Test]
    public async Task RunAsync_OtherEngineHelp_LeavesOutArchAndConfig()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(
            ["unreal3", "--help"],
            output,
            error,
            [new UdkMeasurer(), new UnrealMeasurer()],
            new WriterLogger(output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(output.ToString(), Does.Not.Contain("--arch").And.Not.Contain("--config"));
        }
    }

    // An engine without a measurer stops the parse without a report.
    [Test]
    public async Task RunAsync_UnknownEngine_FailsParse()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(
            ["unreal5", _folder.Root, "--output", Report],
            output,
            error,
            [new UdkMeasurer(), new UnrealMeasurer()],
            new WriterLogger(output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("unreal5"));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // The help of the console overload lists both engines, each with its own subcommand.
    [Test]
    public async Task RunAsync_Help_ListsBothEngines()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(["--help"], output, error, [new UdkMeasurer(), new UnrealMeasurer()]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("unreal3").And.Contain("unreal4"));
        }
    }

    // A real read of the PDB and the executable works in a folder with spaces, letters outside ASCII and a placeholder.
    [Test]
    public async Task RunAsync_OddPlayersFolder_MeasuresThePlayer()
    {
        string players = Path.Combine(_folder.Root, "pläyers {0} フォルダ");
        _ = WritePlayer(X64, players);

        var (code, output, error) = await RunAsync(players, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var group = report.RootElement.GetProperty("groups")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(group.GetProperty("routes").GetProperty("GWorld").GetProperty("routes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output, Does.Contain("pläyers {0} フォルダ"));
        }
    }

    // A player deep enough to pass the 260 characters of the old path limit reads like any other.
    [Test]
    public async Task RunAsync_LongPlayersFolder_MeasuresThePlayer()
    {
        string players = Path.Combine(_folder.Root, new string('a', 100), new string('b', 100), new string('c', 100));
        _ = WritePlayer(X64, players);

        var (code, _, error) = await RunAsync(players, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(report.RootElement.GetProperty("groups").GetArrayLength(), Is.EqualTo(1));
        }
    }

    // A row that fails keeps its error and stays out of the groups. The other build still gets its group and its routes.
    [Test]
    public async Task RunAsync_OnePlayerWithAStalePdb_KeepsTheOtherGroup()
    {
        _ = WritePlayer(X64);
        _ = WritePlayer(X86, executableGuid: _otherGuid);

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var players = report.RootElement.GetProperty("players");
        var groups = report.RootElement.GetProperty("groups");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(players[0].TryGetProperty("error", out _), Is.False);
            Assert.That(players[1].GetProperty("error").GetString(), Is.EqualTo(SR.FormatPdbIdentityMismatch(_guid, 1, _otherGuid, 1)));
            Assert.That(players[1].TryGetProperty("shapes", out _), Is.False);
            Assert.That(groups.GetArrayLength(), Is.EqualTo(1));
            Assert.That(groups[0].GetProperty("key").GetString(), Is.EqualTo("win-x64-shipping"));
            Assert.That(error, Does.Contain($"failed: {X86}: "));
        }
    }

    // Equal builds across minors share 1 group in natural order and 1 route. A shape change splits them even with equal offsets.
    [Test]
    public async Task RunAsync_EqualPlayersAcrossMinors_GroupByShapeInNaturalOrder()
    {
        _ = WritePlayer("4.11.2-win-x64-shipping");
        _ = WritePlayer("4.10.4-win-x64-shipping");
        _ = WritePlayer("4.9.2-win-x64-shipping");
        _ = WritePlayer("4.8.3-win-x64-shipping");

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var groups = report.RootElement.GetProperty("groups");
        var route = groups[0].GetProperty("routes").GetProperty("GWorld").GetProperty("routes")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(groups.GetArrayLength(), Is.EqualTo(2));
            Assert.That(groups[0].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_firstGroup));
            Assert.That(groups[1].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_secondGroup));
            Assert.That(groups[0].GetProperty("shapes").GetProperty("FUObjectArray").GetString(), Is.EqualTo("indirect"));
            Assert.That(groups[1].GetProperty("shapes").GetProperty("FUObjectArray").GetString(), Is.EqualTo("flat"));
            Assert.That(route.GetProperty("versions").GetArrayLength(), Is.EqualTo(3));
        }
    }

    // 2 builds that differ only in a size that no library reads share 1 group. Each row keeps its own size.
    [Test]
    public async Task RunAsync_PlayersWithOtherUnneededSizes_ShareOneGroup()
    {
        _ = WritePlayer("4.0.2-win-x64-shipping", size: 0x28);
        _ = WritePlayer("4.1.1-win-x64-shipping", size: 0x30);

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var players = report.RootElement.GetProperty("players");
        var groups = report.RootElement.GetProperty("groups");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(groups.GetArrayLength(), Is.EqualTo(1));
            Assert.That(groups[0].GetProperty("versions").GetArrayLength(), Is.EqualTo(2));
            Assert.That(
                groups[0].GetProperty("offsets").GetProperty("Player").EnumerateObject().Select(static member => member.Name),
                Is.EqualTo(_member));
            Assert.That(players[0].GetProperty("offsets").GetProperty("Player").GetProperty("sizeof").GetString(), Is.EqualTo("0x28"));
            Assert.That(players[1].GetProperty("offsets").GetProperty("Player").GetProperty("sizeof").GetString(), Is.EqualTo("0x30"));
        }
    }

    // A size that a library reads splits 2 builds like a member does.
    [Test]
    public async Task RunAsync_PlayersWithOtherNeededSizes_SplitIntoTwoGroups()
    {
        _ = WritePlayer("4.0.2-win-x64-shipping", size: 0x28);
        _ = WritePlayer("4.1.1-win-x64-shipping", size: 0x30);
        await File.WriteAllTextAsync(Targets, """{ "Player": { "$type": "layout", "sizeNeeded": true, "members": { "Member": {} } } }""");

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(report.RootElement.GetProperty("groups").GetArrayLength(), Is.EqualTo(2));
        }
    }

    // A member that no library reads leaves the group alone too, while a needed member with another offset splits it.
    // The global keeps the rows in a group, because a row without needed offsets and without globals joins none.
    [TestCase(false, 1)]
    [TestCase(true, 2)]
    public async Task RunAsync_PlayersWithAnotherMemberOffset_GroupByWhetherItIsNeeded(bool needed, int groupCount)
    {
        _ = WritePlayer("4.0.2-win-x64-shipping", memberOffset: 0x10);
        _ = WritePlayer("4.1.1-win-x64-shipping", memberOffset: 0x18);
        string flag = needed ? "true" : "false";
        await File.WriteAllTextAsync(
            Targets,
            $$"""{ "GWorld": { "$type": "global" }, "Player": { "$type": "layout", "members": { "Member": { "needed": {{flag}} } } } }""");

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(report.RootElement.GetProperty("groups").GetArrayLength(), Is.EqualTo(groupCount));
        }
    }

    // A static found through a function reaches the report and the route search like a global, with its log line in hex.
    [TestCase(X64)]
    [TestCase(X86)]
    public async Task RunAsync_FunctionStatic_WritesTheStaticAndItsRoute(string version)
    {
        var player = WritePlayer(version);
        string pdb = PlayerId.Parse(version).PdbPath(player.Directory);

        var (code, output, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var row = report.RootElement.GetProperty("players")[0];
        var route = report.RootElement.GetProperty("groups")[0].GetProperty("routes").GetProperty("NameTable").GetProperty("routes")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(row.GetProperty("globals").GetProperty("NameTable")[0].GetString(), Is.EqualTo("0x2000"));
            Assert.That(route.GetProperty("reached").GetProperty(version).GetString(), Is.EqualTo("0x2000"));
            Assert.That(output, Does.Contain("NameTable: 0x2000").And.Contain($"pdb: {pdb}").And.Contain("layouts: 1 types, 2 offsets"));
        }
    }

    // A list without types still searches the routes of its globals, with 1 group per build and empty offsets.
    [Test]
    public async Task RunAsync_TargetsWithOnlyGlobals_SearchesTheRoutes()
    {
        _ = WritePlayer(X64);
        _ = WritePlayer(X86);
        await File.WriteAllTextAsync(Targets, """{ "GWorld": { "$type": "global" } }""");

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", Targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var groups = report.RootElement.GetProperty("groups");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(groups.GetArrayLength(), Is.EqualTo(2));
            Assert.That(groups[0].GetProperty("offsets").EnumerateObject(), Is.Empty);
            Assert.That(groups[0].GetProperty("routes").GetProperty("GWorld").GetProperty("routes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(groups[1].GetProperty("routes").GetProperty("GWorld").GetProperty("routes").GetArrayLength(), Is.EqualTo(1));
        }
    }

    // A folder without players gets an empty list, which fails the run in the command line.
    [Test]
    public void SelectPlayers_NoPlayers_ReturnsAnEmptyList()
    {
        UnrealMeasurer measurer = new();
        RootCommand root = [.. measurer.Options];

        Assert.That(measurer.SelectPlayers([], root.Parse([]), NullLogger.Instance), Is.Empty);
    }

    // The filter keeps the version order of the folder, so the rows stay in natural order.
    [Test]
    public void SelectPlayers_Filter_KeepsTheOrder()
    {
        UnrealMeasurer measurer = new();
        RootCommand root = [.. measurer.Options];
        string[] versions = ["4.8.3-win-x64-shipping", .. _order, "5.0.3-win-x64-development"];

        var selected = measurer.SelectPlayers(
            [.. versions.Select(static version => new PlayerFolder("folder", version))],
            root.Parse(["--arch", "x86"]),
            NullLogger.Instance);

        Assert.That(selected.Select(static player => player.Version), Is.EqualTo(_order));
    }

    // A version before 4.0 gets dropped with a log line, because the shape table starts at 4.0.
    [Test]
    public void SelectPlayers_VersionBefore4_SkipsIt()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        UnrealMeasurer measurer = new();
        RootCommand root = [.. measurer.Options];

        PlayerFolder[] players = [new("folder", "3.27.2-win-x64-shipping")];

        var selected = measurer.SelectPlayers(players, root.Parse([]), new WriterLogger(output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected, Is.Empty);
            Assert.That(output.ToString(), Does.Contain("skipped: 3.27.2-win-x64-shipping: "));
        }
    }

    // A version before 4.0 fails as an id, because the error of an empty shape search doesn't show what to check.
    [Test]
    public async Task MeasureAsync_VersionBefore4_FailsWithTheId()
    {
        const string Old = "3.27.2-win-x64-shipping";
        var player = WritePlayer("4.2.1-win-x64-shipping");
        UnrealMeasurer measurer = new();
        measurer.LoadTargets(Targets).Unwrap();

        var result = await measurer.MeasureAsync(player with { Version = Old }, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatPlayerIdInvalid(Old)));
    }

    // An empty executable fails with its path.
    [Test]
    public async Task MeasureAsync_EmptyExecutable_FailsWithThePath()
    {
        var player = WritePlayer(X64);
        string executable = PlayerId.Parse(X64).ExecutablePath(player.Directory);
        await File.WriteAllTextAsync(executable, "");

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Does.Contain(executable));
    }

    // An empty PDB fails with its path.
    [Test]
    public async Task MeasureAsync_EmptyPdb_FailsWithThePath()
    {
        var player = WritePlayer(X64);
        string pdb = PlayerId.Parse(X64).PdbPath(player.Directory);
        await File.WriteAllTextAsync(pdb, "");

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Does.Contain(pdb));
    }

    // An executable cut at any byte fails the row with its path and doesn't throw in either read of the file.
    [Test]
    public async Task MeasureAsync_ExecutableCutAtAnyByte_FailsWithThePath()
    {
        var player = WritePlayer(X64);
        string executable = PlayerId.Parse(X64).ExecutablePath(player.Directory);
        byte[] bytes = await File.ReadAllBytesAsync(executable);
        var measurer = Measurer();

        for (int length = 0; length < bytes.Length; length++)
        {
            await File.WriteAllBytesAsync(executable, bytes.AsMemory(0, length));

            var result = await measurer.MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

            Assert.That(result.UnwrapErr().Message, Does.Contain(executable), $"length: {length}");
        }
    }

    // A file without a debug entry can't prove which PDB it belongs to, so the row fails with its path.
    [Test]
    public async Task MeasureAsync_ExecutableWithoutDebugEntry_FailsWithThePath()
    {
        var player = WritePlayer(X64);
        string executable = PlayerId.Parse(X64).ExecutablePath(player.Directory);
        CodeBuilder.WritePe(executable, new CodeBuilder(true).Ret().Build());

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNoCodeView(executable)));
    }

    // An executable that another program has open fails the row with its path and doesn't throw.
    [Test]
    public async Task MeasureAsync_ExecutableOpenInAnotherProgram_FailsWithThePath()
    {
        var player = WritePlayer(X64);
        string executable = PlayerId.Parse(X64).ExecutablePath(player.Directory);
        using FileStream locked = new(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Does.Contain(executable));
    }

    // A PDB that another program has open fails the row with its path and doesn't throw.
    [Test]
    public async Task MeasureAsync_PdbOpenInAnotherProgram_FailsWithThePath()
    {
        var player = WritePlayer(X64);
        string pdb = PlayerId.Parse(X64).PdbPath(player.Directory);
        using FileStream locked = new(pdb, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Does.Contain(pdb));
    }

    // A folder at the path of the executable isn't an executable, so it fails as a missing file.
    [Test]
    public async Task MeasureAsync_FolderAtTheExecutablePath_FailsAsMissing()
    {
        var player = WritePlayer(X64);
        string executable = PlayerId.Parse(X64).ExecutablePath(player.Directory);
        File.Delete(executable);
        _ = Directory.CreateDirectory(executable);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatExecutableNotFound(executable)));
    }

    // A folder at the path of the PDB isn't a PDB, so it fails as a missing file.
    [Test]
    public async Task MeasureAsync_FolderAtThePdbPath_FailsAsMissing()
    {
        var player = WritePlayer(X64);
        string pdb = PlayerId.Parse(X64).PdbPath(player.Directory);
        File.Delete(pdb);
        _ = Directory.CreateDirectory(pdb);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatPdbNotFound(pdb)));
    }

    // The same GUID with another age is another link, so the row fails with both ages.
    [Test]
    public async Task MeasureAsync_SameGuidOtherAge_FailsWithBothAges()
    {
        var player = WritePlayer(X64, executableAge: 2);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(SR.FormatPdbIdentityMismatch(_guid, 1, _guid, 2)));
    }

    // A text that isn't a player id doesn't have a group, so it can't join one by accident.
    [Test]
    public void GroupOf_NotAPlayerId_ReturnsNull()
    {
        Assert.That(new UnrealMeasurer().GroupOf("garbage"), Is.Null);
    }

    private static UnrealMeasurer Measurer()
    {
        UnrealMeasurer measurer = new();
        measurer.LoadTargets(null).Unwrap();

        return measurer;
    }

    private PlayerFolder WritePlayer(
        string version,
        string? players = null,
        Guid? executableGuid = null,
        int executableAge = 1,
        ulong size = 0x28,
        ulong memberOffset = 0x10)
    {
        string directory = Path.Combine(players ?? _folder.Root, $"unreal4-{version}-{Hash}");
        var id = PlayerId.Parse(version);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(id.ExecutablePath(directory))!);

        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic("?GWorld@@3VUWorldProxy@@A", 2, OtherDataRva - DataRva, isFunction: false);
        pdb.AddPublic(Decorated, 1, 0, isFunction: true);
        pdb.AddStruct("Player", size, pdb.AddFieldList(FakePdb.Member("Member", memberOffset)));
        pdb.Write(id.PdbPath(directory));

        // Give each function its own bytes and 6 literals, because a window doesn't cross a return and the 2 x64 loads are the same.
        var code = id.Player.Arch == Architecture.X64
            ? new CodeBuilder(true).Fillers(3).MovRax(DataRva).Fillers(1).Ret().Fillers(3).MovRax(OtherDataRva).Ret()
            : new CodeBuilder(false).Fillers(3).MovEcx(DataRva).Ret().Fillers(3).MovEdx(OtherDataRva).Ret();

        FakeExecutable.Write(id.ExecutablePath(directory), code, executableGuid ?? _guid, executableAge);

        return new PlayerFolder(directory, version);
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(string players, params string[] options)
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(
            ["unreal4", players, "--output", Report, .. options],
            output,
            error,
            [new UnrealMeasurer()],
            new WriterLogger(output, error));

        return (code, output.ToString(), error.ToString());
    }
}
