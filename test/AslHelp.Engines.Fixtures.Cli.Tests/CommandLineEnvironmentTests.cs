using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class CommandLineEnvironmentTests
{
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
}
