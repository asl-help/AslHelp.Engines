using System;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

// Measures nothing and throws for the player folders the test lists, because a real measurer needs a running engine.
internal sealed class FakeMeasurer(params string[] failing) : IEngineMeasurer
{
    public string Name => "engine";

    public Task MeasureAsync(string playerDirectory, CancellationToken cancellationToken)
    {
        if (Array.Exists(failing, playerDirectory.EndsWith))
        {
            throw new InvalidOperationException("The player did not start.");
        }

        return Task.CompletedTask;
    }
}
