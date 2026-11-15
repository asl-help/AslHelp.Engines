using System;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Compares the versions of players with the ranges of <c>targets.json</c>.
/// </summary>
internal static class UnrealVersions
{
    /// <summary>
    ///     Checks that a minor version sits in a range of minor versions, with both ends in the range.
    /// </summary>
    /// <param name="minor">The minor version of the player, such as 4.19.</param>
    /// <param name="from">The 1st minor version or <see langword="null"/> if every earlier version is in the range.</param>
    /// <param name="until">The last minor version or <see langword="null"/> if every later version is in the range.</param>
    /// <returns>
    ///     <see langword="true"/> if <paramref name="minor"/> is in the range.
    /// </returns>
    public static bool Within(Version minor, Version? from, Version? until)
    {
        return (from is null || from <= minor) && (until is null || minor <= until);
    }

    /// <summary>
    ///     Reads the minor version of a player version.
    /// </summary>
    /// <param name="version">The version of the player, such as 4.19.2.</param>
    /// <returns>
    ///     The major and minor parts, such as 4.19.
    /// </returns>
    public static Version MinorOf(Version version)
    {
        return new Version(version.Major, version.Minor);
    }

    /// <summary>
    ///     Reads an end of a range of <c>targets.json</c>, which has only a major and a minor part.
    /// </summary>
    /// <remarks>
    ///     Refuses a 3rd part, because a range compares whole minor versions and 4.19.2 would leave out 4.19.0.
    /// </remarks>
    /// <param name="text">The text, such as 4.19, with <see langword="null"/> for an open end.</param>
    /// <returns>
    ///     The minor version or <see langword="null"/> if <paramref name="text"/> is <see langword="null"/> or not a minor version.
    /// </returns>
    public static Version? ParseMinor(string? text)
    {
        return Version.TryParse(text, out var minor) && minor.Build < 0 ? minor : null;
    }
}
