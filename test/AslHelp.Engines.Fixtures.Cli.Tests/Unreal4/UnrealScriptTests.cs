using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealScriptTests
{
    private static readonly string _buildDirectory = Path.Combine(AppContext.BaseDirectory, "build", "unreal4");
    private static readonly string[] _stepNames = ["editor.bat", "package.bat", "runtimes.bat"];

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

    // Every .ps1 of the image parses in Windows PowerShell, which the image runs them with, so a typo fails here and not hours
    // into a docker build.
    [Test]
    [Platform("Win")]
    public async Task ImageScripts_EveryPs1_ParsesInWindowsPowerShell()
    {
        string image = Path.Combine(_buildDirectory, "image");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(image, "*.ps1"), Is.Not.Empty);
            Assert.That(await ParseErrorsAsync(image), Is.Empty);
        }
    }

    // A script with a syntax error is reported with its file, so the check above really sees errors.
    [Test]
    [Platform("Win")]
    public async Task ParseErrors_BrokenScript_ListsTheFile()
    {
        string folder = _folder.NewFolder();
        WriteBytes(folder, "broken.ps1", "if ($true) {"u8.ToArray());

        Assert.That(await ParseErrorsAsync(folder), Does.Contain("broken.ps1"));
    }

    // The CLI project copies the step scripts to build/unreal4/steps and the copy reaches this test project, so a build finds them there.
    [Test]
    public void StepScripts_CopiedToOutput_HasEveryStep()
    {
        string steps = Path.Combine(_buildDirectory, "steps");

        Assert.That(
            Directory.GetFiles(steps),
            Is.EquivalentTo(_stepNames.Select(name => Path.Combine(steps, name))));
    }

    // Parse every .ps1 under the folder without running it and print 1 line per error.
    private static async Task<string> ParseErrorsAsync(string folder)
    {
        string command = $$"""
            Get-ChildItem -LiteralPath '{{folder}}' -Filter *.ps1 -Recurse | ForEach-Object {
                $errors = $null
                $null = [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$null, [ref]$errors)
                foreach ($e in $errors) { "$($_.FullName)($($e.Extent.StartLineNumber)): $($e.Message)" }
            }
            """;

        ProcessStartInfo info = new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync();
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output + await error;
    }
}
