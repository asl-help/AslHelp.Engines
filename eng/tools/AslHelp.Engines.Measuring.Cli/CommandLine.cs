using AslHelp.Engines.Measuring.Cli.Unreal3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
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
    ///     Parses <paramref name="args"/> and measures the players with every engine, logging to the console.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">The writer for help.</param>
    /// <param name="error">The writer for parse errors.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        // Dispose the factory before the exit, because the console logger writes its lines from a queue.
        using var factory = LoggerFactory.Create(static builder => builder
            .AddConsole(static options =>
            {
                options.FormatterName = MeasureConsoleFormatter.FormatterName;
                options.LogToStandardErrorThreshold = LogLevel.Error;
            })
            .AddConsoleFormatter<MeasureConsoleFormatter, ConsoleFormatterOptions>());

        return await RunAsync(args, output, error, [new UdkMeasurer()], factory.CreateLogger("measure")).ConfigureAwait(false);
    }

    /// <summary>
    ///     Parses <paramref name="args"/> and measures the players with <paramref name="measurers"/>.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">The writer for help.</param>
    /// <param name="error">The writer for parse errors.</param>
    /// <param name="measurers">The measurers the command can pick by engine name.</param>
    /// <param name="logger">The logger for the steps and the failures of the run.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyList<IEngineMeasurer> measurers, ILogger logger)
    {
        Argument<IEngineMeasurer> engineArgument = new("engine")
        {
            Description = SR.EngineDescription,
            CustomParser = result => measurers.First(measurer => measurer.Name.Equals(result.Tokens[0].Value, StringComparison.Ordinal)),
        };
        engineArgument.AcceptOnlyFromAmong([.. measurers.Select(static measurer => measurer.Name)]);

        Argument<string> playersArgument = new("players") { Description = SR.PlayersDescription };
        Option<string> outputOption = new("--output") { Description = SR.OutputDescription, Required = true };
        Option<string?> targetsOption = new("--targets") { Description = SR.TargetsDescription };

        RootCommand root = [with(SR.RootDescription), engineArgument, playersArgument, outputOption, targetsOption];

        root.SetAction((parseResult, cancellationToken) => MeasureAsync(
            parseResult.GetRequiredValue(engineArgument),
            parseResult.GetRequiredValue(playersArgument),
            parseResult.GetRequiredValue(outputOption),
            parseResult.GetValue(targetsOption),
            logger,
            cancellationToken));

        return root.Parse(args).InvokeAsync(new() { Output = output, Error = error });
    }

    /// <summary>
    ///     Measures every player of the engine in <paramref name="playersDirectory"/> and writes the report.
    /// </summary>
    /// <param name="measurer">The measurer of the engine.</param>
    /// <param name="playersDirectory">The folder with the players.</param>
    /// <param name="reportPath">The file for the report.</param>
    /// <param name="targetsPath">The file with the values to find or <see langword="null"/> for the list built into the measurer.</param>
    /// <param name="logger">The logger for the steps and the failures of the run.</param>
    /// <param name="cancellationToken">The token that stops the run.</param>
    /// <returns>
    ///     0 if every player was measured or 1 otherwise.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private static async Task<int> MeasureAsync(
        IEngineMeasurer measurer,
        string playersDirectory,
        string reportPath,
        string? targetsPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            measurer.LoadTargets(targetsPath);

            var players = PlayerFolder.Find(playersDirectory, measurer.Name);

            if (players.Count == 0)
            {
                logger.NoPlayers(playersDirectory, measurer.Name);

                return 1;
            }

            logger.PlayersFound(players.Count, playersDirectory);

            // Create the report folder first, because the rows live only in memory until the end of a run that takes minutes.
            _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);

            List<PlayerRow> rows = [];

            foreach (var player in players)
            {
                logger.Starting(player.Version, rows.Count + 1, players.Count);

                using var scope = logger.BeginPlayerScope(player.Version);
                long started = Stopwatch.GetTimestamp();

                try
                {
                    var measurement = await measurer.MeasureAsync(player.Directory, logger, cancellationToken).ConfigureAwait(false);
                    rows.Add(PlayerRow.Measured(player.Version, measurement));

                    foreach ((string value, string reason) in measurement.Unmeasured)
                    {
                        logger.NotFound(value, reason);
                    }

                    var elapsed = Stopwatch.GetElapsedTime(started);

                    logger.Measured(
                        elapsed.TotalSeconds,
                        measurement.Globals.Count,
                        measurement.Offsets.Count,
                        measurement.Unmeasured.Count);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Keep going, because a full run takes minutes and 1 failed player would throw away the rest.
                    rows.Add(PlayerRow.Failed(player.Version, exception.Message));
                    logger.PlayerFailed(player.Version, exception.Message);
                }
            }

            MeasureReport report = new(measurer.Name, rows);
            string json = JsonSerializer.Serialize(report, MeasureReportContext.Default.MeasureReport);
            await File.WriteAllTextAsync(reportPath, json, cancellationToken).ConfigureAwait(false);

            int measured = rows.Count(static row => row.Error is null);
            logger.ReportWritten(reportPath, measured, rows.Count);

            return measured == rows.Count ? 0 : 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.RunFailed(exception.Message);

            return 1;
        }
    }
}
