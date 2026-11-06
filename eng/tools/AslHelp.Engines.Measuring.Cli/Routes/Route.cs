using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents a signature route that several versions share.
/// </summary>
/// <param name="Forward">The masked bytes of the search step.</param>
/// <param name="Offset">The number of bytes from a match to the address operand.</param>
/// <param name="Versions">The versions that the route covers, in the order of the group.</param>
/// <param name="Reached">The RVA in hex that the route reaches, by version.</param>
internal sealed record Route(
    string Forward,
    int Offset,
    IReadOnlyList<string> Versions,
    IReadOnlyDictionary<string, string> Reached);
