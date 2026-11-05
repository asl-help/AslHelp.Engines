using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Reads the pinned UDK installers from <c>installers.json</c>, 1 row per version.
/// </summary>
internal static class UdkInstallers
{
    private const string InstallersPath = "eng/tools/AslHelp.Engines.Fixtures.Cli/Unreal3/installers.json";
    private const string InstallersResource = "Unreal3/installers.json";

    private static readonly Lazy<IReadOnlyList<UdkInstaller>> _installers = new(ReadEmbedded);

    /// <summary>
    ///     Gets the rows of <c>installers.json</c> in version order, read once on 1st use.
    /// </summary>
    /// <remarks>
    ///     A damaged file throws on every get, because the 1st exception is kept.
    /// </remarks>
    public static IReadOnlyList<UdkInstaller> All => _installers.Value;

    /// <summary>
    ///     Looks up the pinned installer of <paramref name="version"/> in <see cref="All"/>.
    /// </summary>
    /// <param name="version">
    ///     The version as in the file name, such as <c>2015-02</c> or <c>2011-03-BETA2</c>. Upper or lower case both work.
    /// </param>
    /// <returns>
    ///     The row of the version, with the pinned spelling and sha1.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><see cref="All"/> has no installer of <paramref name="version"/>.</exception>
    public static UdkInstaller Find(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        foreach (var installer in All)
        {
            if (installer.Version.Equals(version, StringComparison.OrdinalIgnoreCase))
            {
                return installer;
            }
        }

        string[] versions = new string[All.Count];

        for (int i = 0; i < versions.Length; i++)
        {
            versions[i] = All[i].Version;
        }

        // Throw InvalidOperationException, because the CLI prints the message as is and ArgumentException adds a parameter suffix.
        throw new InvalidOperationException(
            SR.Format(UdkSR.UdkVersionNotPinned, version, string.Join(", ", versions), UdkInstaller.Item, InstallersPath));
    }

    /// <summary>
    ///     Reads the rows of an installer file shaped like <c>installers.json</c>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON array of rows, each with <c>version</c>, <c>sha1</c> and <c>checked</c>.</param>
    /// <returns>
    ///     The rows in file order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">
    ///     The file is not an array of rows or is the JSON literal <c>null</c>. A row lacks a key, has an unknown one or
    ///     has a sha1 that is not 40 lowercase hex chars. The message has the path of <c>installers.json</c>.
    /// </exception>
    public static IReadOnlyList<UdkInstaller> Read(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);

        UdkInstaller[]? installers;

        try
        {
            installers = JsonSerializer.Deserialize(json, UdkInstallerContext.Default.UdkInstallerArray);
        }
        catch (JsonException exception)
        {
            // Wrap the JSON error, so the CLI message points at the file to fix and not only a JSON path.
            throw new InvalidDataException(SR.Format(UdkSR.InstallersInvalid, InstallersPath, exception.Message), exception);
        }

        if (installers is null)
        {
            throw new InvalidDataException(SR.Format(UdkSR.InstallersNull, InstallersPath));
        }

        // Parse every sha1 at load, so a bad pin fails every build and not only one that downloads.
        foreach (var installer in installers)
        {
            try
            {
                _ = installer.File;
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException(SR.Format(UdkSR.InstallersBadSha1, InstallersPath, installer.Sha1, installer.Version), exception);
            }
        }

        return installers;
    }

    /// <summary>
    ///     Reads <c>installers.json</c> from the resources of this assembly.
    /// </summary>
    /// <returns>
    ///     The rows in file order.
    /// </returns>
    /// <exception cref="FileNotFoundException">The assembly has no <c>installers.json</c> resource.</exception>
    private static IReadOnlyList<UdkInstaller> ReadEmbedded()
    {
        using var json = typeof(UdkInstallers).Assembly.GetManifestResourceStream(InstallersResource)
            ?? throw new FileNotFoundException(SR.Format(UdkSR.InstallersNotEmbedded, InstallersResource), InstallersResource);

        return Read(json);
    }
}
