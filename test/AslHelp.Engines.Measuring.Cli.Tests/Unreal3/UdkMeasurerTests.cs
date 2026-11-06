using AslHelp.Engines.Measuring.Cli.Unreal3;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkMeasurerTests
{
    private const string PlayerVariable = "FIXTURES_UDK_PLAYER";
    private const string TargetsFile = """
        {
          "Names": { "$type": "nameArray", "firstNames": ["A", "B"] },
          "Objects": { "$type": "objectArray" }
        }
        """;

    private static readonly string[] _globals = ["GNames", "GObjObjects", "GEngine", "GWorld"];

    private TestFolder _folder = null!;

    private string Report => Path.Combine(_folder.Root, "report.json");

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

    // No --targets measures the 4 built-in globals.
    [Test]
    public void LoadTargets_Null_UsesTheEmbeddedList()
    {
        UdkMeasurer measurer = new();

        measurer.LoadTargets(null);

        Assert.That(measurer.Targets, Has.Count.EqualTo(4));
    }

    // A file swaps the list with no rebuild.
    [Test]
    public void LoadTargets_File_ReplacesTheEmbeddedList()
    {
        string path = Path.Combine(_folder.Root, "targets.json");
        File.WriteAllText(
            path,
            """
            {
              "Names": { "$type": "nameArray", "firstNames": ["A", "B"] },
              "Objects": { "$type": "objectArray" }
            }
            """);
        UdkMeasurer measurer = new();

        measurer.LoadTargets(path);

        Assert.That(measurer.Targets, Does.ContainKey("Names"));
    }

    // A file in a folder with spaces and letters outside ASCII loads like any other.
    [Test]
    public void LoadTargets_PathWithSpacesAndUnicode_Loads()
    {
        string folder = Path.Combine(_folder.Root, "t\u00E4rgets \u30D5\u30A9\u30EB\u30C0");
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "targets.json");
        File.WriteAllText(path, TargetsFile);
        UdkMeasurer measurer = new();

        measurer.LoadTargets(path);

        Assert.That(measurer.Targets, Has.Count.EqualTo(2));
    }

    // A bad list stops the run before any player starts, so no report gets written.
    [TestCase("")]
    [TestCase("{}")]
    [TestCase("not JSON")]
    [TestCase("""{ "Objects": { "$type": "objectArray" } }""")]
    public async Task RunAsync_BadTargetsFile_WritesNoReport(string json)
    {
        string targets = Path.Combine(_folder.Root, "targets.example");
        await File.WriteAllTextAsync(targets, json);

        var (code, error) = await RunAsync(targets);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error, Does.Contain(targets).And.Not.Contain("Unhandled"));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // A folder or a missing file as the list fails with no stack trace and no report.
    [Test]
    public async Task RunAsync_TargetsPathNotAFile_WritesNoReport()
    {
        string missing = Path.Combine(_folder.Root, "missing.example");

        var (folderCode, folderError) = await RunAsync(_folder.Root);
        var (missingCode, missingError) = await RunAsync(missing);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(folderCode, Is.EqualTo(1));
            Assert.That(folderError, Does.Not.Contain("Unhandled"));
            Assert.That(missingCode, Is.EqualTo(1));
            Assert.That(missingError, Does.Contain(missing).And.Not.Contain("Unhandled"));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // A real player returns all 4 globals with at least 1 RVA each.
    [Test]
    public async Task MeasureAsync_RealPlayer_ReturnsEveryGlobal()
    {
        string? player = Environment.GetEnvironmentVariable(PlayerVariable);
        Assume.That(player is not null && Directory.Exists(player), $"Set {PlayerVariable} to a built player to run this test.");

        UdkMeasurer measurer = new();
        measurer.LoadTargets(null);

        var measurement = await measurer.MeasureAsync(player!, NullLogger.Instance, CancellationToken.None);

        Assert.That(measurement.Globals.Keys, Is.EquivalentTo(_globals));
    }

    private async Task<(int Code, string Error)> RunAsync(string targets)
    {
        // Add a folder that reads as a player, so a run that gets past the list fails its row and writes a report.
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "unreal3-1.0-0123456789abcdef"));

        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(["unreal3", _folder.Root, "--output", Report, "--targets", targets], output, error, [new UdkMeasurer()], new WriterLogger(output, error));

        return (code, error.ToString());
    }
}
