using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

// Returns 1 value of each kind and fails for the player folders the test lists, because a real measurer needs a running engine.
internal sealed class FakeMeasurer(params string[] failing) : IEngineMeasurer
{
    public string Name => "engine";

    public int Measured { get; private set; }

    public string? TargetsPath { get; private set; }

    public Result LoadTargets(string? path)
    {
        TargetsPath = path;

        return path?.EndsWith(".bad", StringComparison.Ordinal) == true
            ? new TextError($"'{path}' is not valid.")
            : Result.Ok();
    }

    public Task<Result<PlayerMeasurement>> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        Measured++;

        if (Array.Exists(failing, playerDirectory.EndsWith))
        {
            return Task.FromResult<Result<PlayerMeasurement>>(new TextError("The player did not start."));
        }

        return Task.FromResult<Result<PlayerMeasurement>>(new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [0x100, 0x200] },
            new Dictionary<string, int> { ["Type.Member"] = 40 },
            new Dictionary<string, string> { ["Type.Other"] = "Not found." },
            Path.Combine(playerDirectory, "missing.exe")));
    }
}
