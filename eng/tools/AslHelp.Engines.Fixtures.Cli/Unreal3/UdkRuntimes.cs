using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Copies the DirectX and Visual C++ DLLs that <c>UDK.exe</c> loads into the install, from the install's own redist.
/// </summary>
internal static class UdkRuntimes
{
    private const string TempPath = @"Binaries\Redist\Runtimes.partial";

    /// <summary>
    ///     Lists the DirectX and Visual C++ DLLs that <c>UDK.exe</c> in <paramref name="binariesDirectory"/> loads through
    ///     its static imports.
    /// </summary>
    /// <remarks>
    ///     The import tables of <c>UDK.exe</c> and of every DLL it loads from <paramref name="binariesDirectory"/> are read,
    ///     breadth first. A name that starts like a DirectX or Visual C++ runtime counts and is not followed. Any other
    ///     name that is not a file in the folder is a system DLL and is skipped. Delay imports are not read, because no
    ///     UDK version delay-loads a runtime DLL.
    /// </remarks>
    /// <param name="binariesDirectory">The <c>Binaries/Win32</c> folder of an extracted install.</param>
    /// <returns>
    ///     The runtime DLL names in ordinal order without case, each spelled as its 1st importer spells it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="binariesDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The folder has no <c>UDK.exe</c>.</exception>
    /// <exception cref="InvalidDataException">A file that <c>UDK.exe</c> or a DLL it loads imports is not a PE file.</exception>
    public static IReadOnlyList<string> FindImports(string binariesDirectory)
    {
        ArgumentNullException.ThrowIfNull(binariesDirectory);

        string serverPath = Path.Combine(binariesDirectory, UdkInstall.ExecutableName);

        if (!File.Exists(serverPath))
        {
            throw new FileNotFoundException(SR.Format(UdkSR.ServerNotFound, serverPath), serverPath);
        }

        SortedSet<string> runtimes = [with(StringComparer.OrdinalIgnoreCase)];
        HashSet<string> seen = [with(StringComparer.OrdinalIgnoreCase), UdkInstall.ExecutableName];
        Queue<string> pending = new();
        pending.Enqueue(serverPath);

        while (pending.TryDequeue(out string? path))
        {
            foreach (string import in PeImports.Read(path))
            {
                if (IsRuntime(import))
                {
                    _ = runtimes.Add(import);
                    continue;
                }

                string local = Path.Combine(binariesDirectory, import);

                if (seen.Add(import) && File.Exists(local))
                {
                    pending.Enqueue(local);
                }
            }
        }

        return [.. runtimes];
    }

    /// <summary>
    ///     Copies into <c>Binaries/Win32</c> of the install the DirectX and Visual C++ 2010 DLLs that <c>UDK.exe</c> loads
    ///     and that are not there yet.
    /// </summary>
    /// <remarks>
    ///     The DLL names come from <see cref="FindImports"/> and their bytes from the install's own
    ///     <c>UE3Redist.exe</c>, so nothing is installed for the whole system. Each DirectX DLL comes from the 1st x86
    ///     cabinet of the redist that lists it.
    ///     Only those cabinets are unpacked with <c>expand.exe</c>. The Visual C++ installer only extracts its files.
    ///     Every DLL is mapped to its source before the 1st extraction, so a redist that lacks one leaves the install as it
    ///     was. Each DLL is copied to a <c>.partial</c> file first and then moved to its name, so a copy cut short by a
    ///     killed run never ends up under the name that the next run checks. An install that has every DLL already is
    ///     not touched, except that a temp folder left by an earlier run is deleted.
    /// </remarks>
    /// <param name="installDirectory">The extracted install from <see cref="Udk.InstallAsync"/>.</param>
    /// <param name="cancellationToken">The token that stops the extraction and ends the running tool.</param>
    /// <returns>
    ///     A task that completes once every DLL is in <c>Binaries/Win32</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">
    ///     The install has no <c>UDK.exe</c> or a DLL is missing and the install has no <c>UE3Redist.exe</c>.
    /// </exception>
    /// <exception cref="IOException">An extraction, a copy or a move fails.</exception>
    /// <exception cref="InvalidDataException">
    ///     A file that <c>UDK.exe</c> or a DLL it loads imports is not a PE file. <c>UE3Redist.exe</c> lacks an entry or
    ///     does not give a DLL.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     A DLL imports a Visual C++ runtime other than 2010, no x86 cabinet of the redist lists a missing DirectX DLL,
    ///     or <c>expand.exe</c> or the Visual C++ installer exits with a code other than 0.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task AddAsync(string installDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        string binaries = Path.Combine(installDirectory, UdkInstall.BinariesPath);
        List<string> missing = [];
        List<string> missingDirectX = [];
        bool vcMissing = false;

        foreach (string runtime in FindImports(binaries))
        {
            bool vc = VisualCppRuntimes.IsRuntime(runtime);

            if (vc)
            {
                VisualCppRuntimes.ThrowIfNot2010(runtime, binaries);
            }

            if (!File.Exists(Path.Combine(binaries, runtime)))
            {
                missing.Add(runtime);

                if (vc)
                {
                    vcMissing = true;
                }
                else
                {
                    missingDirectX.Add(runtime);
                }
            }
        }

        string tempDirectory = Path.Combine(installDirectory, TempPath);

        if (missing.Count == 0)
        {
            // Delete a temp folder that an earlier run left, because the install becomes the shared player.
            // Delete the folder quietly, so a scanner on an old temp file does not fail an install that has every DLL.
            if (Directory.Exists(tempDirectory))
            {
                TempPaths.DeleteFolderQuietly(tempDirectory);
            }

            return;
        }

        string redistPath = UdkRedist.FindPath(installDirectory)
            ?? throw new FileNotFoundException(
                SR.Format(
                    UdkSR.RedistNotFound,
                    Path.Combine(installDirectory, UdkRedist.RedistPath),
                    Path.Combine(installDirectory, UdkRedist.WindowsRedistPath),
                    binaries,
                    string.Join(", ", missing)),
                Path.Combine(installDirectory, UdkRedist.RedistPath));

        using var redist = UdkRedist.Open(redistPath);

        // Map every DirectX DLL before any extraction, so a redist without one leaves Binaries/Win32 as it was.
        var cabinets = DirectXRuntimes.FindCabinets(redist, missingDirectX, binaries);

        // Check the Visual C++ installer before any extraction too, so a redist without it leaves the install as it was.
        if (vcMissing)
        {
            VisualCppRuntimes.ThrowIfNoInstaller(redist, binaries);
        }

        // Delete a temp folder from a crashed run, so its files do not mix with this one's.
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }

        _ = Directory.CreateDirectory(tempDirectory);

        try
        {
            await DirectXRuntimes.ExtractAsync(redist, cabinets, tempDirectory, cancellationToken).ConfigureAwait(false);

            if (vcMissing)
            {
                await VisualCppRuntimes.ExtractAsync(redist, tempDirectory, cancellationToken).ConfigureAwait(false);
            }

            // Check every DLL before the 1st copy, so a bad redist leaves Binaries/Win32 as it was.
            foreach (string runtime in missing)
            {
                if (!File.Exists(Path.Combine(tempDirectory, runtime)))
                {
                    throw new InvalidDataException(SR.Format(UdkSR.RedistRuntimeMissing, redistPath, runtime, binaries));
                }
            }

            // Copy under the importer's spelling, because the cabinet can spell a name in other case.
            foreach (string runtime in missing)
            {
                string runtimePath = Path.Combine(binaries, runtime);
                string partialPath = runtimePath + TempPaths.PartialSuffix;

                File.Copy(Path.Combine(tempDirectory, runtime), partialPath, overwrite: true);

                try
                {
                    File.Move(partialPath, runtimePath, overwrite: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(SR.Format(UdkSR.PartialNotMoved, partialPath, runtimePath), exception);
                }
            }
        }
        finally
        {
            // Delete quietly, so a scanner on a temp file neither fails a complete copy nor hides the error of a failed one.
            TempPaths.DeleteFolderQuietly(tempDirectory);
        }
    }

    /// <summary>
    ///     Checks whether <paramref name="fileName"/> is a DirectX or Visual C++ runtime DLL.
    /// </summary>
    /// <param name="fileName">The DLL name of an import.</param>
    /// <returns>
    ///     <see langword="true"/> if the name starts like a DLL of the DirectX or Visual C++ redists.
    /// </returns>
    private static bool IsRuntime(string fileName)
    {
        return VisualCppRuntimes.IsRuntime(fileName) || DirectXRuntimes.IsRuntime(fileName);
    }
}
