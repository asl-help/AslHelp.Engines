using AslHelp.Engines.Measuring.Cli.Routes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.ComponentModel;
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
    ///     Parses <paramref name="args"/> and measures the players with <paramref name="measurers"/>, logging to the console.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">The writer for help.</param>
    /// <param name="error">The writer for parse errors.</param>
    /// <param name="measurers">The measurers, each with its own subcommand.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyList<IEngineMeasurer> measurers)
    {
        // Dispose the factory before the exit, because the console logger writes its lines from a queue.
        using var factory = LoggerFactory.Create(static builder => builder
            .AddConsole(static options =>
            {
                options.FormatterName = MeasureConsoleFormatter.FormatterName;
                options.LogToStandardErrorThreshold = LogLevel.Error;
            })
            .AddConsoleFormatter<MeasureConsoleFormatter, ConsoleFormatterOptions>());

        return await RunAsync(args, output, error, measurers, factory.CreateLogger("measure")).ConfigureAwait(false);
    }

    /// <summary>
    ///     Parses <paramref name="args"/> and measures the players with <paramref name="measurers"/>.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">The writer for help.</param>
    /// <param name="error">The writer for parse errors.</param>
    /// <param name="measurers">The measurers, each with its own subcommand.</param>
    /// <param name="logger">The logger for the steps and the failures of the run.</param>
    /// <param name="cancellationToken">The token that stops the run.</param>
    /// <returns>
    ///     The exit code of the run.
    /// </returns>
    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyList<IEngineMeasurer> measurers,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        Argument<string> playersArgument = new("players") { Description = SR.PlayersDescription };
        Option<string> outputOption = new("--output") { Description = SR.OutputDescription, Required = true };
        Option<string?> targetsOption = new("--targets") { Description = SR.TargetsDescription };
        Option<int?> jobsOption = new("--jobs", "-j") { Description = SR.JobsDescription };
        jobsOption.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<int?>() is < 1)
            {
                result.AddError(SR.JobsBelowOne);
            }
        });

        RootCommand root = [with(SR.RootDescription)];
        root.SetAction(PrintHelpAndFail);

        foreach (var measurer in measurers)
        {
            Command command =
            [
                with(measurer.Name, SR.FormatEngineDescription(measurer.Name)),
                playersArgument,
                outputOption,
                targetsOption,
                jobsOption,
                .. measurer.Options,
            ];

            command.SetAction((parseResult, cancellationToken) => MeasureAsync(
                measurer,
                parseResult.GetRequiredValue(playersArgument),
                parseResult.GetRequiredValue(outputOption),
                parseResult.GetValue(targetsOption),
                JobsOf(measurer, parseResult.GetValue(jobsOption), logger),
                parseResult,
                logger,
                cancellationToken));

            root.Subcommands.Add(command);
        }

        return root.Parse(args).InvokeAsync(new() { Output = output, Error = error }, cancellationToken);
    }

    /// <summary>
    ///     Prints the help of the parsed command and fails, so a script without a subcommand stops.
    /// </summary>
    /// <param name="parseResult">The parsed command line.</param>
    /// <returns>
    ///     1, the exit code of a failed run.
    /// </returns>
    private static int PrintHelpAndFail(ParseResult parseResult)
    {
        _ = new HelpAction().Invoke(parseResult);

        return 1;
    }

    /// <summary>
    ///     Picks the number of players to measure at once, the value of <c>--jobs</c> up to the limit of <paramref name="measurer"/>.
    /// </summary>
    /// <param name="measurer">The measurer of the engine.</param>
    /// <param name="requested">The value of <c>--jobs</c> or <see langword="null"/> without the option.</param>
    /// <param name="logger">The logger for a value above the limit.</param>
    /// <returns>
    ///     The number of players to measure at once, from 1 to the limit of <paramref name="measurer"/>.
    /// </returns>
    private static int JobsOf(
        IEngineMeasurer measurer,
        int? requested,
        ILogger logger)
    {
        int limit = measurer.MaxDegreeOfParallelism;

        if (requested > limit)
        {
            logger.JobsLowered(limit, requested.Value);
        }

        return Math.Min(requested ?? limit, limit);
    }

    /// <summary>
    ///     Measures the players of the engine in <paramref name="playersDirectory"/> that the measurer picks,
    ///     logs the groups of versions with equal offsets and the routes they share and writes the report.
    /// </summary>
    /// <param name="measurer">The measurer of the engine.</param>
    /// <param name="playersDirectory">The folder with the players.</param>
    /// <param name="reportPath">The file for the report.</param>
    /// <param name="targetsPath">The file with the values to find or <see langword="null"/> for the list built into the measurer.</param>
    /// <param name="maxDegreeOfParallelism">The number of players to measure at once, from 1 to the limit of the measurer.</param>
    /// <param name="parseResult">The parsed command line with the values of the options of the measurer.</param>
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
        int maxDegreeOfParallelism,
        ParseResult parseResult,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (measurer.LoadTargets(targetsPath).TryUnwrapErr(out var targetsError))
            {
                logger.RunFailed(targetsError.Message);

                return 1;
            }

            var players = measurer.SelectPlayers(PlayerFolder.Find(playersDirectory, measurer.Name), parseResult, logger);

            if (players.Count == 0)
            {
                logger.NoPlayers(playersDirectory, measurer.Name);

                return 1;
            }

            // Refuse 2 builds of 1 version, because the route search finds a build by its version and 1 build would get the routes of the other.
            var builds = players.GroupBy(static player => player.Version, StringComparer.Ordinal).FirstOrDefault(static same => same.Skip(1).Any());

            if (builds is not null)
            {
                logger.RunFailed(SR.FormatPlayersSameVersion(builds.Key, string.Join(", ", builds.Select(static player => player.Directory))));

                return 1;
            }

            logger.PlayersFound(players.Count, playersDirectory);

            // Create the report folder first, because the rows live only in memory until the end of a run that takes minutes.
            _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);

            List<PlayerRow> rows = [];
            Dictionary<string, PlayerMeasurement> measurements = [with(StringComparer.Ordinal)];
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var parallel = StartParallel(measurer, players, maxDegreeOfParallelism, stop.Token);

            try
            {
                for (int index = 0; index < players.Count; index++)
                {
                    var player = players[index];
                    logger.Starting(player.Version, index + 1, players.Count);

                    using var scope = logger.BeginPlayerScope(player.Version);
                    PlayerOutcome outcome;

                    if (parallel is null)
                    {
                        outcome = await MeasurePlayerAsync(measurer, player, logger, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var buffered = await parallel.Results[index].Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        buffered.Logger.Replay(logger);
                        outcome = buffered.Outcome;
                    }

                    if (outcome is FailedPlayer failed)
                    {
                        rows.Add(PlayerRow.Failed(player.Version, failed.Error));
                        logger.PlayerFailed(player.Version, failed.Error);

                        continue;
                    }

                    var measurement = ((MeasuredPlayer)outcome).Measurement;
                    rows.Add(PlayerRow.Measured(player.Version, measurement));
                    measurements[player.Version] = measurement;

                    foreach ((string value, string reason) in measurement.Unmeasured)
                    {
                        logger.NotFound(value, reason);
                    }

                    logger.Measured(
                        outcome.Elapsed.TotalSeconds,
                        measurement.Globals.Count,
                        measurement.Offsets.Count,
                        measurement.Unmeasured.Count);
                }
            }
            finally
            {
                if (parallel is not null)
                {
                    // Stop and wait for the players that still run, because a player that a measurer started has to end before the run returns.
                    await stop.CancelAsync().ConfigureAwait(false);
                    await parallel.Run.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }

            var measuredGroups = VersionGroups.Find(rows, measurer.GroupOf, measurer.IsNeeded);
            List<VersionGroup> groups = [];

            if (measuredGroups.Count > 0)
            {
                logger.GroupsHeader(measuredGroups.Count);

                using var scope = logger.BeginGroupsScope();

                foreach (var group in measuredGroups)
                {
                    groups.Add(FindRoutes(group, measurements, logger, cancellationToken));
                }
            }

            MeasureReport report = new(measurer.Name, rows, groups);
            string json = JsonSerializer.Serialize(report, MeasureReportContext.Default.MeasureReport);
            await File.WriteAllTextAsync(reportPath, json, cancellationToken).ConfigureAwait(false);

            int measured = rows.Count(static row => row.Error is null);
            logger.ReportWritten(reportPath, measured, rows.Count);

            return measured == rows.Count ? 0 : 1;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Log only the message, because a bad path or a failed read of the players or a failed write of the report is no bug.
            logger.RunFailed(exception.Message);

            return 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Unexpected(exception, exception.GetType().ToString());
            logger.RunFailed(exception.Message);

            return 1;
        }
    }

    /// <summary>
    ///     Starts the measurements of <paramref name="players"/>, <paramref name="maxDegreeOfParallelism"/> at once, each into its own log buffer.
    /// </summary>
    /// <remarks>
    ///     Takes the players in order, so the 1st players start first and their lines come out while the rest run.
    /// </remarks>
    /// <param name="measurer">The measurer of the engine.</param>
    /// <param name="players">The players in version order.</param>
    /// <param name="maxDegreeOfParallelism">The number of players to measure at once.</param>
    /// <param name="cancellationToken">The token that stops the measurements.</param>
    /// <returns>
    ///     The result of each player by position with the task of the whole run
    ///     or <see langword="null"/> when the run measures 1 player at a time.
    /// </returns>
    private static ParallelPlayers? StartParallel(
        IEngineMeasurer measurer,
        IReadOnlyList<PlayerFolder> players,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        if (maxDegreeOfParallelism <= 1)
        {
            return null;
        }

        TaskCompletionSource<BufferedPlayer>[] results =
        [
            .. players.Select(static _ => new TaskCompletionSource<BufferedPlayer>(TaskCreationOptions.RunContinuationsAsynchronously)),
        ];

        ParallelOptions options = new() { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken };

        var run = Parallel.ForEachAsync(Enumerable.Range(0, players.Count), options, async (index, token) =>
        {
            BufferedLogger buffer = new();

            try
            {
                var outcome = await MeasurePlayerAsync(measurer, players[index], buffer, token).ConfigureAwait(false);
                results[index].SetResult(new BufferedPlayer(outcome, buffer));
            }
            catch (OperationCanceledException exception)
            {
                // Cancel the result, so no result stays pending after the run stops.
                // Cancel it instead of failing it, because nobody awaits the results after the 1st cancel and a failed task that
                // nobody observes reports itself.
                results[index].SetCanceled(exception.CancellationToken);
            }
        });

        return new ParallelPlayers(results, run);
    }

    /// <summary>
    ///     Measures <paramref name="player"/> and times it.
    /// </summary>
    /// <param name="measurer">The measurer of the engine.</param>
    /// <param name="player">The player folder.</param>
    /// <param name="logger">The logger for the steps of the measurement.</param>
    /// <param name="cancellationToken">The token that stops the measurement.</param>
    /// <returns>
    ///     The values found or the failure, with the time the measurement took.
    ///     A cancel that does not come from <paramref name="cancellationToken"/>, such as a timeout inside the measurer, fails the player.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private static async Task<PlayerOutcome> MeasurePlayerAsync(
        IEngineMeasurer measurer,
        PlayerFolder player,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        try
        {
            var result = await measurer.MeasureAsync(player, logger, cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (!result.TryUnwrap(out var measurement, out var error))
            {
                return new FailedPlayer(error.Message, elapsed);
            }

            return measurement.FindMalformedOffsetKey() is { } key
                ? new FailedPlayer(SR.FormatOffsetKeyMalformed(key), elapsed)
                : new MeasuredPlayer(measurement, elapsed);
        }
        catch (Exception exception)
            when (IsPlayerFailure(exception) || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Keep going, because a full run takes minutes and 1 player that does not start would throw away the rest.
            return new FailedPlayer(exception.Message, Stopwatch.GetElapsedTime(started));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Log the whole exception, because a bug in a measurer needs its stack. Then keep going like for a failed player.
            logger.Unexpected(exception, exception.GetType().ToString());

            return new FailedPlayer(exception.Message, Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    ///     Checks whether <paramref name="exception"/> is 1 of the types that <see cref="IEngineMeasurer.MeasureAsync"/> throws
    ///     for a player that does not start.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>
    ///     <see langword="true"/> for an expected failure of a player, <see langword="false"/> for a bug.
    /// </returns>
    private static bool IsPlayerFailure(Exception exception)
    {
        return exception is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException or Win32Exception;
    }

    /// <summary>
    ///     Logs <paramref name="group"/> and searches the routes that its versions share.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="measurements">The measurement of each measured version.</param>
    /// <param name="logger">The logger for the steps and the failures of the run.</param>
    /// <param name="cancellationToken">The token that stops the search.</param>
    /// <returns>
    ///     The group with its routes.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    private static VersionGroup FindRoutes(
        VersionGroup group,
        Dictionary<string, PlayerMeasurement> measurements,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        string versions = string.Join(", ", group.Versions);

        if (group.Key is null)
        {
            logger.Group(versions);
        }
        else
        {
            logger.KeyedGroup(group.Key, versions);
        }

        using var scope = logger.BeginGroupScope(versions);
        long started = Stopwatch.GetTimestamp();

        RouteVersion[] routeVersions =
        [
            .. group.Versions.Select(version => new RouteVersion(version, measurements[version].ExecutablePath, measurements[version].Globals)),
        ];

        var routes = SharedRoutes.Find(routeVersions, logger, cancellationToken);

        foreach ((string global, var globalRoutes) in routes)
        {
            foreach (var route in globalRoutes.Routes)
            {
                logger.RouteFound(global, route.Forward, route.Offset, route.Versions.Count, group.Versions.Count);
            }

            if (globalRoutes.Uncovered is { } uncovered)
            {
                string uncoveredVersions = string.Join(", ", uncovered.Keys);

                if (globalRoutes.Routes.Count == 0)
                {
                    logger.NoRoute(global, uncoveredVersions);
                }
                else
                {
                    logger.RouteUncovered(global, uncoveredVersions);
                }
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.RoutesDone(elapsed.TotalSeconds);

        return group with { Routes = routes };
    }

    // Describes the result of 1 player with the time it took.
    private abstract record PlayerOutcome(
        TimeSpan Elapsed);

    // Represents a player with its values.
    private sealed record MeasuredPlayer(
        PlayerMeasurement Measurement,
        TimeSpan Elapsed) : PlayerOutcome(Elapsed);

    // Represents a player that failed, with the reason.
    private sealed record FailedPlayer(
        string Error,
        TimeSpan Elapsed) : PlayerOutcome(Elapsed);

    // Pairs the result of 1 player with the lines it logged.
    private sealed record BufferedPlayer(
        PlayerOutcome Outcome,
        BufferedLogger Logger);

    // Pairs the result of each player with the run that measures them.
    private sealed record ParallelPlayers(
        TaskCompletionSource<BufferedPlayer>[] Results,
        Task Run);
}
