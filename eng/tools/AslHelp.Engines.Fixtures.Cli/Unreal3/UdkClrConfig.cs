using AslHelp.Engines.Fixtures.Unreal3;
using System;
using System.IO;
using System.Xml.Linq;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Moves the executables of an old UDK install from CLR 2 to CLR 4 through their config files.
/// </summary>
/// <remarks>
///     <c>UDK.exe</c> and <c>UDK.com</c> of the installs with the Visual C++ 2008 redist target CLR 2, which Windows Server
///     does not ship. Without the config <c>UDK.exe</c> exits with 0xC0000142 and <c>UDK.com</c> with 0x80131700.
/// </remarks>
internal static class UdkClrConfig
{
    private const string CompilerConfigFileName = "UDK.com.config";
    private const string RootName = "configuration";
    private const string StartupName = "startup";

    /// <summary>
    ///     Adds the startup section with CLR 4 to the config files of <c>UDK.exe</c> and <c>UDK.com</c>.
    /// </summary>
    /// <remarks>
    ///     Skips an install without the Visual C++ 2008 redist because it runs on CLR 4 as shipped. Skips a file that has the
    ///     section too. Each file goes to a <c>.partial</c> file first and then moves to its name, so a write cut short by a
    ///     killed run never replaces it.
    /// </remarks>
    /// <param name="installDirectory">The extracted install.</param>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">A write or a move fails.</exception>
    /// <exception cref="InvalidDataException">The redist doesn't have a zip in it or has a damaged one.</exception>
    /// <exception cref="System.Xml.XmlException">A config file is not XML.</exception>
    public static void Add(string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        if (UdkRedist.FindPath(installDirectory) is not { } redistPath)
        {
            return;
        }

        using (var redist = UdkRedist.Open(redistPath))
        {
            if (!redist.HasVc2008)
            {
                return;
            }
        }

        string binaries = Path.Combine(installDirectory, UdkInstall.BinariesPath);

        AddStartup(Path.Combine(binaries, $"{UdkInstall.ExecutableName}.config"));
        AddStartup(Path.Combine(binaries, CompilerConfigFileName));
    }

    /// <summary>
    ///     Adds the startup section to the config file at <paramref name="path"/>, which starts empty if it does not exist.
    /// </summary>
    /// <param name="path">The path of the config file.</param>
    private static void AddStartup(string path)
    {
        var config = File.Exists(path)
            ? XDocument.Load(path, LoadOptions.PreserveWhitespace)
            : new XDocument(new XElement(RootName));
        var root = config.Root!;

        if (root.Element(StartupName) is not null)
        {
            return;
        }

        root.AddFirst(
            new XElement(
                StartupName,
                new XAttribute("useLegacyV2RuntimeActivationPolicy", "true"),
                new XElement("supportedRuntime", new XAttribute("version", "v4.0"))));

        string partialPath = path + TempPaths.PartialSuffix;
        config.Save(partialPath);

        try
        {
            File.Move(partialPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(UdkSR.FormatPartialNotMoved(partialPath, path), exception);
        }
    }
}
