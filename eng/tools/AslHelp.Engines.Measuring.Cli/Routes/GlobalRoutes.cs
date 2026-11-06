using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents the routes of 1 global in a group of versions.
/// </summary>
/// <param name="Routes">The routes in the order that the search picked them.</param>
/// <param name="Uncovered">The reason for each version that no route covers or <see langword="null"/> if every version is covered.</param>
internal sealed record GlobalRoutes(
    IReadOnlyList<Route> Routes,
    IReadOnlyDictionary<string, string>? Uncovered);
