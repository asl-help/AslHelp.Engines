using AslHelp.Engines.Measuring.Cli.Unreal3;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Parses and runs the measuring command.
/// </summary>
internal static class CommandLine
{
    /// <summary>
    ///     Parses <paramref name="args"/> and measures the players with every engine.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">The writer for help and progress.</param>
    /// <param name="error">The writer for parse errors and failures.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        return RunAsync(args, output, error, [new UdkMeasurer()]);
    }

    /// <summary>
    ///     Parses <paramref name="args"/> and measures the players with <paramref name="measurers"/>.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">The writer for help and progress.</param>
    /// <param name="error">The writer for parse errors and failures.</param>
    /// <param name="measurers">The measurers the command can pick by engine name.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyList<IEngineMeasurer> measurers)
    {
        Argument<IEngineMeasurer> engineArgument = new("engine")
        {
            Description = SR.EngineDescription,
            CustomParser = result => measurers.First(measurer => measurer.Name.Equals(result.Tokens[0].Value, StringComparison.Ordinal)),
        };
        engineArgument.AcceptOnlyFromAmong([.. measurers.Select(static measurer => measurer.Name)]);

        Argument<string> playersArgument = new("players") { Description = SR.PlayersDescription };
        Option<string> outputOption = new("--output") { Description = SR.OutputDescription, Required = true };

        RootCommand root = [with(SR.RootDescription), engineArgument, playersArgument, outputOption];

        root.SetAction((parseResult, cancellationToken) => MeasureAsync(
            parseResult.GetRequiredValue(engineArgument),
            parseResult.GetRequiredValue(playersArgument),
            parseResult.GetRequiredValue(outputOption),
            output,
            error,
            cancellationToken));

        return root.Parse(args).InvokeAsync(new() { Output = output, Error = error });
    }

    /// <summary>
    ///     Measures every player of the engine in <paramref name="playersDirectory"/> and writes the report.
    /// </summary>
    /// <param name="measurer">The measurer of the engine.</param>
    /// <param name="playersDirectory">The folder with the players.</param>
    /// <param name="reportPath">The file for the report.</param>
    /// <param name="output">The writer for the steps.</param>
    /// <param name="error">The writer for failures.</param>
    /// <param name="cancellationToken">The token that stops the run.</param>
    /// <returns>
    ///     0 if every player was measured or 1 otherwise.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private static async Task<int> MeasureAsync(
        IEngineMeasurer measurer,
        string playersDirectory,
        string reportPath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var players = PlayerFolder.Find(playersDirectory, measurer.Name);

            if (players.Count == 0)
            {
                string none = SR.FormatNoPlayers(playersDirectory, measurer.Name);
                await error.WriteLineAsync(none.AsMemory(), cancellationToken).ConfigureAwait(false);

                return 1;
            }

            // Create the report folder first, because the rows live only in memory until the end of a run that takes minutes.
            _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);

            List<PlayerRow> rows = [];

            foreach (var player in players)
            {
                await output.WriteLineAsync(SR.FormatMeasuring(player.Version).AsMemory(), cancellationToken).ConfigureAwait(false);

                try
                {
                    await measurer.MeasureAsync(player.Directory, cancellationToken).ConfigureAwait(false);
                    rows.Add(new PlayerRow(player.Version, null));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Keep going, because a full run takes minutes and 1 failed player would throw away the rest.
                    rows.Add(new PlayerRow(player.Version, exception.Message));

                    string failed = SR.FormatPlayerFailed(player.Version, exception.Message);
                    await error.WriteLineAsync(failed.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }

            MeasureReport report = new(measurer.Name, rows);
            string json = JsonSerializer.Serialize(report, MeasureReportContext.Default.MeasureReport);
            await File.WriteAllTextAsync(reportPath, json, cancellationToken).ConfigureAwait(false);

            return rows.Exists(static row => row.Error is not null) ? 1 : 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync(exception.Message.AsMemory(), CancellationToken.None).ConfigureAwait(false);

            return 1;
        }
    }
}
