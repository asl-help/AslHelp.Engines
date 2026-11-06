using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class CommandLineRoutesTests
{
    private const string First = "engine-1.0-0123456789abcdef";
    private const uint GlobalRva = 0x2000;
    private const string Second = "engine-2.0-fedcba9876543210";

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

    // A route that covers part of a group logs its count out of the group and the other versions in full.
    [Test]
    public async Task RunAsync_PartlyCoveredGlobal_LogsTheRouteAndTheUncovered()
    {
        int code = await RunAsync(
            (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret()),
            (Second, new CodeBuilder(false).Nops(1)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(_output.ToString(), Does.Contain("Global: forward 8B 0D ????????, offset 2, versions 1/2"));
            Assert.That(_output.ToString(), Does.Contain("Global: uncovered [2.0]"));
            Assert.That(_output.ToString(), Does.Not.Contain("no route"));
        }
    }

    // The search result stays readable in the raw file, with the question marks unescaped and no executable path.
    [Test]
    public async Task RunAsync_RawReport_HasThePlainSignatureAndNoExecutablePath()
    {
        int code = await RunAsync((First, new CodeBuilder(false).MovEcx(GlobalRva).Ret()));

        string report = await File.ReadAllTextAsync(_report);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(report, Does.Contain("\"forward\": \"8B 0D ????????\""));
            Assert.That(report, Does.Not.Contain("file.bin"));
            Assert.That(report, Does.Not.Contain("executablePath"));
        }
    }

    // A corrupt executable leaves 1 version uncovered and the report still comes out, because the players took minutes to run.
    [Test]
    public async Task RunAsync_CorruptExecutable_StillWritesTheReport()
    {
        int code = await RunAsync(
            (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret()),
            (Second, new CodeBuilder(false, rva: 0x8000_0000).MovEcx(GlobalRva).Ret()));

        Assert.That(File.Exists(_report), Is.True, _error.ToString());

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var global = report.RootElement.GetProperty("groups")[0].GetProperty("routes").GetProperty("Global");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(global.GetProperty("routes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(global.GetProperty("uncovered").TryGetProperty("2.0", out _), Is.True);
        }
    }

    // The duration line keeps its period under a culture with a decimal comma, because the log has 1 format on every machine.
    [Test]
    [SetCulture("de-DE")]
    public async Task RunAsync_CultureWithDecimalComma_LogsTheDurationWithAPeriod()
    {
        _ = await RunAsync((First, new CodeBuilder(false).MovEcx(GlobalRva).Ret()));

        Assert.That(_output.ToString(), Does.Match(@"routes \(\d+\.\d s\)"));
    }

    // Each group counts its versions in its own route lines.
    [Test]
    public async Task RunAsync_TwoGroups_CountsVersionsPerGroup()
    {
        string first = Executable(First, new CodeBuilder(false).MovEcx(GlobalRva).Ret());
        string second = Executable(Second, new CodeBuilder(false).MovEcx(GlobalRva).Ret());

        int code = await CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", _report],
            _output,
            _error,
            [
                new ScriptedMeasurer(directory =>
                {
                    bool isFirst = directory.EndsWith(First, StringComparison.Ordinal);

                    return new PlayerMeasurement(
                        new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [GlobalRva] },
                        new Dictionary<string, int> { ["Type.Member"] = isFirst ? 0x28 : 0x2C },
                        new Dictionary<string, string>(),
                        isFirst ? first : second);
                }),
            ],
            new WriterLogger(_output, _error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(_output.ToString().Split("versions 1/1"), Has.Length.EqualTo(3));
        }
    }

    private Task<int> RunAsync(params (string Folder, CodeBuilder Builder)[] players)
    {
        Dictionary<string, string> paths = [with(StringComparer.Ordinal)];

        foreach ((string folder, var builder) in players)
        {
            paths[folder] = Executable(folder, builder);
        }

        return CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", _report],
            _output,
            _error,
            [
                new ScriptedMeasurer(directory => new PlayerMeasurement(
                    new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [GlobalRva] },
                    new Dictionary<string, int> { ["Type.Member"] = 0x28 },
                    new Dictionary<string, string>(),
                    paths[Path.GetFileName(directory)])),
            ],
            new WriterLogger(_output, _error));
    }

    private string Executable(string folder, CodeBuilder builder)
    {
        string directory = Path.Combine(_folder.Root, folder);
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "file.bin");
        CodeBuilder.WritePe(path, builder.Build());

        return path;
    }
}

// Returns the measurement that the test builds for each player folder.
file sealed class ScriptedMeasurer(
    Func<string, PlayerMeasurement> measure) : IEngineMeasurer
{
    public string Name => "engine";

    public Result LoadTargets(string? path)
    {
        return Result.Ok();
    }

    public Task<Result<PlayerMeasurement>> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        return Task.FromResult<Result<PlayerMeasurement>>(measure(playerDirectory));
    }
}
