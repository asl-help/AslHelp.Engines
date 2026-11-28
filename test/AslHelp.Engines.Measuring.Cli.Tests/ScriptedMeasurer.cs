using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

// Measures each player folder through the given function, because the command line tests pick the result per folder.
internal sealed class ScriptedMeasurer(Func<string, Result<PlayerMeasurement>> measure) : IEngineMeasurer
{
    public string? Key { get; init; }

    public Action<string, ILogger>? Log { get; init; }

    public int MaxDegreeOfParallelism { get; init; } = 1;

    public string Name => "engine";

    public IReadOnlyList<Option> Options { get; init; } = [];

    public Func<IReadOnlyList<PlayerFolder>, IReadOnlyList<PlayerFolder>> Select { get; init; } = static players => players;

    public string? TargetsError { get; init; }

    public Result LoadTargets(string? path)
    {
        return TargetsError is null ? Result.Ok() : new TextError(TargetsError);
    }

    public IReadOnlyList<PlayerFolder> SelectPlayers(IReadOnlyList<PlayerFolder> players, ParseResult parseResult, ILogger logger)
    {
        return Select(players);
    }

    public string? GroupOf(string version)
    {
        return Key;
    }

    public bool IsNeeded(string type, string member)
    {
        return true;
    }

    public Task<Result<PlayerMeasurement>> MeasureAsync(PlayerFolder player, ILogger logger, CancellationToken cancellationToken)
    {
        Log?.Invoke(player.Directory, logger);

        return Task.FromResult(measure(player.Directory));
    }
}
