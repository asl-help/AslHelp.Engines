using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents the input of the route search for 1 version.
/// </summary>
/// <param name="Version">The version.</param>
/// <param name="ExecutablePath">The file of the executable.</param>
/// <param name="Globals">The RVAs of each global, by global.</param>
internal sealed record RouteVersion(
    string Version,
    string ExecutablePath,
    IReadOnlyDictionary<string, IReadOnlyList<uint>> Globals);
