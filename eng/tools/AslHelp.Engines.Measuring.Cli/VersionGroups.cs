using AslHelp.Engines.Measuring.Cli.Routes;
using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Provides the grouping of versions by their offsets.
/// </summary>
internal static class VersionGroups
{
    /// <summary>
    ///     Groups the measured rows by equal offsets, with the same types, members and values.
    ///     A failed row or a row with empty offsets does not join a group and every other row joins exactly 1.
    /// </summary>
    /// <param name="rows">The rows of the players in the order of the run.</param>
    /// <returns>
    ///     The groups in order of their 1st version, with the versions of each group in the order of <paramref name="rows"/>
    ///     and no routes.
    /// </returns>
    public static IReadOnlyList<VersionGroup> Find(IReadOnlyList<PlayerRow> rows)
    {
        List<VersionGroup> groups = [];

        foreach (var row in rows)
        {
            if (row.Offsets is not { Count: > 0 } rowOffsets)
            {
                continue;
            }

            var group = groups.Find(existing => SameOffsets(existing.Offsets, rowOffsets));

            if (group is null)
            {
                groups.Add(new VersionGroup([row.Version], rowOffsets, new Dictionary<string, GlobalRoutes>()));
            }
            else
            {
                group.Versions.Add(row.Version);
            }
        }

        return groups;
    }

    private static bool SameOffsets(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> left,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string type, var leftMembers) in left)
        {
            if (!right.TryGetValue(type, out var rightMembers) || leftMembers.Count != rightMembers.Count)
            {
                return false;
            }

            foreach ((string member, string offset) in leftMembers)
            {
                if (!rightMembers.TryGetValue(member, out string? rightOffset) || offset != rightOffset)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
