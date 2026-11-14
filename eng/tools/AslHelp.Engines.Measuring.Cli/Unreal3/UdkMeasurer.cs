using AslHelp.Engines.Fixtures.Unreal3;
using AslHelp.Engines.Measuring.Cli.Unreal3.Errors;
using AslHelp.Memory;
using AslHelp.Memory.Remote;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
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

    /// <inheritdoc/>
    public IReadOnlyList<Option> Options => [];

    /// <inheritdoc/>
    /// <remarks>
    ///     Measures 1 player at a time, because each player runs a server under the machine-wide lock.
    /// </remarks>
    public int MaxDegreeOfParallelism => 1;

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
    public IReadOnlyList<PlayerFolder> SelectPlayers(IReadOnlyList<PlayerFolder> players, ParseResult parseResult, ILogger logger)
    {
        return players;
    }

    /// <inheritdoc/>
    public string? GroupOf(string version)
    {
        return null;
    }

    /// <inheritdoc/>
    public bool IsNeeded(string type, string member)
    {
        return true;
    }

    /// <inheritdoc/>
    public async Task<Result<PlayerMeasurement>> MeasureAsync(PlayerFolder player, ILogger logger, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        using var server = await UdkPlayer.StartAsync(player.Directory, cancellationToken).ConfigureAwait(false);
        using RemoteProcessMemory memory = new(server.Process);

        var startup = Stopwatch.GetElapsedTime(started);
        logger.ServerReady(startup.TotalSeconds);

        var measurement = Measure(memory, player.Directory, logger, cancellationToken);

        // Fail the row, because the members of a server that dies during the searches end up in unmeasured with the wrong reason.
        // Put the exit first, because a dead server does not have a module and its sections read as zeros, which fail the search.
        return server.Process.HasExited
            ? UdkPlayerError.PlayerExitedDuringMeasurement(server.Process.ExitCode, measurement.Error)
            : measurement;
    }

    private Result<PlayerMeasurement> Measure(RemoteProcessMemory memory, string playerDirectory, ILogger logger, CancellationToken cancellationToken)
    {
        if (!memory.GetModule(UdkInstall.ExecutableName).TryUnwrap(out var module, out var moduleError))
        {
            return UdkPlayerError.ModuleNotLoaded(UdkInstall.ExecutableName, moduleError);
        }

        var moduleBase = module.Range.Start;
        string executablePath = Path.Combine(playerDirectory, UdkInstall.ServerPath);

        if (!UdkSections.Read(executablePath, memory, moduleBase).TryUnwrap(out var sections, out var error))
        {
            return error;
        }

        int sectionBytes = sections.Sum(static section => section.Bytes.Length);
        logger.SectionsRead(sections.Count, sectionBytes);

        return UdkMeasurement.Measure(memory, moduleBase, sections, Targets, executablePath, logger, cancellationToken);
    }
}
