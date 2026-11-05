using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Adds the <c>Fixture</c> package to a UDK install and compiles it with <c>UDK.com make</c>.
/// </summary>
internal static class UdkProject
{
    /// <summary>
    ///     Represents the line edit of the default engine config, as the player key spells it.
    /// </summary>
    public const string ConfigEdit = $"{DefaultEngineConfigPath}: {TemplatePackageLine} -> {EnabledPackageLine}";

    /// <summary>
    ///     Represents the path of the config file that is deleted, relative to the install.
    /// </summary>
    public const string EngineConfigPath = @"UDKGame\Config\UDKEngine.ini";

    /// <summary>
    ///     Represents the compiler command line.
    /// </summary>
    public const string MakeCommand = $"{CompilerPath} {CompilerArguments}";

    /// <summary>
    ///     Represents the folder of the <c>.uc</c> files, relative to the player project.
    /// </summary>
    public const string ProjectClassesPath = "Classes";

    /// <summary>
    ///     Represents how long <see cref="MakeAsync"/> lets the compile run by default, 10 minutes.
    /// </summary>
    public static readonly TimeSpan DefaultMakeTimeout = TimeSpan.FromMinutes(10);

    private const string ClassesPath = @"Development\Src\Fixture\Classes";

    /// <summary>
    ///     Represents the compiler arguments.
    /// </summary>
    /// <remarks>
    ///     <c>-nullrhi</c> turns rendering off, because a container has no display mode. Without it D3D9
    ///     <c>GetAdapterDisplayMode</c> fails and make exits with code 3.
    /// </remarks>
    private const string CompilerArguments = "make -unattended -nullrhi";

    /// <summary>
    ///     Represents the path of <c>UDK.com</c>, relative to the install.
    /// </summary>
    private const string CompilerPath = $@"{UdkInstall.BinariesPath}\UDK.com";

    /// <summary>
    ///     Represents the path of the config file whose package line is turned on, relative to the install.
    /// </summary>
    private const string DefaultEngineConfigPath = @"UDKGame\Config\DefaultEngineUDK.ini";

    /// <summary>
    ///     Represents the package line that turns the <c>Fixture</c> package on.
    /// </summary>
    private const string EnabledPackageLine = "ModEditPackages=Fixture";

    /// <summary>
    ///     Represents the path of the compiled package, relative to the install.
    /// </summary>
    private const string PackagePath = @"UDKGame\Script\Fixture.u";

    /// <summary>
    ///     Represents the template line of the shipped config file that <see cref="EnabledPackageLine"/> replaces.
    /// </summary>
    private const string TemplatePackageLine = ";ModEditPackages=MyMod";

    /// <summary>
    ///     Copies the fixture classes into the install and turns the <c>Fixture</c> package on.
    /// </summary>
    /// <remarks>
    ///     The classes replace <c>Development/Src/Fixture/Classes</c>, so a class removed from
    ///     <paramref name="classesDirectory"/> is not compiled again. The line <c>;ModEditPackages=MyMod</c> of
    ///     <c>UDKGame/Config/DefaultEngineUDK.ini</c> becomes <c>ModEditPackages=Fixture</c> and every other line stays
    ///     as it was. A file that has <c>ModEditPackages=Fixture</c> already is not written.
    ///     The edited lines go to <c>DefaultEngineUDK.ini.partial</c> first, which then moves over the file, so a write
    ///     cut short by a killed run never replaces it.
    ///     <c>UDKGame/Config/UDKEngine.ini</c> is deleted, because the engine writes it again from the defaults.
    /// </remarks>
    /// <param name="installDirectory">The extracted install from <see cref="Udk.InstallAsync"/>.</param>
    /// <param name="classesDirectory">The folder with the <c>.uc</c> files of the package, outside the install.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="installDirectory"/> or <paramref name="classesDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="classesDirectory"/> does not exist.</exception>
    /// <exception cref="FileNotFoundException">The install has no <c>UDKGame/Config/DefaultEngineUDK.ini</c>.</exception>
    /// <exception cref="IOException">A copy, the write or the move fails.</exception>
    /// <exception cref="InvalidDataException">
    ///     The config file has neither <c>;ModEditPackages=MyMod</c> nor <c>ModEditPackages=Fixture</c> as a line.
    /// </exception>
    public static void AddFixturePackage(string installDirectory, string classesDirectory)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(classesDirectory);

        // Check both inputs before any write, so a bad install stays untouched.
        if (!Directory.Exists(classesDirectory))
        {
            throw new DirectoryNotFoundException(SR.Format(UdkSR.ClassesFolderNotFound, classesDirectory));
        }

        string iniPath = Path.Combine(installDirectory, DefaultEngineConfigPath);

        if (!File.Exists(iniPath))
        {
            throw new FileNotFoundException(SR.Format(UdkSR.ConfigNotFound, iniPath), iniPath);
        }

        string[] lines = File.ReadAllLines(iniPath);
        bool enabled = Array.IndexOf(lines, EnabledPackageLine) >= 0;
        int templateLine = Array.IndexOf(lines, TemplatePackageLine);

        if (!enabled && templateLine < 0)
        {
            throw new InvalidDataException(SR.Format(UdkSR.ConfigLineMissing, iniPath, TemplatePackageLine, EnabledPackageLine));
        }

        ReplaceDirectory(classesDirectory, Path.Combine(installDirectory, ClassesPath));

        if (!enabled)
        {
            // Write the lines back with CRLF, as in the shipped ASCII file.
            lines[templateLine] = EnabledPackageLine;
            string partialPath = iniPath + TempPaths.PartialSuffix;
            File.WriteAllLines(partialPath, lines);

            try
            {
                File.Move(partialPath, iniPath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException(SR.Format(UdkSR.PartialNotMoved, partialPath, iniPath), exception);
            }
        }

        File.Delete(Path.Combine(installDirectory, EngineConfigPath));
    }

    /// <summary>
    ///     Throws if the install in <paramref name="installDirectory"/> has no <c>UDK.com</c>.
    /// </summary>
    /// <param name="installDirectory">The extracted install.</param>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The install has no <c>UDK.com</c>.</exception>
    public static void ThrowIfNoCompiler(string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        string compilerPath = Path.Combine(installDirectory, CompilerPath);

        if (!File.Exists(compilerPath))
        {
            throw new FileNotFoundException(SR.Format(UdkSR.CompilerNotFound, compilerPath), compilerPath);
        }
    }

    /// <summary>
    ///     Compiles the <c>Fixture</c> package of the install in <paramref name="installDirectory"/> with <c>UDK.com make</c>.
    /// </summary>
    /// <remarks>
    ///     The compiler runs in <paramref name="host"/>, so a cancel or a timeout ends <c>UDK.com</c> and every process it
    ///     started. <c>UDKGame/Script/Fixture.u</c> is deleted first,
    ///     because make skips a package that looks up to date and would keep a removed class in it.
    ///     <c>UDKGame/Logs/Launch.log</c> is deleted too, so a failure points only at a log of this compile.
    /// </remarks>
    /// <param name="host">The host that the compiler runs in.</param>
    /// <param name="installDirectory">The install with the classes from <see cref="AddFixturePackage"/>.</param>
    /// <param name="timeout">How long the compile may run once it runs, such as <see cref="DefaultMakeTimeout"/>.</param>
    /// <param name="cancellationToken">The token that stops the compile.</param>
    /// <returns>
    ///     A task that completes once <c>UDKGame/Script/Fixture.u</c> is written.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="installDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The install has no <c>UDK.com</c>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The host cannot run anything or <c>UDK.com</c> exits with a code other than 0 or exits with 0 but does not write
    ///     <c>UDKGame/Script/Fixture.u</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="TimeoutException">The compile runs longer than <paramref name="timeout"/>.</exception>
    public static async Task MakeAsync(IPlayerHost host, string installDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ThrowIfNoCompiler(installDirectory);

        string compilerPath = Path.Combine(installDirectory, CompilerPath);
        string logPath = Path.Combine(installDirectory, UdkInstall.LogPath);
        string packagePath = Path.Combine(installDirectory, PackagePath);

        // Check the token before the deletes, so a canceled call keeps the package and the log of the last run.
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        int exitCode;
        var player = await host.StartAsync(installDirectory, CompilerPath, CompilerArguments, cancellationToken).ConfigureAwait(false);

        await using (player.ConfigureAwait(false))
        {
            // Start the clock once the compiler runs, so the pull of a container image does not count against it.
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await player.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(SR.Format(UdkSR.MakeNotDone, timeout.TotalSeconds, NextStep(logPath, compilerPath)));
            }

            exitCode = player.ExitCode;
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(SR.Format(UdkSR.MakeFailed, exitCode, NextStep(logPath, compilerPath)));
        }

        // Check the package too, because UDK.com can exit with 0 after a failed compile.
        if (!File.Exists(packagePath))
        {
            throw new InvalidOperationException(SR.Format(UdkSR.MakeNoPackage, packagePath, logPath, EnabledPackageLine));
        }
    }

    /// <summary>
    ///     Creates the hint after a failed compile, which points at the log only if it exists, because <c>UDK.com</c> can
    ///     fail before the engine opens it.
    /// </summary>
    /// <param name="logPath">The path of <c>Launch.log</c>.</param>
    /// <param name="compilerPath">The path of <c>UDK.com</c>.</param>
    /// <returns>
    ///     The hint.
    /// </returns>
    private static string NextStep(string logPath, string compilerPath)
    {
        return File.Exists(logPath)
            ? SR.Format(UdkSR.MakeLogFix, logPath)
            : SR.Format(UdkSR.MakeStartFix, compilerPath);
    }

    /// <summary>
    ///     Replaces <paramref name="targetDirectory"/> with a copy of every file under <paramref name="sourceDirectory"/>.
    /// </summary>
    /// <param name="sourceDirectory">The folder to copy.</param>
    /// <param name="targetDirectory">The folder to replace.</param>
    private static void ReplaceDirectory(string sourceDirectory, string targetDirectory)
    {
        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, recursive: true);
        }

        _ = Directory.CreateDirectory(targetDirectory);

        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, source));

            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
    }
}
