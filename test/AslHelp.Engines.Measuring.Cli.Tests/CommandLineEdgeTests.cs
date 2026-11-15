using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class CommandLineEdgeTests
{
    private const string First = "engine-1.0-0123456789abcdef";
    private const string FirstBuild = "engine-1.0-0000000000000001";

    // The form of a report before group keys and shapes existed.
    private const string ReportWithoutKeysOrShapes = """
        {
          "engine": "engine",
          "players": [
            {
              "version": "1.0",
              "globals": {},
              "offsets": {
                "Type": {
                  "Member": "0x28"
                }
              },
              "unmeasured": {
                "Type.Other": "Not found."
              }
            },
            {
              "version": "2.0",
              "error": "The player did not start."
            }
          ],
          "groups": [
            {
              "versions": [
                "1.0"
              ],
              "offsets": {
                "Type": {
                  "Member": "0x28"
                }
              },
              "routes": {}
            }
          ]
        }
        """;

    private const string SecondBuild = "engine-1.0-0000000000000002";

    private static readonly string[] _soleVersion = ["1.0"];

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

    // 2 builds of 1 version would share their rows and the route search would read 1 build for both, so the run stops with both folders.
    [Test]
    public async Task RunAsync_TwoBuildsOfOneVersion_FailsBeforeAnyPlayer()
    {
        string first = Path.Combine(_folder.Root, FirstBuild);
        string second = Path.Combine(_folder.Root, SecondBuild);
        _ = Directory.CreateDirectory(first);
        _ = Directory.CreateDirectory(second);
        FakeMeasurer measurer = new();

        int code = await RunAsync(measurer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(measurer.Measured, Is.Zero);
            Assert.That(_error.ToString(), Does.Contain("1.0").And.Contain(first).And.Contain(second));
            Assert.That(File.Exists(_report), Is.False);
        }
    }

    // The check comes after the measurer picks its players, so a filter that keeps 1 of the 2 builds still runs.
    [Test]
    public async Task RunAsync_MeasurerKeepsOneBuild_Measures()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, FirstBuild));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, SecondBuild));

        int code = await RunAsync(new ScriptedMeasurer(static _ => Measurement()) { Select = static players => [players[0]] });

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var versions = report.RootElement.GetProperty("groups")[0].GetProperty("versions");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(versions.EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_soleVersion));
        }
    }

    // A measurer without group keys or shapes writes the report byte for byte as before, because older reports compare with earlier runs.
    [Test]
    public async Task RunAsync_MeasurerWithoutKeysOrShapes_WritesTheOldReport()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "engine-2.0-0123456789abcdef"));

        _ = await RunAsync(new ScriptedMeasurer(static directory => directory.Contains("engine-2.0", StringComparison.Ordinal)
            ? new TextError("The player did not start.")
            : Measurement()));

        string written = await File.ReadAllTextAsync(_report);

        Assert.That(written.ReplaceLineEndings("\n"), Is.EqualTo(ReportWithoutKeysOrShapes.ReplaceLineEndings("\n")));
    }

    // A report that can't be replaced fails after the groups and keeps the earlier file.
    [Test]
    public async Task RunAsync_ReadOnlyReport_FailsAndKeepsTheFile()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        await File.WriteAllTextAsync(_report, "earlier");
        File.SetAttributes(_report, FileAttributes.ReadOnly);

        try
        {
            int code = await RunAsync(new ScriptedMeasurer(static _ => Measurement()));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(code, Is.EqualTo(1));
                Assert.That(_error.ToString(), Does.Contain(_report));
                Assert.That(await File.ReadAllTextAsync(_report), Is.EqualTo("earlier"));
            }
        }
        finally
        {
            File.SetAttributes(_report, FileAttributes.Normal);
        }
    }

    // A file in the way of the report folder stops the run before the 1st player, because the rows live only in memory until the end.
    [Test]
    public async Task RunAsync_FileInTheWayOfTheReportFolder_FailsBeforeAnyPlayer()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        string blocked = Path.Combine(_folder.Root, "blocked");
        await File.WriteAllTextAsync(blocked, "");
        FakeMeasurer measurer = new();

        int code = await CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", Path.Combine(blocked, "report.json")],
            _output,
            _error,
            [measurer],
            new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(measurer.Measured, Is.Zero);
            Assert.That(_error.ToString(), Does.Not.Contain("Unhandled"));
        }
    }

    // A run that gets canceled keeps the report of the earlier run, because the write comes after the last player.
    [Test]
    public async Task RunAsync_CanceledWithAnEarlierReport_KeepsTheFile()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        await File.WriteAllTextAsync(_report, "earlier");
        using CancellationTokenSource run = new();

        int code = await CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", _report],
            _output,
            _error,
            [
                new ScriptedMeasurer(_ =>
                {
                    run.Cancel();

                    throw new OperationCanceledException(run.Token);
                }),
            ],
            new WriterLogger(_output, _error),
            run.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Not.Zero);
            Assert.That(await File.ReadAllTextAsync(_report), Is.EqualTo("earlier"));
        }
    }

    // A file at the path of the players folder fails without a stack trace or a report.
    [Test]
    public async Task RunAsync_PlayersPathIsAFile_FailsWithoutAStackTrace()
    {
        string players = Path.Combine(_folder.Root, "players.bin");
        await File.WriteAllTextAsync(players, "");

        int code = await CommandLine.RunAsync(
            ["engine", players, "--output", _report],
            _output,
            _error,
            [new FakeMeasurer()],
            new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Is.Not.Empty.And.Not.Contain("Unhandled"));
            Assert.That(File.Exists(_report), Is.False);
        }
    }

    // A blank report path fails without a stack trace and before the 1st player.
    [Test]
    public async Task RunAsync_BlankOutput_FailsBeforeAnyPlayer()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, First));
        FakeMeasurer measurer = new();

        int code = await CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", ""],
            _output,
            _error,
            [measurer],
            new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(measurer.Measured, Is.Zero);
            Assert.That(_error.ToString(), Does.Not.Contain("Unhandled"));
        }
    }

    private static PlayerMeasurement Measurement()
    {
        return new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>>(),
            new Dictionary<string, int> { ["Type.Member"] = 0x28 },
            new Dictionary<string, string> { ["Type.Other"] = "Not found." },
            "file.bin");
    }

    private Task<int> RunAsync(IEngineMeasurer measurer)
    {
        return CommandLine.RunAsync(["engine", _folder.Root, "--output", _report], _output, _error, [measurer], new WriterLogger(_output, _error));
    }
}
