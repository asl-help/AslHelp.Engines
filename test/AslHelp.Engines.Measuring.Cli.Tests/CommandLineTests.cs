using NUnit.Framework;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class CommandLineTests
{
    private const string First = "engine-1.0-0123456789abcdef";
    private const string Second = "engine-2.0-BETA-fedcba9876543210";

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

        int code = await CommandLine.RunAsync(["engine", players, "--output", _report], _output, _error, [new FakeMeasurer()]);

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

        int code = await CommandLine.RunAsync(["engine", _folder.Root, "--output", report], _output, _error, [new FakeMeasurer()]);

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

    private Task<int> RunAsync(IEngineMeasurer measurer)
    {
        return CommandLine.RunAsync(["engine", _folder.Root, "--output", _report], _output, _error, [measurer]);
    }
}
