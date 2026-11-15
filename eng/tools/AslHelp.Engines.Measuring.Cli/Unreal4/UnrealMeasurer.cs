using AslHelp.Engines.Fixtures.Unreal4;
using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4.Errors;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Measures the players of Unreal Engine 4 and 5 from their PDBs and executables, without starting them.
/// </summary>
internal sealed class UnrealMeasurer : IEngineMeasurer
{
    private readonly Option<Architecture?> _archOption = UnrealPlayerOptions.Arch(UnrealSR.ArchDescription);
    private readonly Option<UnrealConfig?> _configOption = UnrealPlayerOptions.Config(UnrealSR.ConfigDescription);

    // Keeps the scope and the name of each constant, because the grouping checks every offset of every row and a joined name
    // would allocate each time.
    private readonly HashSet<(string Scope, string Member)> _constantKeys = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealMeasurer"/> class.
    /// </summary>
    public UnrealMeasurer()
    {
        Options = [_archOption, _configOption];
    }

    /// <inheritdoc/>
    public string Name => "unreal4";

    /// <inheritdoc/>
    public IReadOnlyList<Option> Options { get; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Measures 4 players at a time, because a player only reads files and keeps its PDB and its code in memory, about 1 GB each.
    /// </remarks>
    public int MaxDegreeOfParallelism => 4;

    /// <summary>
    ///     Gets the values to find, which <see cref="LoadTargets"/> sets.
    /// </summary>
    public IReadOnlyDictionary<string, UnrealTarget> Targets { get; private set; } = new Dictionary<string, UnrealTarget>();

    /// <inheritdoc/>
    public Result LoadTargets(string? path)
    {
        var read = UnrealTargets.Read(path);

        if (!read.TryUnwrap(out var targets, out var error))
        {
            return error;
        }

        Targets = targets;
        _constantKeys.Clear();

        foreach ((string name, var target) in targets)
        {
            if (target is UnrealConstantTarget && CppName.TrySplitScope(name, out string? scope, out string? member))
            {
                _ = _constantKeys.Add((scope, member));
            }
        }

        return Result.Ok();
    }

    /// <inheritdoc/>
    public IReadOnlyList<PlayerFolder> SelectPlayers(IReadOnlyList<PlayerFolder> players, ParseResult parseResult, ILogger logger)
    {
        var arch = parseResult.GetValue(_archOption);
        var config = parseResult.GetValue(_configOption);
        List<PlayerFolder> selected = [];

        foreach (var player in players)
        {
            if (!UnrealPlayer.TryParseId(player.Version, out _, out var parsed))
            {
                logger.PlayerSkipped(player.Version, UnrealSR.FormatPlayerIdInvalid(player.Version));
            }
            else if ((arch is null || parsed.Arch == arch) && (config is null || parsed.Config == config))
            {
                selected.Add(player);
            }
        }

        return selected;
    }

    /// <inheritdoc/>
    public string? GroupOf(string version)
    {
        return UnrealPlayer.TryParseId(version, out _, out var player) ? player.Tag : null;
    }

    /// <inheritdoc/>
    public bool IsNeeded(string type, string member)
    {
        // Count every constant, because a reader uses a constant to index what the offsets lead to.
        return (Targets.TryGetValue(type, out var target) && target is UnrealLayoutTarget layout && layout.Needs(member))
            || _constantKeys.Contains((type, member));
    }

    /// <inheritdoc/>
    public Task<Result<PlayerMeasurement>> MeasureAsync(PlayerFolder player, ILogger logger, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Measure(player, logger, cancellationToken));
    }

    private Result<PlayerMeasurement> Measure(PlayerFolder player, ILogger logger, CancellationToken cancellationToken)
    {
        if (!UnrealPlayer.TryParseId(player.Version, out var version, out var parsed))
        {
            return UnrealPlayerError.PlayerIdInvalid(player.Version);
        }

        string executablePath = parsed.ExecutablePath(player.Directory, version);
        string pdbPath = parsed.PdbPath(player.Directory, version);

        if (!File.Exists(executablePath))
        {
            return UnrealPlayerError.ExecutableNotFound(executablePath);
        }

        if (!File.Exists(pdbPath))
        {
            return UnrealPlayerError.PdbNotFound(pdbPath);
        }

        if (!ExecutableIdentity.Read(executablePath).TryUnwrap(out var identity, out var error))
        {
            return error;
        }

        if (!PdbFile.Open(pdbPath).TryUnwrap(out var pdb, out error))
        {
            return error;
        }

        using (pdb)
        {
            logger.PdbRead(pdbPath);

            if (pdb.Identity.Check(identity).TryUnwrapErr(out error))
            {
                return error;
            }

            // Check the token after the PDB read, because the read takes seconds on a large PDB and cannot stop halfway.
            cancellationToken.ThrowIfCancellationRequested();

            if (!CodeImage.Read(executablePath).TryUnwrap(out var image, out error))
            {
                return error;
            }

            using (image)
            {
                return UnrealMeasurement.Measure(pdb, image, version, Targets, executablePath, logger, cancellationToken);
            }
        }
    }
}
