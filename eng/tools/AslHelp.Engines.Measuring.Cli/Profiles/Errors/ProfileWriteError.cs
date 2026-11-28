using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Profiles.Errors;

/// <summary>
///     Represents profile documents that can't be written.
/// </summary>
internal abstract record ProfileWriteError : ResultError
{
    /// <summary>
    ///     Creates the error for 2 groups whose documents get the same file name.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="firstVersions">The versions of the 1st group.</param>
    /// <param name="otherVersions">The versions of the other group.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileWriteError SameFileName(string fileName, IReadOnlyList<string> firstVersions, IReadOnlyList<string> otherVersions)
    {
        return new SameFileNameError(fileName, firstVersions, otherVersions);
    }

    /// <summary>
    ///     Creates the error for 2 routes of 1 global that get the same name.
    /// </summary>
    /// <param name="global">The name of the global.</param>
    /// <param name="routeName">The name of both routes.</param>
    /// <param name="versions">The versions of the group.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileWriteError SameRouteName(string global, string routeName, IReadOnlyList<string> versions)
    {
        return new SameRouteNameError(global, routeName, versions);
    }
}

file sealed record SameFileNameError(
    string FileName,
    IReadOnlyList<string> FirstVersions,
    IReadOnlyList<string> OtherVersions) : ProfileWriteError
{
    public override string Message => SR.FormatProfileSameFileName(FileName, string.Join(", ", FirstVersions), string.Join(", ", OtherVersions));
}

file sealed record SameRouteNameError(
    string Global,
    string RouteName,
    IReadOnlyList<string> Versions) : ProfileWriteError
{
    public override string Message => SR.FormatProfileSameRouteName(Global, string.Join(", ", Versions), RouteName);
}
