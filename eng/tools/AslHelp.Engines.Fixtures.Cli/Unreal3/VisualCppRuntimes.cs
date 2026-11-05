using AslHelp.Engines.Fixtures.Unreal3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Matches the Visual C++ runtime DLLs by name and extracts the 2010 runtime from the redist.
/// </summary>
internal static class VisualCppRuntimes
{
    private const string VcCabFilter = "F_CENTRAL_msvc*100_x86";
    private const string VcCabName = "vc_red.cab";
    private const string VcFilePrefix = "F_CENTRAL_";
    private const string VcFileSuffix = "_x86";

    // List the Visual C++ 2010 DLLs, because only vcredist_x86_vs2010sp1.exe is extracted.
    private static readonly string[] _vc2010Runtimes =
    [
        "msvcp100.dll",
        "msvcr100.dll",
    ];

    // List the Visual C++ 2005 and 2008 DLLs, because Windows ships them in WinSxS with the policies that redirect the older builds to them.
    private static readonly string[] _windowsRuntimes =
    [
        "msvcp80.dll",
        "msvcp90.dll",
        "msvcr80.dll",
        "msvcr90.dll",
    ];

    /// <summary>
    ///     Checks whether <paramref name="fileName"/> is a Visual C++ runtime DLL, such as <c>msvcr100.dll</c>.
    /// </summary>
    /// <remarks>
    ///     The system CRTs <c>msvcrt.dll</c> and <c>msvcp60.dll</c> do not count, because Windows ships them.
    /// </remarks>
    /// <param name="fileName">The DLL name of an import.</param>
    /// <returns>
    ///     <see langword="true"/> if the name is <c>msvcr</c> or <c>msvcp</c>, then a version of 2 or 3 digits other than
    ///     60, then <c>.dll</c>.
    /// </returns>
    public static bool IsRuntime(string fileName)
    {
        if (!fileName.StartsWith("msvcr", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("msvcp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var version = Path.GetFileNameWithoutExtension(fileName.AsSpan())["msvcr".Length..];

        return version.Length is 2 or 3
            && !version.SequenceEqual("60")
            && !version.ContainsAnyExceptInRange('0', '9')
            && Path.GetExtension(fileName.AsSpan()).Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Checks whether <paramref name="runtime"/> is a Visual C++ runtime DLL that Windows ships, so no install needs a copy.
    /// </summary>
    /// <param name="runtime">The DLL name of an import, which <see cref="IsRuntime"/> accepts.</param>
    /// <returns>
    ///     <see langword="true"/> if the name is a Visual C++ 2005 or 2008 DLL.
    /// </returns>
    public static bool IsPartOfWindows(string runtime)
    {
        return Array.Exists(_windowsRuntimes, name => name.Equals(runtime, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Throws if <paramref name="runtime"/>, a Visual C++ runtime DLL, is not a Visual C++ 2010 one.
    /// </summary>
    /// <param name="runtime">The DLL name of an import, which <see cref="IsRuntime"/> accepts.</param>
    /// <param name="binariesDirectory">The <c>Binaries/Win32</c> folder of the install, for the message.</param>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="runtime"/> is not <c>msvcr100.dll</c> or <c>msvcp100.dll</c>.
    /// </exception>
    public static void ThrowIfNot2010(string runtime, string binariesDirectory)
    {
        if (Array.Exists(_vc2010Runtimes, name => name.Equals(runtime, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string importer = SR.Format(UdkSR.VcImporter, Path.Combine(binariesDirectory, UdkInstall.ExecutableName));

        throw new InvalidOperationException(SR.Format(UdkSR.VcOtherUnsupported, importer, runtime));
    }

    /// <summary>
    ///     Throws if <paramref name="redist"/> has no Visual C++ 2010 installer.
    /// </summary>
    /// <param name="redist">The open redist.</param>
    /// <param name="binariesDirectory">The <c>Binaries/Win32</c> folder of the install, for the message.</param>
    /// <exception cref="InvalidDataException">The redist has no <c>vcredist_x86_vs2010sp1.exe</c>.</exception>
    public static void ThrowIfNoInstaller(UdkRedist redist, string binariesDirectory)
    {
        if (!redist.HasVc2010)
        {
            throw new InvalidDataException(SR.Format(UdkSR.Vc2010InstallerMissing, redist.FilePath, UdkRedist.Vc2010Entry, binariesDirectory));
        }
    }

    /// <summary>
    ///     Extracts <c>msvcr100.dll</c> and <c>msvcp100.dll</c> from the Visual C++ 2010 installer of
    ///     <paramref name="redist"/> into <paramref name="tempDirectory"/>.
    /// </summary>
    /// <remarks>
    ///     The installer only extracts its files, so nothing is installed for the whole system.
    ///     <see cref="ExpandCabinetAsync"/> then unpacks the DLLs from the extracted <c>vc_red.cab</c>.
    /// </remarks>
    /// <param name="redist">The open redist.</param>
    /// <param name="tempDirectory">The temp folder of the runtime step.</param>
    /// <param name="cancellationToken">The token that ends the running tool.</param>
    /// <returns>
    ///     A task that completes once <c>expand.exe</c> exits with code 0 and the unpacked files are renamed.
    /// </returns>
    /// <exception cref="IOException">The installer cannot be extracted or a rename fails.</exception>
    /// <exception cref="InvalidDataException">The redist has no <c>vcredist_x86_vs2010sp1.exe</c>.</exception>
    /// <exception cref="InvalidOperationException">The installer or <c>expand.exe</c> exits with a code other than 0.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task ExtractAsync(UdkRedist redist, string tempDirectory, CancellationToken cancellationToken)
    {
        redist.Extract(UdkRedist.Vc2010Entry, tempDirectory);

        await ProcessRunner.RunAsync(NewInstallerStartInfo(tempDirectory), cancellationToken).ConfigureAwait(false);
        await ExpandCabinetAsync(tempDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates the start info that runs the Visual C++ installer in <paramref name="tempDirectory"/> and extracts its files there.
    /// </summary>
    /// <param name="tempDirectory">The temp folder of the runtime step, with the extracted installer.</param>
    /// <returns>
    ///     The start info for <see cref="ProcessRunner.RunAsync"/>.
    /// </returns>
    public static ProcessStartInfo NewInstallerStartInfo(string tempDirectory)
    {
        var startInfo = ProcessRunner.NewStartInfo(Path.Combine(tempDirectory, UdkRedist.Vc2010Entry));

        // Quote only the path after /x:, because the installer reads a quoted "/x:<folder>" as no option and starts an install.
        startInfo.Arguments = $"/q /x:\"{tempDirectory}\"";

        // Run the Visual C++ installer as the invoker, because it waits on a hidden elevation prompt otherwise.
        startInfo.Environment["__COMPAT_LAYER"] = "RunAsInvoker";

        return startInfo;
    }

    /// <summary>
    ///     Unpacks <c>msvcr100.dll</c> and <c>msvcp100.dll</c> from the <c>vc_red.cab</c> in
    ///     <paramref name="tempDirectory"/> into the same folder.
    /// </summary>
    /// <param name="tempDirectory">
    ///     The temp folder of the runtime step, with the <c>vc_red.cab</c> that the installer extracted.
    /// </param>
    /// <param name="cancellationToken">The token that ends <c>expand.exe</c>.</param>
    /// <returns>
    ///     A task that completes once <c>expand.exe</c> exits with code 0 and the unpacked files are renamed.
    /// </returns>
    /// <exception cref="IOException">A rename fails.</exception>
    /// <exception cref="InvalidOperationException"><c>expand.exe</c> exits with a code other than 0.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task ExpandCabinetAsync(string tempDirectory, CancellationToken cancellationToken)
    {
        await Cabinet.ExpandAsync(Path.Combine(tempDirectory, VcCabName), VcCabFilter, tempDirectory, cancellationToken).ConfigureAwait(false);

        // Rename F_CENTRAL_msvcr100_x86 to msvcr100.dll, because the cabinet stores the DLLs under their MSI file keys.
        foreach (string file in Directory.GetFiles(tempDirectory, VcCabFilter))
        {
            string name = Path.GetFileName(file);
            File.Move(file, Path.Combine(tempDirectory, $"{name[VcFilePrefix.Length..^VcFileSuffix.Length]}.dll"), overwrite: true);
        }
    }
}
