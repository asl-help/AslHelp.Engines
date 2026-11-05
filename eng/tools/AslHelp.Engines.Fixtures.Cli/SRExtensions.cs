using System;
using System.Globalization;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Adds the format helper to the generated <see cref="SR"/> class, which cannot take members of its own.
/// </summary>
internal static class SRExtensions
{
    extension(SR)
    {
        /// <summary>
        ///     Fills the placeholders of the message <paramref name="format"/> with <paramref name="args"/> in the current culture.
        /// </summary>
        /// <param name="format">The message from <see cref="SR"/>, with placeholders such as <c>{0}</c>.</param>
        /// <param name="args">The values for the placeholders, in order.</param>
        /// <returns>
        ///     The message with every placeholder filled.
        /// </returns>
        public static string Format(string format, params ReadOnlySpan<object?> args)
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
    }
}
