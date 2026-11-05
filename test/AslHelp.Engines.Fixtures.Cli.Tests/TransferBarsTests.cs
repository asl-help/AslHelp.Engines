using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class TransferBarsTests
{
    // A transfer that reports nothing shows no bar, so a file that is there already prints no line.
    [Test]
    public async Task ShowAsync_TransferReportsNothing_PrintsNothingAndReturnsTheResult()
    {
        using StringWriter output = new();
        var bars = TransferBars.Create(output);

        bool result = await bars.ShowAsync("file.bin", _ => Task.FromResult(true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(output.ToString(), Is.Empty);
        }
    }

    // On a writer that is not the console, a transfer prints its name and its percentage as a plain line.
    [Test]
    public async Task ShowAsync_TransferReportsHalf_PrintsTheNameAndPercentage()
    {
        using StringWriter output = new();
        var bars = TransferBars.Create(output);

        await bars.ShowAsync("file.bin", progress => Report(progress, new TransferProgress(50, 100)));

        Assert.That(output.ToString(), Is.EqualTo($"file.bin: 50%{Environment.NewLine}"));
    }

    // A transfer with no length shows no bar, because a bar without an end would count as done at its 1st chunk.
    [Test]
    public async Task ShowAsync_TransferReportsNoLength_PrintsNothing()
    {
        using StringWriter output = new();
        var bars = TransferBars.Create(output);

        await bars.ShowAsync("file.bin", progress => Report(progress, new TransferProgress(200, null)));

        Assert.That(output.ToString(), Is.Empty);
    }

    // A name with brackets prints as it is, because the display would read an unescaped '[' as the start of markup.
    [Test]
    public async Task ShowAsync_NameWithBrackets_PrintsTheNameAsItIs()
    {
        using StringWriter output = new();
        var bars = TransferBars.Create(output);

        await bars.ShowAsync("players/1.0[rc]/file.bin", progress => Report(progress, new TransferProgress(50, 100)));

        Assert.That(output.ToString(), Is.EqualTo($"players/1.0[rc]/file.bin: 50%{Environment.NewLine}"));
    }

    // GitHub Actions turns ANSI on for every console, so a writer that is not the console still gets no escape codes there.
    [Test]
    [NonParallelizable]
    public async Task ShowAsync_InGitHubActions_PrintsNoEscapeCodes()
    {
        string? githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        string? noColor = Environment.GetEnvironmentVariable("NO_COLOR");

        // Clear NO_COLOR as on a GitHub runner, because it turns the colors off however ANSI is detected.
        Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
        Environment.SetEnvironmentVariable("NO_COLOR", null);

        try
        {
            using StringWriter output = new();
            var bars = TransferBars.Create(output);

            await bars.ShowAsync("file.bin", progress => Report(progress, new TransferProgress(50, 100)));

            Assert.That(output.ToString(), Is.EqualTo($"file.bin: 50%{Environment.NewLine}"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", githubActions);
            Environment.SetEnvironmentVariable("NO_COLOR", noColor);
        }
    }

    // A failed transfer throws its own exception through the bar, so the failed step prints the real cause.
    [Test]
    public void ShowAsync_TransferThrows_ThrowsTheSameException()
    {
        var bars = TransferBars.Create(TextWriter.Null);

        Assert.That(
            async () => await bars.ShowAsync("file.bin", _ => Task.FromException<bool>(new IOException("The disk is full."))),
            Throws.TypeOf<IOException>().With.Message.EqualTo("The disk is full."));
    }

    private static Task Report(IProgress<TransferProgress> progress, TransferProgress value)
    {
        progress.Report(value);

        return Task.CompletedTask;
    }
}
