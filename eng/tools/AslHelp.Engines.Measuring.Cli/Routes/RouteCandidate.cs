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
    int Bytes)
{
    /// <summary>
    ///     Gets the number of whole bytes that the mask keeps, which a match has to equal.
    /// </summary>
    /// <remarks>
    ///     A byte with 1 masked half, like <c>4?</c>, doesn't count.
    /// </remarks>
    public int Literals
    {
        get
        {
            int literals = 0;
            int digits = 0;
            bool kept = true;

            foreach (char digit in Forward)
            {
                if (digit == ' ')
                {
                    continue;
                }

                kept &= digit != '?';

                if (++digits % 2 == 0)
                {
                    literals += kept ? 1 : 0;
                    kept = true;
                }
            }

            return literals;
        }
    }
}
