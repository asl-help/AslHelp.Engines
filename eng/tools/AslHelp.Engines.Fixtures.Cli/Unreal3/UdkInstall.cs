namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Defines the install paths that the other parts share.
/// </summary>
internal static class UdkInstall
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

}
