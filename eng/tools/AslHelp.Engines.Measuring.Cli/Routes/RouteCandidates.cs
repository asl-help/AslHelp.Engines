using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Builds the candidates of a site window.
/// </summary>
internal static class RouteCandidates
{
    /// <summary>
    ///     Represents the default of the fewest bytes a route keeps unmasked: 6, so a random match in the writable memory of
    ///     a module, which a search at run time also reads, is about 1 in 5 million per search of 50 MB.
    /// </summary>
    public const int DefaultLeastLiterals = 6;

    /// <summary>
    ///     Represents the largest number of bytes of a candidate.
    /// </summary>
    public const int MostBytes = 64;

    /// <summary>
    ///     Represents the largest number of candidates that the search tries for 1 instruction count.
    /// </summary>
    public const int MostCandidates = 64;

    /// <summary>
    ///     Represents the largest number of instructions of a candidate.
    /// </summary>
    public const int MostInstructions = (2 * MostNeighbors) + 1;

    /// <summary>
    ///     Represents the largest number of instructions of a candidate on each side of the site.
    /// </summary>
    public const int MostNeighbors = 8;

    /// <summary>
    ///     Lists the candidates of <paramref name="window"/> with exactly <paramref name="instructions"/> instructions.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="tier">The bytes to mask.</param>
    /// <param name="instructions">The number of instructions.</param>
    /// <returns>
    ///     The candidates in order of their instructions before the site.
    /// </returns>
    public static IEnumerable<RouteCandidate> Enumerate(SiteWindow window, MaskTier tier, int instructions)
    {
        // Skip a tier that masks nothing more than the tier before it, because its candidates would repeat.
        if ((tier != MaskTier.Strict && window.RegisterAt < 0) || (tier == MaskTier.Prefix && window.PrefixAt < 0))
        {
            yield break;
        }

        int neighbors = instructions - 1;

        for (int before = 0; before <= int.Min(neighbors, MostNeighbors); before++)
        {
            int after = neighbors - before;
            int first = window.Site - before;
            int last = window.Site + after;

            if (after > MostNeighbors || first < 0 || last >= window.Starts.Length)
            {
                continue;
            }

            int start = window.Starts[first];
            int end = last + 1 < window.Starts.Length ? window.Starts[last + 1] : window.Bytes.Length;

            if (end - start <= MostBytes)
            {
                yield return new RouteCandidate(Render(window, tier, start, end), window.OperandAt - start, instructions, end - start);
            }
        }
    }

    private static string Render(SiteWindow window, MaskTier tier, int start, int end)
    {
        StringBuilder text = new();

        for (int position = start; position < end; position++)
        {
            if (text.Length > 0)
            {
                _ = text.Append(' ');
            }

            int operand = window.Operands[position];
            byte value = window.Bytes[position];

            // Mask the low nibble of the REX prefix, because it has the register extension bit.
            // The nibble tier masks the high nibble of the ModRM byte, so it also loses mod, because that nibble has mod and register bits 2 and 1.
            // It keeps the low nibble, because that has register bit 0 and the rm field, and the scan refuses a match with another mod.
            if (tier != MaskTier.Strict && position == window.PrefixAt)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"{value >> 4:X}?");
            }
            else if (tier == MaskTier.Nibble && position == window.RegisterAt)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"?{value & 0xF:X}");
            }
            else if (tier == MaskTier.Register && position == window.RegisterAt)
            {
                _ = text.Append("??");
            }
            else if (operand == 0)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"{value:X2}");
            }
            else
            {
                // Write 1 token per operand, so the text of 2 operands next to each other shows both.
                while (position < end && window.Operands[position] == operand)
                {
                    _ = text.Append("??");
                    position++;
                }

                position--;
            }
        }

        return text.ToString();
    }
}
