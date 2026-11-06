using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Measures the players that UDK built.
/// </summary>
internal sealed class UdkMeasurer : IEngineMeasurer
{
    /// <inheritdoc/>
    public string Name => "unreal3";

    /// <inheritdoc/>
    public async Task MeasureAsync(string playerDirectory, CancellationToken cancellationToken)
    {
        using var player = await UdkPlayer.StartAsync(playerDirectory, cancellationToken).ConfigureAwait(false);
    }
}
