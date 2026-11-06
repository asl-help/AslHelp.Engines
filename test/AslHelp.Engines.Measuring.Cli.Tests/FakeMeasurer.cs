using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

// Returns 1 value of each kind and throws for the player folders the test lists, because a real measurer needs a running engine.
internal sealed class FakeMeasurer(params string[] failing) : IEngineMeasurer
{
    public string Name => "engine";

    public int Measured { get; private set; }

    public string? TargetsPath { get; private set; }

    public void LoadTargets(string? path)
    {
        TargetsPath = path;

        if (path?.EndsWith(".bad", StringComparison.Ordinal) == true)
        {
            throw new InvalidDataException($"'{path}' is not valid.");
        }
    }

    public Task<PlayerMeasurement> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        Measured++;

        if (Array.Exists(failing, playerDirectory.EndsWith))
        {
            throw new InvalidOperationException("The player did not start.");
        }

        return Task.FromResult(new PlayerMeasurement(
            new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [0x100, 0x200] },
            new Dictionary<string, int> { ["Type.Member"] = 40 },
            new Dictionary<string, string> { ["Type.Other"] = "Not found." },
            Path.Combine(playerDirectory, "missing.exe")));
    }
}
