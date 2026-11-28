using AslHelp.Engines.Profiles;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Tests.Profiles;

/// <summary>
///     Provides the version scheme of the made-up engine, like <c>2022.3.5f1</c>, through public members of AslHelp.Engines only.
/// </summary>
/// <remarks>
///     A version is 2 to 4 numbers between dots. After the last number comes an optional release type and its increment.
///     The types sort a, b, f = c, p, x and become 1 part, so <c>2022.3.5f1</c> is 2022, 3, 5, 2, 1.
///     Text after the increment and a letter outside the types don't count.
/// </remarks>
internal static class FakeVersion
{
    /// <summary>
    ///     Reads a version of the made-up engine.
    /// </summary>
    /// <param name="text">The text, like <c>2022.3.5f1</c>.</param>
    /// <returns>
    ///     The version or <see langword="null"/> when <paramref name="text"/> doesn't start with 2 to 4 numbers between dots.
    /// </returns>
    public static ProfileVersion? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<int> parts = [];
        int at = 0;

        while (true)
        {
            int start = at;
            int number = ReadNumber(text, ref at);

            if (at == start)
            {
                return null;
            }

            parts.Add(number);

            if (parts.Count == 4 || at + 1 >= text.Length || text[at] != '.' || text[at + 1] is < '0' or > '9')
            {
                break;
            }

            at++;
        }

        if (parts.Count < 2)
        {
            return null;
        }

        if (at < text.Length && Rank(text[at]) is { } rank)
        {
            parts.Add(rank);
            at++;

            int start = at;
            int increment = ReadNumber(text, ref at);

            if (at > start)
            {
                parts.Add(increment);
            }
        }

        return new ProfileVersion(text, [.. parts]);
    }

    // Read a digit run past int.MaxValue as int.MaxValue, because a version read from game files can have any number of digits.
    private static int ReadNumber(string text, ref int at)
    {
        long number = 0;

        for (; at < text.Length && text[at] is >= '0' and <= '9'; at++)
        {
            number = number > int.MaxValue ? number : (number * 10) + (text[at] - '0');
        }

        return number > int.MaxValue ? int.MaxValue : (int)number;
    }

    private static int? Rank(char type)
    {
        return type switch
        {
            'a' => 0,
            'b' => 1,
            'f' or 'c' => 2,
            'p' => 3,
            'x' => 4,
            _ => null,
        };
    }
}
