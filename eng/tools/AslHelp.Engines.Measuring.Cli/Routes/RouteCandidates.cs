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
        if (tier == MaskTier.Register && window.RegisterAt < 0)
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
                int registerAt = tier == MaskTier.Register ? window.RegisterAt : -1;

                yield return new RouteCandidate(Render(window, start, end, registerAt), window.OperandAt - start, instructions, end - start);
            }
        }
    }

    private static string Render(SiteWindow window, int start, int end, int registerAt)
    {
        StringBuilder text = new();

        for (int position = start; position < end; position++)
        {
            if (text.Length > 0)
            {
                _ = text.Append(' ');
            }

            int operand = window.Operands[position];

            if (position == registerAt)
            {
                _ = text.Append("??");
            }
            else if (operand == 0)
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"{window.Bytes[position]:X2}");
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
