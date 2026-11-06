using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class CommandLineFailureTests
{
    private const string Player = "engine-1.0-0123456789abcdef";

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
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, Player));
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _error.Dispose();
        _folder.Delete();
    }

    // A message with braces and letters outside ASCII reaches the row and the failed line as it was, because the log template does not read it.
    [Test]
    public async Task RunAsync_FailedResultWithPlaceholders_WritesItUnchanged()
    {
        const string Message = "Can't read '{0}' at {address} フ {{x}}.";

        int code = await RunAsync(new FailingMeasurer(measureError: Message));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.RootElement.GetProperty("players")[0].GetProperty("error").GetString(), Is.EqualTo(Message));
            Assert.That(_error.ToString(), Does.Contain(Message));
        }
    }

    // A failed list with braces in its message reaches the failed line as it was.
    [Test]
    public async Task RunAsync_LoadTargetsFailsWithPlaceholders_LogsItUnchanged()
    {
        const string Message = "'{0}' is not valid at {path}.";

        int code = await RunAsync(new FailingMeasurer(targetsError: Message));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Does.Contain(Message));
        }
    }

    // An empty message still fails the row, because the failure is the error and not its text.
    [Test]
    public async Task RunAsync_FailedResultWithEmptyMessage_FailsTheRow()
    {
        int code = await RunAsync(new FailingMeasurer(measureError: string.Empty));

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var root = report.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(root.GetProperty("players")[0].GetProperty("error").GetString(), Is.Empty);
            Assert.That(root.GetProperty("groups").GetArrayLength(), Is.Zero);
        }
    }

    private Task<int> RunAsync(IEngineMeasurer measurer)
    {
        return CommandLine.RunAsync(["engine", _folder.Root, "--output", _report], _output, _error, [measurer], new WriterLogger(_output, _error));
    }
}

file sealed class FailingMeasurer(
    string? targetsError = null,
    string? measureError = null) : IEngineMeasurer
{
    public string Name => "engine";

    public Result LoadTargets(string? path)
    {
        return targetsError is null ? Result.Ok() : new TextError(targetsError);
    }

    public Task<Result<PlayerMeasurement>> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        return Task.FromResult<Result<PlayerMeasurement>>(new TextError(measureError!));
    }
}
