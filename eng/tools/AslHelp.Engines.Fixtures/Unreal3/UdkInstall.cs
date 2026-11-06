using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Fixtures.Unreal3;

/// <summary>
///     Defines the paths and the server command line of a UDK install, which the command lines that run UDK share.
/// </summary>
public static class UdkInstall
{
    /// <summary>
    ///     Represents the folder of <c>UDK.exe</c> and the DLLs it loads, relative to the install.
    /// </summary>
    public const string BinariesPath = @"Binaries\Win32";

    /// <summary>
    ///     Represents the file name of the engine executable in <see cref="BinariesPath"/>.
    /// </summary>
    public const string ExecutableName = "UDK.exe";

    /// <summary>
    ///     Represents the path of the log the engine writes, relative to the install.
    /// </summary>
    public const string LogPath = @"UDKGame\Logs\Launch.log";

    /// <summary>
    ///     Represents the server command line, with the map as <c>{0}</c>.
    /// </summary>
    public const string ServerCommand = $"{ServerPath} {ServerArgumentsFormat}";

    /// <summary>
    ///     Represents the path of <c>UDK.exe</c>, relative to the install.
    /// </summary>
    public const string ServerPath = $@"{BinariesPath}\{ExecutableName}";

    private const string MapPattern = "*.udk";
    private const string MapsPath = @"UDKGame\Content\Maps";

    /// <summary>
    ///     Represents the server arguments, with the map as <c>{0}</c>.
    /// </summary>
    /// <remarks>
    ///     The server renders nothing, so the fixture game runs on it. <c>-FORCELOGFLUSH</c> flushes every log line, so
    ///     a poll of the log reads a line as soon as it is logged.
    /// </remarks>
    private const string ServerArgumentsFormat = "server {0}?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH";

    private static readonly CompositeFormat _serverArguments = CompositeFormat.Parse(ServerArgumentsFormat);

    /// <summary>
    ///     Creates the server arguments that load <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The map, as <see cref="FindMap"/> returns it.</param>
    /// <returns>
    ///     The arguments.
    /// </returns>
    public static string ServerArguments(string map)
    {
        return string.Format(CultureInfo.InvariantCulture, _serverArguments, map);
    }

    /// <summary>
    ///     Gets the name of the 1st map under <c>UDKGame/Content/Maps</c> of <paramref name="installDirectory"/> in ordinal
    ///     order of its relative path.
    /// </summary>
    /// <param name="installDirectory">The install.</param>
    /// <returns>
    ///     The map file name without its extension, as the server takes it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The install doesn't have a <c>.udk</c> file under <c>UDKGame/Content/Maps</c>.</exception>
    public static string FindMap(string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        string mapsDirectory = Path.Combine(installDirectory, MapsPath);
        string? first = Directory.Exists(mapsDirectory)
            ? Directory.EnumerateFiles(mapsDirectory, MapPattern, SearchOption.AllDirectories)
                .Select(map => Path.GetRelativePath(mapsDirectory, map))
                .Order(StringComparer.Ordinal)
                .FirstOrDefault()
            : null;

        return first is null
            ? throw new FileNotFoundException(SR.FormatMapNotFound(MapPattern, mapsDirectory), mapsDirectory)
            : Path.GetFileNameWithoutExtension(first);
    }
}
