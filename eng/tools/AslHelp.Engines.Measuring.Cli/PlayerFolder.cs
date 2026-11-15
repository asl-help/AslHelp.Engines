using AslHelp.Engines.Fixtures;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Describes a player folder that the fixtures command line built.
/// </summary>
/// <param name="Directory">The full path of the folder.</param>
/// <param name="Version">The version of the engine.</param>
internal sealed record PlayerFolder(
    string Directory,
    string Version)
{
    private static readonly SearchValues<char> _hashChars = SearchValues.Create("0123456789abcdef");
    private static readonly StringComparer _versionOrder = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering);

    /// <summary>
    ///     Finds the <c>&lt;engine&gt;-&lt;version&gt;-&lt;hash&gt;</c> folders in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The folder with the players.</param>
    /// <param name="engine">The name of the engine.</param>
    /// <returns>
    ///     The player folders in natural order of their versions, with 4.9 before 4.10.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directory"/> does not exist.</exception>
    public static IReadOnlyList<PlayerFolder> Find(string directory, string engine)
    {
        return
        [
            .. System.IO.Directory.EnumerateDirectories(Path.GetFullPath(directory))
                .Select(folder => TryParse(folder, engine))
                .OfType<PlayerFolder>()
                .OrderBy(static player => player.Version, _versionOrder)
                .ThenBy(static player => player.Version, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    ///     Reads the version out of the name of <paramref name="folder"/>.
    /// </summary>
    /// <param name="folder">The full path of the folder.</param>
    /// <param name="engine">The name of the engine.</param>
    /// <returns>
    ///     The player folder or <see langword="null"/> if the name does not fit.
    /// </returns>
    private static PlayerFolder? TryParse(string folder, string engine)
    {
        string name = Path.GetFileName(folder);
        string prefix = $"{engine}-";
        int hashAt = name.Length - CacheKey.ShortHashLength;

        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || hashAt - 1 <= prefix.Length
            || name[hashAt - 1] != '-'
            || name.AsSpan(hashAt).ContainsAnyExcept(_hashChars))
        {
            return null;
        }

        return new PlayerFolder(folder, name[prefix.Length..(hashAt - 1)]);
    }
}
