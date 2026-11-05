using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

[NonParallelizable]
public sealed class CommandLineEnvironmentTests
{
    private const string Url = "https://dav.example/remote.php/dav/files/u/fixtures";

    private static readonly string[] _variables = [DavStore.UrlVariable, DavStore.UserVariable, DavStore.TokenVariable];

    private readonly Dictionary<string, string?> _saved = [];

    private TestFolder _folder = null!;
    private string _output = null!;
    private string _projects = null!;

    [SetUp]
    public void SetUp()
    {
        // Clear the variables, because a machine can have the real ones set and no test may reach the real store.
        foreach (string variable in _variables)
        {
            _saved[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }

        _folder = new TestFolder();
        _projects = _folder.NewFolder();
        _output = _folder.NewFolder();

        TestFolder.WriteBytes(_projects, "player/file.bin", [4, 5, 6]);
    }

    [TearDown]
    public void TearDown()
    {
        foreach ((string variable, string? value) in _saved)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }

        _folder.Delete();
    }

    // Help describes the tool and succeeds.
    [Test]
    public async Task RunAsync_Help_PrintsDescriptionAndSucceeds()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(["--help"], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("Builds the fixture players"));
            Assert.That(output.ToString(), Does.Contain("Usage:"));
        }
    }

    // A run with no command prints the help and fails, so a script that passes no command stops.
    [Test]
    public async Task RunAsync_NoArguments_PrintsHelpAndFails()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync([], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Not.Zero);
            Assert.That(output.ToString(), Does.Contain("Usage:"));
        }
    }

    // An unknown argument fails with an error that shows it.
    [Test]
    public async Task RunAsync_UnknownArgument_FailsAndShowsArgument()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(["frob"], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Not.Zero);
            Assert.That(error.ToString(), Does.Contain("frob"));
        }
    }

    // With none of the 3 variables set the build stays local and 1 line lists the variables that turn the cache on.
    [Test]
    public async Task RunAsync_BuildNoVariables_BuildsLocallyAndPrintsTheSkipLine()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        var key = PlayerCache.KeyOf(toolchain, "1.0", Path.Combine(_projects, "player")).Key;
        string player = Path.Combine(_output, $"player-1.0-{key.ShortHash}");

        int code = await RunBuildAsync(output, error, toolchain);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error.ToString());
            Assert.That(toolchain.Calls, Has.Count.EqualTo(3));
            Assert.That(Directory.Exists(player), Is.True);
            Assert.That(Directory.GetFiles(_output, "*.tar.zst"), Is.Empty);
            Assert.That(
                output.ToString(),
                Does.Contain(DavStore.UrlVariable).And.Contain(DavStore.UserVariable).And.Contain(DavStore.TokenVariable));
        }
    }

    // With only the URL set the run fails before any step and lists the 2 missing variables.
    [Test]
    public async Task RunAsync_BuildSomeVariables_FailsWithTheMissingOnes()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        FakeToolchain toolchain = new("player");
        Environment.SetEnvironmentVariable(DavStore.UrlVariable, Url);

        int code = await RunBuildAsync(output, error, toolchain);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain(DavStore.UserVariable).And.Contain(DavStore.TokenVariable));
            Assert.That(error.ToString(), Does.Not.Contain(DavStore.UrlVariable));
            Assert.That(toolchain.Calls, Is.Empty);
        }
    }

    private Task<int> RunBuildAsync(StringWriter output, StringWriter error, FakeToolchain toolchain)
    {
        return CommandLine.RunAsync(
            ["build", toolchain.Name, "1.0", "--output", _output],
            output,
            error,
            [toolchain],
            DavStore.FromEnvironmentIfSet,
            _projects);
    }
}
