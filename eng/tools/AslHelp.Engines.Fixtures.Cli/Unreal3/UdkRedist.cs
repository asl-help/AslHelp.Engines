using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Reads the <c>UE3Redist.exe</c> of a UDK install, the self-extracting zip with the DirectX cabinets and the Visual C++ installers.
/// </summary>
internal sealed class UdkRedist : IDisposable
{
    /// <summary>
    ///     Represents the path of the redist in an install of UDK 2011-05 and later, relative to the install.
    /// </summary>
    public const string RedistPath = @"Binaries\Redist\UE3Redist.exe";

    /// <summary>
    ///     Represents the entry name of the Visual C++ 2010 installer, which UDK 2011-10 to 2015-02 ship.
    /// </summary>
    public const string Vc2010Entry = "vcredist_x86_vs2010sp1.exe";

    /// <summary>
    ///     Represents the path of the redist in an install before UDK 2011-05, relative to the install.
    /// </summary>
    public const string WindowsRedistPath = @"Binaries\Windows\UE3Redist.exe";

    private const string CabinetPrefix = "DXRedistCutdown/";
    private const string CabinetSuffix = "_x86.cab";
    private const string PhysXInstallerEntry = "PhysX/PhysX_8.10.29_SystemSoftware.exe";
    private const string Vc2008Entry = "vcredist_x86_vs2008sp1.exe";

    private readonly ZipArchive _archive;

    private Dictionary<string, string>? _cabinets;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UdkRedist"/> class.
    /// </summary>
    /// <param name="filePath">The path of the redist.</param>
    /// <param name="archive">The open zip of the redist. The instance disposes it.</param>
    private UdkRedist(string filePath, ZipArchive archive)
    {
        FilePath = filePath;
        _archive = archive;
        HasVc2008 = archive.GetEntry(Vc2008Entry) is not null;
        HasVc2010 = archive.GetEntry(Vc2010Entry) is not null;
    }

    /// <summary>
    ///     Gets the path of the redist.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    ///     Gets whether the redist has the Visual C++ 2008 installer, as UDK 2009-11 to 2011-07 do. Those versions target CLR 2.
    /// </summary>
    public bool HasVc2008 { get; }

    /// <summary>
    ///     Gets whether the redist has the Visual C++ 2010 installer <see cref="Vc2010Entry"/>.
    /// </summary>
    public bool HasVc2010 { get; }

    /// <summary>
    ///     Finds the redist of the install in <paramref name="installDirectory"/>.
    /// </summary>
    /// <remarks>
    ///     UDK 2011-05 and later have it in <c>Binaries/Redist</c> and earlier versions in <c>Binaries/Windows</c>.
    /// </remarks>
    /// <param name="installDirectory">The extracted install.</param>
    /// <returns>
    ///     The path of <c>Binaries/Redist/UE3Redist.exe</c>, else of <c>Binaries/Windows/UE3Redist.exe</c>, else <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    public static string? FindPath(string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        foreach (string relative in (ReadOnlySpan<string>)[RedistPath, WindowsRedistPath])
        {
            string path = Path.Combine(installDirectory, relative);

            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    ///     Throws if the redist of the install in <paramref name="installDirectory"/> has the PhysX installer.
    /// </summary>
    /// <remarks>
    ///     An install without a redist passes, because the runtime step reports a missing redist only if it needs one.
    /// </remarks>
    /// <param name="installDirectory">The extracted install.</param>
    /// <param name="version">The version, for the message.</param>
    /// <exception cref="InvalidDataException">The redist has no zip in it or a damaged one.</exception>
    /// <exception cref="InvalidOperationException">The redist has the PhysX installer.</exception>
    public static void ThrowIfNeedsPhysXInstaller(string installDirectory, string version)
    {
        if (FindPath(installDirectory) is not { } redistPath)
        {
            return;
        }

        using var redist = Open(redistPath);

        // Refuse the install, because the engine stops at startup until that installer has run on the machine.
        if (redist._archive.GetEntry(PhysXInstallerEntry) is not null)
        {
            throw new InvalidOperationException(UdkSR.FormatUdkNeedsPhysXInstaller(version));
        }
    }

    /// <summary>
    ///     Opens the redist at <paramref name="redistPath"/>.
    /// </summary>
    /// <param name="redistPath">The redist from <see cref="FindPath"/>.</param>
    /// <returns>
    ///     The open redist. Disposing it closes the file.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="redistPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="redistPath"/> does not exist.</exception>
    /// <exception cref="InvalidDataException">The file has no zip in it or a damaged one.</exception>
    public static UdkRedist Open(string redistPath)
    {
        ArgumentNullException.ThrowIfNull(redistPath);

        var archive = SelfExtractingZip.Open(redistPath);

        try
        {
            return new UdkRedist(redistPath, archive);
        }
        catch
        {
            // Close the file on a damaged central directory, because the constructor makes the 1st read of it.
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Finds the x86 DirectX cabinet that has the file <paramref name="fileName"/>.
    /// </summary>
    /// <remarks>
    ///     The file names of every <c>DXRedistCutdown/*_x86.cab</c> are read once, on the 1st call. Names compare without
    ///     case and the 1st cabinet in zip order wins.
    /// </remarks>
    /// <param name="fileName">The file name, such as <c>d3dx9_43.dll</c>.</param>
    /// <returns>
    ///     The entry name of the cabinet or <see langword="null"/> if no x86 cabinet has the file.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A cabinet is damaged.</exception>
    public string? FindCabinet(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        _cabinets ??= ReadCabinets();

        return _cabinets.GetValueOrDefault(fileName);
    }

    /// <summary>
    ///     Extracts the entry <paramref name="entryName"/> into <paramref name="directory"/> under its file name.
    /// </summary>
    /// <param name="entryName">The full name of the entry, such as <c>DXRedistCutdown/Jun2010_d3dx9_43_x86.cab</c>.</param>
    /// <param name="directory">The folder to extract into.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="entryName"/> or <paramref name="directory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="IOException">The file exists or cannot be written.</exception>
    /// <exception cref="InvalidDataException">The redist has no entry <paramref name="entryName"/> or it is damaged.</exception>
    public void Extract(string entryName, string directory)
    {
        ArgumentNullException.ThrowIfNull(entryName);
        ArgumentNullException.ThrowIfNull(directory);

        var entry = _archive.GetEntry(entryName)
            ?? throw new InvalidDataException(UdkSR.FormatRedistEntryMissing(FilePath, entryName));

        entry.ExtractToFile(Path.Combine(directory, Path.GetFileName(entryName)));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _archive.Dispose();
    }

    /// <summary>
    ///     Maps every file name of the x86 DirectX cabinets to the entry name of its cabinet.
    /// </summary>
    /// <returns>
    ///     The map, with names compared without case.
    /// </returns>
    /// <exception cref="InvalidDataException">A cabinet is damaged.</exception>
    private Dictionary<string, string> ReadCabinets()
    {
        Dictionary<string, string> cabinets = [with(StringComparer.OrdinalIgnoreCase)];

        foreach (var entry in _archive.Entries)
        {
            string name = entry.FullName;

            if (!name.StartsWith(CabinetPrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(CabinetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IReadOnlyList<string> files;

            try
            {
                using var cabinet = entry.Open();
                files = Cabinet.ReadNames(cabinet);
            }
            catch (InvalidDataException exception)
            {
                // Put the redist, the entry and the fix in the message, because Cabinet.ReadNames gets only a stream.
                throw new InvalidDataException(UdkSR.FormatRedistEntryDamaged(name, FilePath, exception.Message), exception);
            }

            foreach (string file in files)
            {
                _ = cabinets.TryAdd(file, name);
            }
        }

        return cabinets;
    }
}
