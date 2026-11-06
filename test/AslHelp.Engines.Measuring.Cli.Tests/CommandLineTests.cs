using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class CommandLineTests
{
    private const string First = "engine-1.0-0123456789abcdef";
    private const string Second = "engine-2.0-BETA-fedcba9876543210";
    private const string Third = "engine-3.0-00112233445566ff";

    // Players, then the start and done lines for each of 3 players, then 2 lines for each of the 2 groups and the report line.
    private static readonly (int EventId, int Scopes)[] _linesOfTwoGroups =
    [
        (8, 0),
        (1, 0),
        (2, 1),
        (1, 0),
        (2, 1),
        (1, 0),
        (2, 1),
        (9, 0),
        (10, 1),
        (15, 2),
        (10, 1),
        (15, 2),
        (7, 0),
    ];

    // The group line, then inside its scope the read failure of each of 2 versions, the global without a route and the duration.
    private static readonly (int EventId, int Scopes)[] _linesOfOneGroup =
    [
        (10, 1),
        (14, 2),
        (14, 2),
        (13, 2),
        (15, 2),
    ];

    private static readonly string[] _globalKeys = ["Global"];
    private static readonly string[] _groupKeys = ["versions", "offsets", "routes"];

    private static readonly string[] _oddVersions = ["{0}", "バー 1"];
    private static readonly string[] _reportKeys = ["engine", "players", "groups"];
    private static readonly string[] _sharedVersions = ["1.0", "3.0"];
    private static readonly string[] _soleVersion = ["2.0-BETA"];
    private static readonly string[] _sortedMembers = ["Early", "Late"];
    private static readonly string[] _sortedTypes = ["A", "B"];

    private StringWriter _error = null!;
    private TestFolder _folder = null!;
    private StringWriter _output = null!;
    private string _report = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _output = new StringWriter();
        _error = new StringWriter();
        _report = Path.Combine(_folder.Root, "report.json");
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _error.Dispose();
        _folder.Delete();
    }

    // The report has 1 row per player folder in version order, because a later step compares neighboring versions.
    [Test]
    public async Task RunAsync_TwoPlayers_WritesRowsInVersionOrder()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        int code = await RunAsync(new FakeMeasurer());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var players = report.RootElement.GetProperty("players");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(report.RootElement.GetProperty("engine").GetString(), Is.EqualTo("engine"));
            Assert.That(players.GetArrayLength(), Is.EqualTo(2));
            Assert.That(players[0].GetProperty("version").GetString(), Is.EqualTo("1.0"));
            Assert.That(players[1].GetProperty("version").GetString(), Is.EqualTo("2.0-BETA"));
            Assert.That(players[0].TryGetProperty("error", out _), Is.False);
        }
    }

    // A player that fails gets its error in its row and the others still run, because a full run takes minutes.
    [Test]
    public async Task RunAsync_OnePlayerFails_KeepsGoingAndExitsWith1()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        int code = await RunAsync(new FakeMeasurer(First));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var players = report.RootElement.GetProperty("players");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(players[0].GetProperty("error").GetString(), Does.Contain("did not start"));
            Assert.That(players[1].TryGetProperty("error", out _), Is.False);
            Assert.That(_error.ToString(), Does.Contain("1.0").And.Contain("did not start"));
        }
    }

    // A folder without a player of the engine fails, because an empty report reads like a run with nothing to find.
    [Test]
    public async Task RunAsync_NoPlayers_FailsWithTheFolder()
    {
        int code = await RunAsync(new FakeMeasurer());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Does.Contain(_folder.Root));
            Assert.That(File.Exists(_report), Is.False);
        }
    }

    // A players folder that does not exist fails with its path and no stack trace.
    [Test]
    public async Task RunAsync_MissingPlayersFolder_FailsWithTheFolder()
    {
        string players = Path.Combine(_folder.Root, "missing");

        int code = await CommandLine.RunAsync(["engine", players, "--output", _report], _output, _error, [new FakeMeasurer()], new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Does.Contain(players).And.Not.Contain("Unhandled"));
        }
    }

    // The report folder gets created before the 1st player starts, because a full run takes minutes and the rows live only in memory until the end.
    [Test]
    public async Task RunAsync_ReportInNewFolder_CreatesTheFolder()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        string report = Path.Combine(_folder.Root, "new", "report.json");

        int code = await CommandLine.RunAsync(["engine", _folder.Root, "--output", report], _output, _error, [new FakeMeasurer()], new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(File.Exists(report), Is.True);
        }
    }

    // A report that cannot be written fails with the reason and no stack trace.
    [Test]
    public async Task RunAsync_ReportPathIsAFolder_FailsWithoutAStackTrace()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(_report);

        int code = await RunAsync(new FakeMeasurer());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Does.Contain(_report).And.Not.Contain("Unhandled"));
        }
    }

    // A bad targets file fails before the 1st player starts, because a full run takes minutes.
    [Test]
    public async Task RunAsync_BadTargetsFile_FailsBeforeTheFirstPlayer()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        string targets = Path.Combine(_folder.Root, "targets.bad");
        FakeMeasurer measurer = new();

        int code = await CommandLine.RunAsync(["engine", _folder.Root, "--output", _report, "--targets", targets], _output, _error, [measurer], new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(measurer.Measured, Is.Zero);
            Assert.That(_error.ToString(), Does.Contain(targets));
            Assert.That(File.Exists(_report), Is.False);
        }
    }

    // A file given with --targets reaches the measurer, which then uses it instead of its built-in list.
    [Test]
    public async Task RunAsync_TargetsOption_PassesThePathToTheMeasurer()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        string targets = Path.Combine(_folder.Root, "targets.json");
        FakeMeasurer measurer = new();

        int code = await CommandLine.RunAsync(["engine", _folder.Root, "--output", _report, "--targets", targets], _output, _error, [measurer], new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(measurer.TargetsPath, Is.EqualTo(targets));
        }
    }

    // Without --targets the measurer gets null, which selects its built-in list.
    [Test]
    public async Task RunAsync_NoTargetsOption_PassesNullToTheMeasurer()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        FakeMeasurer measurer = new();
        measurer.LoadTargets("earlier.json");

        int code = await RunAsync(measurer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(measurer.TargetsPath, Is.Null);
        }
    }

    // The log has the player count once and the version and position of each player on its start line.
    [Test]
    public async Task RunAsync_TwoPlayers_LogsTheProgress()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        _ = await RunAsync(new FakeMeasurer());

        Assert.That(_output.ToString(), Does.Contain("players: 2").And.Contain("[1.0] (1/2)").And.Contain("[2.0-BETA] (2/2)"));
    }

    // A measured row has its 3 maps, with the offsets grouped by type and the numbers in hex.
    [Test]
    public async Task RunAsync_MeasuredPlayer_WritesGlobalsOffsetsAndUnmeasured()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        int code = await RunAsync(new FakeMeasurer());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var row = report.RootElement.GetProperty("players")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(row.TryGetProperty("error", out _), Is.False);
            Assert.That(row.GetProperty("globals").GetProperty("Global")[1].GetString(), Is.EqualTo("0x200"));
            Assert.That(row.GetProperty("offsets").GetProperty("Type").GetProperty("Member").GetString(), Is.EqualTo("0x28"));
            Assert.That(row.GetProperty("unmeasured").GetProperty("Type.Other").GetString(), Is.EqualTo("Not found."));
        }
    }

    // The report lists the types by name and the members of a type by offset, so the rows of 2 players compare line by line.
    [Test]
    public async Task RunAsync_MeasuredPlayer_SortsTypesAndMembers()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        _ = await RunAsync(new ScriptedMeasurer(static _ => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>>(),
            new Dictionary<string, int> { ["B.Late"] = 8, ["B.Early"] = 4, ["A.Only"] = 12 },
            new Dictionary<string, string>(),
            "file.bin")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var offsets = report.RootElement.GetProperty("players")[0].GetProperty("offsets");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(offsets.EnumerateObject().Select(static type => type.Name), Is.EqualTo(_sortedTypes));
            Assert.That(offsets.GetProperty("B").EnumerateObject().Select(static member => member.Name), Is.EqualTo(_sortedMembers));
        }
    }

    // A failed row has its error and none of the 3 maps, so an empty map always means a measured player.
    [Test]
    public async Task RunAsync_FailedPlayer_WritesNoMeasurement()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        _ = await RunAsync(new FakeMeasurer(First));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var row = report.RootElement.GetProperty("players")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.TryGetProperty("error", out _), Is.True);
            Assert.That(row.TryGetProperty("globals", out _), Is.False);
            Assert.That(row.TryGetProperty("offsets", out _), Is.False);
            Assert.That(row.TryGetProperty("unmeasured", out _), Is.False);
        }
    }

    // A measured row with nothing to report keeps its 3 empty maps, because a failed row has none of them.
    [Test]
    public async Task RunAsync_MeasurementWithEmptyMaps_WritesEmptyMaps()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        int code = await RunAsync(new ScriptedMeasurer(static _ => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>>(),
            new Dictionary<string, int>(),
            new Dictionary<string, string>(),
            "file.bin")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var row = report.RootElement.GetProperty("players")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(row.GetProperty("globals").EnumerateObject(), Is.Empty);
            Assert.That(row.GetProperty("offsets").EnumerateObject(), Is.Empty);
            Assert.That(row.GetProperty("unmeasured").EnumerateObject(), Is.Empty);
        }
    }

    // The largest RVA and a key and a reason outside ASCII come through the JSON unchanged.
    [Test]
    public async Task RunAsync_ExtremeValues_WritesThemUnchanged()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        int code = await RunAsync(new ScriptedMeasurer(static _ => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>> { ["\u00DCber"] = [uint.MaxValue] },
            new Dictionary<string, int> { ["a\"b.c.d"] = int.MaxValue },
            new Dictionary<string, string> { ["c"] = "\u30A8\u30E9\u30FC 'x' <y>" },
            "file.bin")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var row = report.RootElement.GetProperty("players")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(row.GetProperty("globals").GetProperty("\u00DCber")[0].GetString(), Is.EqualTo("0xFFFFFFFF"));
            Assert.That(row.GetProperty("offsets").GetProperty("a\"b").GetProperty("c.d").GetString(), Is.EqualTo("0x7FFFFFFF"));
            Assert.That(row.GetProperty("unmeasured").GetProperty("c").GetString(), Is.EqualTo("\u30A8\u30E9\u30FC 'x' <y>"));
        }
    }

    // A players folder and a report with spaces and letters outside ASCII work like any other.
    [Test]
    public async Task RunAsync_PathsWithSpacesAndUnicode_WritesTheReport()
    {
        string players = Path.Combine(_folder.Root, "pl\u00E4yers \u30D5\u30A9\u30EB\u30C0");
        _ = Directory.CreateDirectory(Path.Combine(players, First));
        string report = Path.Combine(_folder.Root, "r\u00E9port \u30D5\u30A9\u30EB\u30C0", "report \u00FC.json");

        int code = await CommandLine.RunAsync(["engine", players, "--output", report], _output, _error, [new FakeMeasurer()], new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(File.Exists(report), Is.True);
        }
    }

    // A longer report from an earlier run leaves nothing after the new report, because the file has to stay valid JSON.
    [Test]
    public async Task RunAsync_LongerReportExists_ReplacesAllOfIt()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        await File.WriteAllTextAsync(_report, new string('x', 1 << 16));

        _ = await RunAsync(new FakeMeasurer());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));

        Assert.That(report.RootElement.GetProperty("players").GetArrayLength(), Is.EqualTo(1));
    }

    // A run that stops at a bad list keeps the report of the earlier run.
    [Test]
    public async Task RunAsync_BadTargetsAndReportExists_KeepsTheReport()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        await File.WriteAllTextAsync(_report, "earlier");
        string targets = Path.Combine(_folder.Root, "targets.bad");

        _ = await CommandLine.RunAsync(["engine", _folder.Root, "--output", _report, "--targets", targets], _output, _error, [new FakeMeasurer()], new WriterLogger(_output, _error));

        Assert.That(await File.ReadAllTextAsync(_report), Is.EqualTo("earlier"));
    }

    // A bad list fails before the check for players, so the error is about the list.
    [Test]
    public async Task RunAsync_BadTargetsAndNoPlayers_FailsWithTheTargets()
    {
        string targets = Path.Combine(_folder.Root, "targets.bad");
        FakeMeasurer measurer = new();

        int code = await CommandLine.RunAsync(["engine", _folder.Root, "--output", _report, "--targets", targets], _output, _error, [measurer], new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Does.Contain(targets));
        }
    }

    // Every player failing still writes the report, with 1 error per row.
    [Test]
    public async Task RunAsync_EveryPlayerFails_WritesEveryRow()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        int code = await RunAsync(new FakeMeasurer(First, Second));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var players = report.RootElement.GetProperty("players");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(players[0].TryGetProperty("error", out _), Is.True);
            Assert.That(players[1].TryGetProperty("error", out _), Is.True);
        }
    }

    // A canceled run writes no report and does not turn the cancellation into a failed row.
    [Test]
    public async Task RunAsync_MeasurerCanceled_WritesNoReport()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));
        int measured = 0;

        int code = await RunAsync(new ScriptedMeasurer(_ =>
        {
            measured++;

            throw new OperationCanceledException();
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Not.Zero);
            Assert.That(measured, Is.EqualTo(1));
            Assert.That(File.Exists(_report), Is.False);
        }
    }

    // The groups follow the players in the JSON and their log lines come between the last player and the report line.
    [Test]
    public async Task RunAsync_SharedOffsets_WritesGroupsAndLogsThem()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Third));

        int code = await RunAsync(new ScriptedMeasurer(static directory => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>>(),
            new Dictionary<string, int> { ["Type.Member"] = directory.EndsWith(Second, StringComparison.Ordinal) ? 0x2C : 0x28 },
            new Dictionary<string, string>(),
            "file.bin")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var groups = report.RootElement.GetProperty("groups");

        Assert.That(groups.GetArrayLength(), Is.EqualTo(2));

        string log = _output.ToString();
        int lastPlayer = log.IndexOf("[3.0] (3/3)", StringComparison.Ordinal);
        int shared = log.IndexOf("[1.0, 3.0]", StringComparison.Ordinal);
        int sole = log.LastIndexOf("[2.0-BETA]", StringComparison.Ordinal);
        int written = log.IndexOf("report: '", StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(report.RootElement.EnumerateObject().Select(static property => property.Name), Is.EqualTo(_reportKeys));
            Assert.That(groups[0].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_sharedVersions));
            Assert.That(groups[0].GetProperty("offsets").GetProperty("Type").GetProperty("Member").GetString(), Is.EqualTo("0x28"));
            Assert.That(groups[1].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_soleVersion));
            Assert.That(groups[1].GetProperty("offsets").GetProperty("Type").GetProperty("Member").GetString(), Is.EqualTo("0x2C"));
            Assert.That(lastPlayer, Is.Not.Negative);
            Assert.That(shared, Is.GreaterThan(lastPlayer));
            Assert.That(sole, Is.GreaterThan(shared));
            Assert.That(written, Is.GreaterThan(sole));
        }
    }

    // A run where every player failed writes an empty list and does not log a group.
    [Test]
    public async Task RunAsync_EveryPlayerFails_WritesEmptyGroupsAndLogsNoGroup()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        int code = await RunAsync(new FakeMeasurer(First, Second));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.RootElement.GetProperty("groups").GetArrayLength(), Is.Zero);
            Assert.That(_output.ToString(), Does.Not.Contain("same offsets"));
        }
    }

    // A run where every map is empty exits with 0 and writes an empty list, because VersionGroups.Find skips empty maps.
    [Test]
    public async Task RunAsync_EveryOffsetMapEmpty_WritesEmptyGroupsAndExitsWith0()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        int code = await RunAsync(new ScriptedMeasurer(static _ => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>>(),
            new Dictionary<string, int>(),
            new Dictionary<string, string>(),
            "file.bin")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(report.RootElement.GetProperty("groups").GetArrayLength(), Is.Zero);
            Assert.That(_output.ToString(), Does.Not.Contain("same offsets"));
        }
    }

    // A failed player between 2 equal players is in the rows and not in the group, so the log line counts 2 versions.
    [Test]
    public async Task RunAsync_FailedPlayerBetweenEqualPlayers_CountsOnlyTheMeasured()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Third));

        int code = await RunAsync(new FakeMeasurer(Second));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var groups = report.RootElement.GetProperty("groups");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.RootElement.GetProperty("players").GetArrayLength(), Is.EqualTo(3));
            Assert.That(groups.GetArrayLength(), Is.EqualTo(1));
            Assert.That(groups[0].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_sharedVersions));
            Assert.That(_output.ToString(), Does.Contain("[1.0, 3.0]"));
        }
    }

    // Each group gets 1 line after the last player and before the report line, with no player scope open, so the console does not indent it.
    [Test]
    public async Task RunAsync_Groups_LogsOneLinePerGroupOutsideAnyScope()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Third));
        ScopeLogger logger = new();

        int code = await CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", _report],
            _output,
            _error,
            [
                new ScriptedMeasurer(static directory => new PlayerMeasurement(
                    new Dictionary<string, IReadOnlyList<uint>>(),
                    new Dictionary<string, int> { ["Type.Member"] = directory.EndsWith(Second, StringComparison.Ordinal) ? 0x2C : 0x28 },
                    new Dictionary<string, string>(),
                    "file.bin")),
            ],
            logger);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(logger.Lines, Is.EqualTo(_linesOfTwoGroups));
        }
    }

    // The offsets of a group have the form of the offsets of its 1st row, with the types and members in the same order and the same keys.
    [Test]
    public async Task RunAsync_Groups_WritesOffsetsInTheFormOfTheFirstRow()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        _ = await RunAsync(new ScriptedMeasurer(static _ => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>>(),
            new Dictionary<string, int> { ["B.Late"] = 8, ["B.Early"] = 4, ["A.Only"] = 12, ["Ü\".c.d"] = int.MaxValue },
            new Dictionary<string, string>(),
            "file.bin")));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var row = report.RootElement.GetProperty("players")[0].GetProperty("offsets");
        var group = report.RootElement.GetProperty("groups")[0].GetProperty("offsets");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(group.GetRawText(), Is.EqualTo(row.GetRawText()));
            Assert.That(group.GetProperty("Ü\"").GetProperty("c.d").GetString(), Is.EqualTo("0x7FFFFFFF"));
        }
    }

    // A version with letters outside ASCII and a placeholder in its folder comes through the JSON and the log line unchanged.
    [Test]
    public async Task RunAsync_OddVersions_WritesAndLogsThemUnchanged()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "engine-{0}-0123456789abcdef"));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "engine-バー 1-fedcba9876543210"));

        int code = await RunAsync(new FakeMeasurer());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var versions = report.RootElement.GetProperty("groups")[0].GetProperty("versions");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(versions.EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_oddVersions));
            Assert.That(_output.ToString(), Does.Contain("[{0}, バー 1]"));
        }
    }

    // A canceled run stops before the groups, so it logs none.
    [Test]
    public async Task RunAsync_CanceledAfterFirstPlayer_LogsNoGroup()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        int code = await RunAsync(new ScriptedMeasurer(static directory => directory.EndsWith(Second, StringComparison.Ordinal)
            ? throw new OperationCanceledException()
            : new PlayerMeasurement(
                new Dictionary<string, IReadOnlyList<uint>>(),
                new Dictionary<string, int> { ["Type.Member"] = 0x28 },
                new Dictionary<string, string>(),
                "file.bin")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Not.Zero);
            Assert.That(_output.ToString(), Does.Not.Contain("same offsets"));
            Assert.That(File.Exists(_report), Is.False);
        }
    }

    // A report that cannot be written still logs the groups first and never logs the report line.
    [Test]
    public async Task RunAsync_ReportPathIsAFolder_LogsTheGroupAndNoReportLine()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(_report);

        int code = await RunAsync(new FakeMeasurer());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_output.ToString(), Does.Contain("[1.0]").And.Not.Contain("report: '"));
        }
    }

    // Each group has its routes after its offsets, with 1 entry per global of the measurements.
    [Test]
    public async Task RunAsync_SharedOffsets_WritesRoutesUnderEachGroup()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Second));

        int code = await RunAsync(new FakeMeasurer());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var group = report.RootElement.GetProperty("groups")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(group.EnumerateObject().Select(static property => property.Name), Is.EqualTo(_groupKeys));
            Assert.That(group.GetProperty("routes").EnumerateObject().Select(static property => property.Name), Is.EqualTo(_globalKeys));
        }
    }

    // A real executable written by the measurer gets its route with the reached RVA in the report and in the log line.
    [Test]
    public async Task RunAsync_ExecutableWithALoad_WritesTheRouteAndLogsIt()
    {
        string first = Path.Combine(_folder.Root, First);
        _ = Directory.CreateDirectory(first);
        string executable = Path.Combine(first, "file.bin");
        CodeBuilder.WritePe(executable, new CodeBuilder(false).MovEcx(0x2000).Ret().Build());

        int code = await RunAsync(new ScriptedMeasurer(_ => new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [0x2000] },
            new Dictionary<string, int> { ["Type.Member"] = 0x28 },
            new Dictionary<string, string>(),
            executable)));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var global = report.RootElement.GetProperty("groups")[0].GetProperty("routes").GetProperty("Global");
        var route = global.GetProperty("routes")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(route.GetProperty("forward").GetString(), Is.EqualTo("8B 0D ????????"));
            Assert.That(route.GetProperty("offset").GetInt32(), Is.EqualTo(2));
            Assert.That(route.GetProperty("reached").GetProperty("1.0").GetString(), Is.EqualTo("0x2000"));
            Assert.That(global.TryGetProperty("uncovered", out _), Is.False);
            Assert.That(_output.ToString(), Does.Contain("Global: forward 8B 0D ????????, offset 2, versions 1/1"));
        }
    }

    // An executable that does not exist ends in the uncovered map with its path and the run still exits with 0.
    [Test]
    public async Task RunAsync_MeasurerWithoutExecutable_WritesUncoveredReason()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));

        int code = await RunAsync(new FakeMeasurer());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var global = report.RootElement.GetProperty("groups")[0].GetProperty("routes").GetProperty("Global");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(global.GetProperty("routes").GetArrayLength(), Is.Zero);
            Assert.That(global.GetProperty("uncovered").GetProperty("1.0").GetString(), Does.Contain("missing.exe"));
            Assert.That(_output.ToString(), Does.Contain("Global: no route, uncovered [1.0]"));
        }
    }

    // The route lines sit inside a scope of the group, so the console indents them under its line.
    [Test]
    public async Task RunAsync_Groups_LogsRoutesInsideTheGroupScope()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Third));
        ScopeLogger logger = new();

        int code = await CommandLine.RunAsync(["engine", _folder.Root, "--output", _report], _output, _error, [new FakeMeasurer()], logger);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(logger.Lines.SkipWhile(static line => line.EventId != 10).Take(_linesOfOneGroup.Length), Is.EqualTo(_linesOfOneGroup));
        }
    }

    private Task<int> RunAsync(IEngineMeasurer measurer)
    {
        return CommandLine.RunAsync(["engine", _folder.Root, "--output", _report], _output, _error, [measurer], new WriterLogger(_output, _error));
    }
}

// Records the event ID of each line with the number of scopes open at that line.
file sealed class ScopeLogger : ILogger
{
    private readonly LoggerExternalScopeProvider _scopes = new();

    public List<(int EventId, int Scopes)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _scopes.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        StrongBox<int> scopes = new();
        _scopes.ForEachScope(static (_, count) => count.Value++, scopes);

        Lines.Add((eventId.Id, scopes.Value));
    }
}

file sealed class ScriptedMeasurer(
    Func<string, PlayerMeasurement> measure) : IEngineMeasurer
{
    public string Name => "engine";

    public void LoadTargets(string? path)
    {
    }

    public Task<PlayerMeasurement> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        return Task.FromResult(measure(playerDirectory));
    }
}
