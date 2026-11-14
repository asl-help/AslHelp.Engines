using AslHelp.Engines.Measuring.Cli.Routes;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Provides the grouping of versions by their offsets.
/// </summary>
internal static class VersionGroups
{
    /// <summary>
    ///     Groups the measured rows with equal keys, equal shapes and equal needed offsets, which means the same types, members and values.
    ///     A failed row or a row without needed offsets and without globals does not join a group and every other row joins exactly 1.
    /// </summary>
    /// <param name="rows">The rows of the players in the order of the run.</param>
    /// <param name="groupOf">Gets the key of a version or <see langword="null"/> if it doesn't have a key.</param>
    /// <param name="isNeeded">Checks whether an offset, by type and member, decides the groups.</param>
    /// <returns>
    ///     The groups in order of their 1st version, with the versions of each group in the order of <paramref name="rows"/>,
    ///     the key, the needed offsets and the shapes of its 1st row and no routes.
    /// </returns>
    public static IReadOnlyList<VersionGroup> Find(
        IReadOnlyList<PlayerRow> rows,
        Func<string, string?> groupOf,
        Func<string, string, bool> isNeeded)
    {
        List<VersionGroup> groups = [];

        foreach (var row in rows)
        {
            if (row.Offsets is not { } rowOffsets)
            {
                continue;
            }

            var neededOffsets = Needed(rowOffsets, isNeeded);

            // Keep a row with only globals, because the route search runs per group and a list of targets can leave out every type.
            if (neededOffsets.Count == 0 && row.Globals is not { Count: > 0 })
            {
                continue;
            }

            string? key = groupOf(row.Version);

            var group = groups.Find(existing => existing.Key == key
                && SameShapes(existing.Shapes, row.Shapes)
                && SameOffsets(existing.Offsets, neededOffsets));

            if (group is null)
            {
                groups.Add(new VersionGroup(key, [row.Version], neededOffsets, row.Shapes, new Dictionary<string, GlobalRoutes>()));
            }
            else
            {
                group.Versions.Add(row.Version);
            }
        }

        return groups;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Needed(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> offsets,
        Func<string, string, bool> isNeeded)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> needed = [with(StringComparer.Ordinal)];

        foreach ((string type, var members) in offsets)
        {
            Dictionary<string, string> neededMembers = [with(StringComparer.Ordinal)];

            foreach ((string member, string value) in members)
            {
                if (isNeeded(type, member))
                {
                    neededMembers[member] = value;
                }
            }

            // Leave out a type without needed members, so a size-only type from a later version doesn't split a group.
            if (neededMembers.Count > 0)
            {
                needed[type] = neededMembers;
            }
        }

        return needed;
    }

    private static bool SameOffsets(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> left,
        Dictionary<string, IReadOnlyDictionary<string, string>> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string type, var leftMembers) in left)
        {
            if (!right.TryGetValue(type, out var rightMembers) || !SameValues(leftMembers, rightMembers))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameShapes(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        return left is null || right is null ? left == right : SameValues(left, right);
    }

    private static bool SameValues(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? rightValue) || value != rightValue)
            {
                return false;
            }
        }

        return true;
    }
}
