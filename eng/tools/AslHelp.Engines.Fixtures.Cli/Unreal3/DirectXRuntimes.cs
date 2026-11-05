using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Matches the DirectX runtime DLLs by name and unpacks them from the x86 cabinets of the redist.
/// </summary>
internal static class DirectXRuntimes
{
    // Match the DirectX DLL families by prefix, because the version suffix, such as _43, differs per UDK version.
    private static readonly string[] _prefixes =
    [
        "d3dcompiler_",
        "d3dcsx_",
        "d3dx10_",
        "d3dx11_",
        "d3dx9_",
        "x3daudio",
        "xactengine",
        "xapofx",
        "xaudio",
        "xinput",
    ];

    /// <summary>
    ///     Checks whether <paramref name="fileName"/> is a DirectX runtime DLL, such as <c>d3dx9_43.dll</c>.
    /// </summary>
    /// <param name="fileName">The DLL name of an import.</param>
    /// <returns>
    ///     <see langword="true"/> if the name starts like a DLL of the DirectX redist.
    /// </returns>
    public static bool IsRuntime(string fileName)
    {
        foreach (string prefix in _prefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Maps each DLL of <paramref name="runtimes"/> to the 1st x86 cabinet of <paramref name="redist"/> that lists it.
    /// </summary>
    /// <param name="redist">The open redist.</param>
    /// <param name="runtimes">The DirectX DLLs to map.</param>
    /// <param name="binariesDirectory">The <c>Binaries/Win32</c> folder of the install, for the message.</param>
    /// <returns>
    ///     The entry names of the cabinets, each once, in the order of the 1st DLL they give.
    /// </returns>
    /// <exception cref="InvalidDataException">A cabinet is damaged.</exception>
    /// <exception cref="InvalidOperationException">No x86 cabinet of the redist lists a DLL.</exception>
    public static List<string> FindCabinets(UdkRedist redist, IReadOnlyList<string> runtimes, string binariesDirectory)
    {
        List<string> cabinets = [];

        foreach (string runtime in runtimes)
        {
            string cabinet = redist.FindCabinet(runtime)
                ?? throw new InvalidOperationException(SR.Format(UdkSR.DirectXRuntimeNotInRedist, redist.FilePath, runtime, binariesDirectory));

            if (!cabinets.Contains(cabinet))
            {
                cabinets.Add(cabinet);
            }
        }

        return cabinets;
    }

    /// <summary>
    ///     Extracts each of <paramref name="cabinets"/> from <paramref name="redist"/> into <paramref name="tempDirectory"/>
    ///     and unpacks its DLLs there with <c>expand.exe</c>.
    /// </summary>
    /// <param name="redist">The open redist.</param>
    /// <param name="cabinets">The cabinets from <see cref="FindCabinets"/>.</param>
    /// <param name="tempDirectory">The temp folder of the runtime step.</param>
    /// <param name="cancellationToken">The token that ends the running tool.</param>
    /// <returns>
    ///     A task that completes once the DLLs of every cabinet are in <paramref name="tempDirectory"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException"><c>expand.exe</c> exits with a code other than 0.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task ExtractAsync(UdkRedist redist, IReadOnlyList<string> cabinets, string tempDirectory, CancellationToken cancellationToken)
    {
        foreach (string cabinet in cabinets)
        {
            redist.Extract(cabinet, tempDirectory);
            string cabinetPath = Path.Combine(tempDirectory, Path.GetFileName(cabinet));
            await Cabinet.ExpandAsync(cabinetPath, "*.dll", tempDirectory, cancellationToken).ConfigureAwait(false);
        }
    }
}
