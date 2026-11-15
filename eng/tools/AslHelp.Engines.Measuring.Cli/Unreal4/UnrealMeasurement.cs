using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4.Errors;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Reads the globals, the constants and the member offsets of an Unreal player out of its PDB and its executable.
/// </summary>
internal static class UnrealMeasurement
{
    /// <summary>
    ///     Measures the player whose PDB is <paramref name="pdb"/>.
    /// </summary>
    /// <param name="pdb">The PDB of the player.</param>
    /// <param name="image">The code of the executable of the player.</param>
    /// <param name="version">The engine version of the player, such as 4.19.2.</param>
    /// <param name="targets">The targets by name.</param>
    /// <param name="executablePath">The file of the executable of the player.</param>
    /// <param name="logger">The logger for the steps of the measurement.</param>
    /// <param name="cancellationToken">The token that stops the measurement before the next target.</param>
    /// <returns>
    ///     The values of the targets and members inside their ranges
    ///     or a failure when one of them is not found, 2 layouts of a type disagree on a value or the table of shapes starts after the version.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Result<PlayerMeasurement> Measure(
        PdbFile pdb,
        CodeImage image,
        Version version,
        IReadOnlyDictionary<string, UnrealTarget> targets,
        string executablePath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Parse the version once, because every target compares its range with it.
        var minor = UnrealVersions.MinorOf(version);

        if (!UnrealShapes.Of(minor).TryUnwrap(out var shapes, out var shapeError))
        {
            return shapeError;
        }

        Dictionary<string, IReadOnlyList<uint>> globals = [];
        Dictionary<string, int> offsets = [];
        int types = 0;

        foreach ((string name, var target) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!UnrealVersions.Within(minor, target.FromMinor, target.UntilMinor))
            {
                continue;
            }

            switch (target)
            {
                case UnrealGlobalTarget { ConstructedType: { } type } global
                    when UnrealVersions.Within(minor, global.ConstructedFromMinor, global.ConstructedUntilMinor):
                {
                    if (!UnrealStatics.FindThroughConstructor(pdb, image, type, cancellationToken).TryUnwrap(out uint rva, out var error))
                    {
                        return error;
                    }

                    globals[name] = [rva];
                    logger.StaticFound(name, rva);

                    break;
                }
                case UnrealGlobalTarget:
                {
                    if (!pdb.GlobalRva(name).TryUnwrap(out uint rva, out var error))
                    {
                        return error;
                    }

                    globals[name] = [rva];

                    break;
                }
                case UnrealFunctionStaticTarget function:
                {
                    if (!UnrealStatics.FindThroughFunction(pdb, image, function.Function).TryUnwrap(out uint rva, out var error))
                    {
                        return error;
                    }

                    globals[name] = [rva];
                    logger.StaticFound(name, rva);

                    break;
                }
                case UnrealConstantTarget:
                {
                    if (!pdb.ConstantValue(name).TryUnwrap(out long value, out var error))
                    {
                        return error;
                    }

                    if (value is < int.MinValue or > int.MaxValue)
                    {
                        return UnrealLayoutError.ConstantOutOfRange(name, value);
                    }

                    offsets[UnrealConstantTarget.ReportKey(name)!] = (int)value;

                    break;
                }
                case UnrealLayoutTarget layout:
                {
                    if (ReadLayout(pdb, name, layout, minor, offsets).TryUnwrapErr(out var error))
                    {
                        return error;
                    }

                    types++;

                    break;
                }
            }
        }

        logger.LayoutsRead(types, offsets.Count);

        // Report no unmeasured values, because a value inside its range fails the row when it isn't found.
        // Write the shapes as their spellings, because the report rows and groups only compare and write text.
        return new PlayerMeasurement(globals, offsets, new Dictionary<string, string>(), executablePath)
        {
            Shapes = shapes.ToDictionary(static shape => shape.Key, static shape => UnrealShapes.NameOf(shape.Value), StringComparer.Ordinal),
        };
    }

    /// <summary>
    ///     Reads the size and the members of a type, checked against every layout of the type in the PDB.
    /// </summary>
    /// <param name="pdb">The PDB of the player.</param>
    /// <param name="type">The name of the type.</param>
    /// <param name="target">The target of the type.</param>
    /// <param name="minor">The minor version of the player.</param>
    /// <param name="offsets">The offsets found so far.</param>
    /// <returns>
    ///     A failure when the type or a member inside its range is not found or the layouts disagree on a reported value.
    /// </returns>
    private static Result ReadLayout(
        PdbFile pdb,
        string type,
        UnrealLayoutTarget target,
        Version minor,
        Dictionary<string, int> offsets)
    {
        if (!pdb.FindLayouts(type).TryUnwrap(out var layouts, out var error))
        {
            return error;
        }

        uint[] sizes = [.. layouts.Select(static layout => layout.Size).Distinct()];

        // Compare only the reported values, because compile units complete a type with other members that a report doesn't use.
        if (sizes.Length > 1)
        {
            return UnrealLayoutError.LayoutsDiffer(type, UnrealLayoutTarget.SizeofMember, sizes);
        }

        offsets[$"{type}.{UnrealLayoutTarget.SizeofMember}"] = (int)sizes[0];

        foreach ((string member, var range) in target.Members)
        {
            if (!UnrealVersions.Within(minor, range.FromMinor, range.UntilMinor))
            {
                continue;
            }

            string pdbName = range.PdbName(member, minor);
            var found = layouts.Select(layout => layout.Member(pdbName)).ToList();

            if (found.Contains(null))
            {
                return UnrealLayoutError.MemberNotFound(type, pdbName);
            }

            uint[] memberOffsets = [.. found.Select(static item => item!.Offset).Distinct()];

            if (memberOffsets.Length > 1)
            {
                return UnrealLayoutError.LayoutsDiffer(type, member, memberOffsets);
            }

            offsets[$"{type}.{member}"] = (int)memberOffsets[0];
        }

        return Result.Ok();
    }
}
