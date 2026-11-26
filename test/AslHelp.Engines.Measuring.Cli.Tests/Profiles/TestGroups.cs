using AslHelp.Engines.Measuring.Cli.Routes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Tests.Profiles;

// Builds the groups and routes of a report, because the profile tests start from a finished report.
internal static class TestGroups
{
    public const string Forward = "8B 0D ????????";

    public static VersionGroup Group(string? key, List<string> versions, params (string Global, GlobalRoutes Routes)[] globals)
    {
        return new VersionGroup(
            key,
            versions,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["Type"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["Member"] = "0x28" },
            },
            null,
            globals.ToDictionary(static global => global.Global, static global => global.Routes, StringComparer.Ordinal));
    }

    public static Route Route(params string[] versions)
    {
        return new Route(Forward, 2, versions, versions.ToDictionary(static version => version, static _ => "0x2000", StringComparer.Ordinal));
    }

    public static GlobalRoutes RoutesOf(params IReadOnlyList<Route> routes)
    {
        return new GlobalRoutes(routes, null);
    }
}
