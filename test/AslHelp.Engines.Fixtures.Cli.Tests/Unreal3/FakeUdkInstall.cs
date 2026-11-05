using System.IO;
using System.Text;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

/// <summary>
///     Writes a fake UDK install with the folders, the config file and the server that the Unreal3 tests need.
/// </summary>
internal static class FakeUdkInstall
{
    /// <summary>
    ///     Represents the template package line of the shipped config file.
    /// </summary>
    public const string DisabledLine = ";ModEditPackages=MyMod";

    /// <summary>
    ///     Represents the package line that turns the <c>Fixture</c> package on.
    /// </summary>
    public const string EnabledLine = "ModEditPackages=Fixture";

    /// <summary>
    ///     Represents a config file shaped like the shipped one, with CRLF endings and the package line in the editor section.
    /// </summary>
    public static readonly string DefaultIni = $"""
        [Configuration]
        BasedOn=..\Engine\Config\BaseEngine.ini

        [UnrealEd.EditorEngine]
        EditPackagesOutPath=..\..\UDKGame\Script
        FRScriptOutputPath=..\..\UDKGame\ScriptFinalRelease
        +EditPackages=UDKBase
        +EditPackages=UTEditor
        {DisabledLine}
        AutoSaveDir=..\..\UDKGame\Autosaves

        """.ReplaceLineEndings("\r\n");

    /// <summary>
    ///     Writes an install in a new folder of <paramref name="folder"/> with <c>Binaries/Win32</c> and the config file.
    /// </summary>
    /// <param name="folder">The temp folder of the test.</param>
    /// <param name="ini">The text of <c>UDKGame/Config/DefaultEngineUDK.ini</c>, written as ASCII.</param>
    /// <returns>
    ///     The full path of the install.
    /// </returns>
    public static string WriteInstall(TestFolder folder, string ini)
    {
        string install = folder.NewFolder();
        _ = Directory.CreateDirectory(Binaries(install));
        WriteBytes(install, "UDKGame/Config/DefaultEngineUDK.ini", Encoding.ASCII.GetBytes(ini));

        return install;
    }

    /// <summary>
    ///     Gets the path of <c>UDKGame/Config/DefaultEngineUDK.ini</c> of <paramref name="install"/>.
    /// </summary>
    /// <param name="install">The install.</param>
    /// <returns>
    ///     The full path of the config file.
    /// </returns>
    public static string IniPath(string install)
    {
        return Path.Combine(install, "UDKGame", "Config", "DefaultEngineUDK.ini");
    }

    /// <summary>
    ///     Gets the path of <c>Binaries/Win32</c> of <paramref name="install"/>.
    /// </summary>
    /// <param name="install">The install.</param>
    /// <returns>
    ///     The full path of the folder.
    /// </returns>
    public static string Binaries(string install)
    {
        return Path.Combine(install, "Binaries", "Win32");
    }

    /// <summary>
    ///     Writes <c>Binaries/Win32/UDK.exe</c> of <paramref name="install"/> as a PE file with the given imports.
    /// </summary>
    /// <param name="install">The install.</param>
    /// <param name="imports">The DLL names of the import table.</param>
    public static void WriteServer(string install, params string[] imports)
    {
        FakeExecutable.Write(Path.Combine(Binaries(install), "UDK.exe"), imports);
    }

    /// <summary>
    ///     Writes <c>Binaries/Redist/UE3Redist.exe</c> of <paramref name="install"/> with 1 x86 cabinet from
    ///     <c>makecab.exe</c> that has <paramref name="dllName"/> and an inf.
    /// </summary>
    /// <remarks>
    ///     The cabinet has the inf too, because <c>expand.exe</c> writes the file of a 1-file cabinet under the
    ///     cabinet's own name.
    /// </remarks>
    /// <param name="folder">The temp folder of the test, for the cabinet sources.</param>
    /// <param name="install">The install.</param>
    /// <param name="dllName">The DLL name, spelled as the cabinet stores it.</param>
    /// <param name="dllBytes">The DLL content.</param>
    public static void WriteDirectXRedist(TestFolder folder, string install, string dllName, byte[] dllBytes)
    {
        byte[] cabinet = FakeCabinet.Make(folder, (dllName, dllBytes), ("d3dx9_43_x86.inf", [4]));
        WriteZip(Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe"), ("DXRedistCutdown/Jun2010_d3dx9_43_x86.cab", cabinet));
    }
}
