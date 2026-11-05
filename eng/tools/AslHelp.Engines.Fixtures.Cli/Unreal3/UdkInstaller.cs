using System;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Describes 1 row of <c>installers.json</c>, a UDK version and its pinned installer.
/// </summary>
/// <param name="Version">The version with the pinned spelling, such as <c>2015-02</c> or <c>2011-03-BETA2</c>.</param>
/// <param name="Sha1">The sha1 of the installer as 40 lowercase hex chars.</param>
internal sealed record UdkInstaller(
    string Version,
    string Sha1)
{
    /// <summary>
    ///     Identifies the archive.org item that has every UDK installer.
    /// </summary>
    public const string Item = "udkinstaller";

    private const string FilePrefix = "UDKInstall-";
    private const string FileSuffix = ".exe";

    /// <summary>
    ///     Gets the installer <c>UDKInstall-&lt;version&gt;.exe</c> of the <c>udkinstaller</c> item, pinned to <see cref="Sha1"/>.
    /// </summary>
    /// <exception cref="FormatException"><see cref="Sha1"/> is not 40 lowercase hex chars.</exception>
    [JsonIgnore]
    public ArchiveOrgFile File => new(Item, $"{FilePrefix}{Version}{FileSuffix}", PinnedHash.Sha1(Sha1));
}
