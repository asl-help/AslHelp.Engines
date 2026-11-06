namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents a masked signature that may become a route.
/// </summary>
/// <param name="Forward">The masked bytes as text, as in <c>8B 0D ????????</c>.</param>
/// <param name="Offset">The number of bytes from the 1st byte to the address operand.</param>
/// <param name="Instructions">The number of whole instructions.</param>
/// <param name="Bytes">The number of bytes.</param>
internal sealed record RouteCandidate(
    string Forward,
    int Offset,
    int Instructions,
    int Bytes);
