using AslHelp.Engines.Fixtures.Cli.Hosting;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

// Runs a fake executable as a player on this machine, because the tests have neither Docker nor a real engine.
internal sealed class LocalPlayerHost : IPlayerHost
{
    public Task<IHostedRun> StartAsync(string installDirectory, string executablePath, string arguments, CancellationToken cancellationToken)
    {
        string path = Path.Combine(installDirectory, executablePath);
        PlayerStartInfo info = new(path, arguments, Path.GetDirectoryName(path)!) { HiddenDesktop = true };

        return Task.FromResult<IHostedRun>(new LocalPlayerRun(Player.Start(info)));
    }
}

file sealed class LocalPlayerRun(Player player) : IHostedRun
{
    public int ExitCode => player.Process.ExitCode;

    public bool HasExited => player.HasExited;

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        return player.Process.WaitForExitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        player.Dispose();

        return ValueTask.CompletedTask;
    }
}
