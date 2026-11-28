using AslHelp.Engines.Fixtures.Cli.Transfer;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Describes 1 row of <c>releases.json</c>, a UDK version and its pinned installer.
/// </summary>
/// <param name="Version">The version with the pinned spelling, such as <c>2015-02</c> or <c>2011-03-BETA2</c>.</param>
/// <param name="Sha1">The sha1 of the installer as 40 lowercase hex chars.</param>
internal sealed record UdkRelease(
    string Version,
    string Sha1) : IReleaseRow<UdkRelease>
{
    private const string FilePrefix = "UDKInstall-";
    private const string FileSuffix = ".exe";
    private const string Item = "udkinstaller";

    /// <inheritdoc/>
    public static JsonTypeInfo<UdkRelease[]> ArrayTypeInfo => UdkReleaseContext.Default.UdkReleaseArray;

    /// <inheritdoc/>
    public static string DisplayName => UdkSR.DisplayName;

    /// <summary>
    ///     Gets the installer <c>UDKInstall-&lt;version&gt;.exe</c> of the <c>udkinstaller</c> item, pinned to <see cref="Sha1"/>.
    /// </summary>
    /// <exception cref="FormatException"><see cref="Sha1"/> is not 40 lowercase hex chars.</exception>
    [JsonIgnore]
    public ArchiveOrgFile File => new(Item, $"{FilePrefix}{Version}{FileSuffix}", PinnedHash.Sha1(Sha1));

    /// <inheritdoc/>
    /// <remarks>
    ///     The only key is the version.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<string> Keys => [Version];

    /// <inheritdoc/>
    /// <remarks>
    ///     The pin is the version.
    /// </remarks>
    [JsonIgnore]
    public string Pin => Version;

    /// <inheritdoc/>
    public static string ReleasesPath => "eng/fixtures/unreal3/releases.json";

    /// <inheritdoc/>
    public static string ReleasesResource => "Unreal3/releases.json";

    /// <inheritdoc/>
    /// <remarks>
    ///     The spelling is the version as in the file name, such as <c>2015-02</c> or <c>2011-03-BETA2</c>. Upper or lower case both work.
    /// </remarks>
    public bool Matches(string spelling)
    {
        return spelling.Equals(Version, StringComparison.OrdinalIgnoreCase);
    }
}
