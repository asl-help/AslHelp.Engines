using AslHelp.Engines.Fixtures.Unreal3;
using AslHelp.Memory;
using AslHelp.Memory.Remote;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>
    ///     Gets the globals to find, which <see cref="LoadTargets"/> sets.
    /// </summary>
    public IReadOnlyDictionary<string, UdkTarget> Targets { get; private set; } = new Dictionary<string, UdkTarget>();

    /// <inheritdoc/>
    public void LoadTargets(string? path)
    {
        Targets = path is null ? UdkTargets.ReadEmbedded() : UdkTargets.ReadFile(path);
    }

    /// <inheritdoc/>
    public async Task<PlayerMeasurement> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        using var player = await UdkPlayer.StartAsync(playerDirectory, cancellationToken).ConfigureAwait(false);
        using RemoteProcessMemory memory = new(player.Process);

        var startup = Stopwatch.GetElapsedTime(started);
        logger.ServerReady(startup.TotalSeconds);

        try
        {
            if (!memory.GetModule(UdkInstall.ExecutableName).TryUnwrap(out var module, out var error))
            {
                throw new InvalidOperationException(UdkSR.FormatModuleNotLoaded(UdkInstall.ExecutableName, error.Message));
            }

            var moduleBase = module.Range.Start;
            string executablePath = Path.Combine(playerDirectory, UdkInstall.ServerPath);
            var sections = UdkSections.Read(executablePath, memory, moduleBase);

            int sectionBytes = sections.Sum(static section => section.Bytes.Length);
            logger.SectionsRead(sections.Count, sectionBytes);

            var measurement = UdkMeasurement.Measure(memory, moduleBase, sections, Targets, executablePath, logger);

            // Fail the row, because the members of a server that dies during the searches end up in unmeasured with the wrong reason.
            return player.Process.HasExited
                ? throw new InvalidOperationException(UdkSR.FormatPlayerExitedDuringMeasurement(player.Process.ExitCode))
                : measurement;
        }
        catch (InvalidOperationException exception) when (player.Process.HasExited)
        {
            // Replace the failure, because a dead server has no module and its sections read as zeros, which fail the search.
            throw new InvalidOperationException(UdkSR.FormatPlayerExitedDuringMeasurement(player.Process.ExitCode), exception);
        }
    }
}
