using System;
using System.Globalization;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Formats a timeout for a message in the largest whole unit, such as <c>90 min</c> or <c>30 s</c>.
/// </summary>
internal static class DurationText
{
    /// <summary>
    ///     Formats <paramref name="duration"/> in minutes when it is a whole number of minutes and in seconds otherwise.
    /// </summary>
    /// <param name="duration">The duration, such as a timeout.</param>
    /// <returns>
    ///     The text, such as <c>90 min</c>, <c>30 s</c> or <c>0.5 s</c>.
    /// </returns>
    public static string Format(TimeSpan duration)
    {
        // Prints seconds for a timeout under a minute or with seconds left over, because rounding would print a wrong limit.
        if (duration >= TimeSpan.FromMinutes(1) && duration.Ticks % TimeSpan.TicksPerMinute == 0)
        {
            return SR.FormatDurationMinutes(((long)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture));
        }

        // Prints every digit down to the tick, because a rounded fraction such as 59.9996 would print as 60.
        decimal seconds = (decimal)duration.Ticks / TimeSpan.TicksPerSecond;

        return SR.FormatDurationSeconds(seconds.ToString("0.#######", CultureInfo.InvariantCulture));
    }
}
