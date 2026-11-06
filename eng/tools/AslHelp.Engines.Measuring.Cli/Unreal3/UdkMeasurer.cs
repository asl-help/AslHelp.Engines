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
    public Result LoadTargets(string? path)
    {
        var read = path is null ? UdkTargets.ReadEmbedded() : UdkTargets.ReadFile(path);

        if (!read.TryUnwrap(out var targets, out var error))
        {
            return error;
        }

        Targets = targets;

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result<PlayerMeasurement>> MeasureAsync(string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        using var player = await UdkPlayer.StartAsync(playerDirectory, cancellationToken).ConfigureAwait(false);
        using RemoteProcessMemory memory = new(player.Process);

        var startup = Stopwatch.GetElapsedTime(started);
        logger.ServerReady(startup.TotalSeconds);

        var measurement = Measure(memory, playerDirectory, logger);

        // Fail the row, because the members of a server that dies during the searches end up in unmeasured with the wrong reason.
        // Put the exit first, because a dead server does not have a module and its sections read as zeros, which fail the search.
        return player.Process.HasExited
            ? UdkError.PlayerExitedDuringMeasurement(player.Process.ExitCode, measurement.Error)
            : measurement;
    }

    private Result<PlayerMeasurement> Measure(RemoteProcessMemory memory, string playerDirectory, ILogger logger)
    {
        if (!memory.GetModule(UdkInstall.ExecutableName).TryUnwrap(out var module, out var moduleError))
        {
            return UdkError.ModuleNotLoaded(UdkInstall.ExecutableName, moduleError);
        }

        var moduleBase = module.Range.Start;
        string executablePath = Path.Combine(playerDirectory, UdkInstall.ServerPath);

        if (!UdkSections.Read(executablePath, memory, moduleBase).TryUnwrap(out var sections, out var error))
        {
            return error;
        }

        int sectionBytes = sections.Sum(static section => section.Bytes.Length);
        logger.SectionsRead(sections.Count, sectionBytes);

        return UdkMeasurement.Measure(memory, moduleBase, sections, Targets, executablePath, logger);
    }
}
