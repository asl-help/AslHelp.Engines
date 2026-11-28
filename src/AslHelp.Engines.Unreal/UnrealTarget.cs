using AslHelp.Engines.Profiles;
using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.IO;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides the engine version and the key of a running game, which <see cref="ProfileCatalog{TProfile}.Select"/> picks a document for.
/// </summary>
public static class UnrealTarget
{
    /// <summary>
    ///     The value of <c>engine</c> in the documents of Unreal Engine 3.
    /// </summary>
    public const string Unreal3 = "unreal3";

    /// <summary>
    ///     The value of <c>engine</c> in the documents of Unreal Engine 4 and 5.
    /// </summary>
    public const string Unreal4 = "unreal4";

    private const string ReleaseMarker = "Release-";

    /// <summary>
    ///     Reads or takes the engine version of the game and builds its key.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without <paramref name="version"/>, reads the file version of <paramref name="module"/>.
    ///         Takes the release version of a file version text like <c>++UE5+Release-5.8-CL-58902052</c> and adds the patch of the numbers
    ///         when they match the release version. Without a release version, numbers with major 4 or 5 are the version,
    ///         like 4.19.2.0 for 4.19.2. Unreal Engine 3 stamps 1.0, its engine build and 0, which reads as the UDK release of that build.
    ///         Any other numbers are the game's own version, so the file doesn't have an engine stamp.
    ///         A game can stamp its own numbers in the engine form too, so the check after attach can still fail with the detected version.
    ///     </para>
    ///     <para>
    ///         The key is <c>win-x64</c> or <c>win-x86</c> from the pointer size, then the configuration.
    ///         A name that ends with <c>-Debug</c> or <c>-DebugGame</c> is development and any other name is shipping.
    ///         Only those 2 endings are sure. UBT leaves the configuration out of the name of a development build, like <c>Game.exe</c>,
    ///         so a name without a configuration is a guess. Released games are shipping builds, so the guess is shipping.
    ///         Pass <paramref name="configuration"/> for a development build.
    ///     </para>
    /// </remarks>
    /// <param name="memory">The memory of the game, for its pointer size.</param>
    /// <param name="module">The module whose file has the version resource, usually the executable.</param>
    /// <param name="version">The engine version, like <c>4.27.2</c> or <c>2010-09</c>, to use instead of the stamp.</param>
    /// <param name="configuration">The configuration to use instead of the one of the module name.</param>
    /// <returns>
    ///     The target or a failure when the file doesn't read or doesn't have an engine version,
    ///     <paramref name="version"/> isn't an engine version or the pointer size isn't 4 or 8.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> or <paramref name="module"/> is <see langword="null"/>.
    /// </exception>
    public static Result<ProfileTarget> Detect(
        IMemoryReader memory,
        MemoryModule module,
        string? version = null,
        UnrealConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(module);

        ModuleFileVersion? fileVersion = null;

        if (version is null && !module.GetFileVersion().TryUnwrap(out fileVersion, out var error))
        {
            return error;
        }

        return FromFileVersion(fileVersion, module.Name, memory.Layout.PointerSize, version, configuration);
    }

    /// <summary>
    ///     Reads an engine version the way the Unreal documents and games write it, like <c>4.27.2</c> or <c>2010-09</c>.
    /// </summary>
    /// <remarks>
    ///     Every run of digits is 1 part and anything between the runs only separates them, so <c>2010-09</c> and <c>2010.9</c> are equal.
    ///     Text after the last number doesn't count, so <c>2010-09-BETA</c> equals <c>2010-09</c> too.
    ///     <see cref="ProfileCatalog{TProfile}"/> takes it as the version parser of the Unreal documents.
    /// </remarks>
    /// <param name="text">The version text, like <c>4.27.2</c>.</param>
    /// <returns>
    ///     The version or <see langword="null"/> when <paramref name="text"/> doesn't have an ASCII digit
    ///     or has a number past <see cref="int.MaxValue"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static ProfileVersion? ParseVersion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<int> parts = [];
        int part = -1;

        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                if (part >= 0)
                {
                    parts.Add(part);
                    part = -1;
                }

                continue;
            }

            int digit = c - '0';

            if (part > (int.MaxValue - digit) / 10)
            {
                return null;
            }

            part = (part < 0 ? 0 : part * 10) + digit;
        }

        if (part >= 0)
        {
            parts.Add(part);
        }

        return parts.Count == 0 ? null : new ProfileVersion(text, [.. parts]);
    }

    /// <summary>
    ///     Builds the target from a file version the way <see cref="Detect"/> does.
    /// </summary>
    /// <param name="fileVersion">The file version of the module or <see langword="null"/> when <paramref name="version"/> is passed.</param>
    /// <param name="moduleName">The name of the module, like <c>Game-Win64-Shipping.exe</c>.</param>
    /// <param name="pointerSize">The pointer size of the memory.</param>
    /// <param name="version">The engine version to use instead of the stamp.</param>
    /// <param name="configuration">The configuration to use instead of the one of the module name.</param>
    /// <returns>
    ///     The target or the failure of <see cref="Detect"/>.
    /// </returns>
    internal static Result<ProfileTarget> FromFileVersion(
        ModuleFileVersion? fileVersion,
        string moduleName,
        byte pointerSize,
        string? version,
        UnrealConfiguration? configuration)
    {
        string? architecture = pointerSize switch
        {
            8 => "x64",
            4 => "x86",
            _ => null,
        };

        if (architecture is null)
        {
            return TargetError.PointerSizeUnsupported(pointerSize);
        }

        string configurationName = (configuration ?? ConfigurationOf(moduleName)) == UnrealConfiguration.Development ? "development" : "shipping";
        string key = $"win-{architecture}-{configurationName}";

        return version is null ? ReadFileVersion(fileVersion!, moduleName, key) : ReadVersion(version, key);
    }

    private static UnrealConfiguration ConfigurationOf(string moduleName)
    {
        string name = Path.GetFileNameWithoutExtension(moduleName);

        return name.EndsWith("-Debug", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-DebugGame", StringComparison.OrdinalIgnoreCase)
            ? UnrealConfiguration.Development
            : UnrealConfiguration.Shipping;
    }

    private static Result<ProfileTarget> ReadFileVersion(ModuleFileVersion fileVersion, string moduleName, string key)
    {
        var numbers = fileVersion.Version;

        if (ReadReleaseVersion(fileVersion.Text) is { } release)
        {
            int major = release.Parts[0];
            int minor = release.Parts.Count > 1 ? release.Parts[1] : 0;

            var version = numbers.Major == major && numbers.Minor == minor
                ? new ProfileVersion($"{major}.{minor}.{numbers.Build}", major, minor, numbers.Build)
                : new ProfileVersion($"{major}.{minor}", major, minor);

            return new ProfileTarget(Unreal4, version, key);
        }

        if (numbers.Major is 4 or 5)
        {
            return new ProfileTarget(
                Unreal4,
                new ProfileVersion($"{numbers.Major}.{numbers.Minor}.{numbers.Build}", numbers.Major, numbers.Minor, numbers.Build),
                key);
        }

        if (numbers is { Major: 1, Minor: 0, Build: > 0, Revision: 0 })
        {
            return Unreal3Release(numbers.Build) is { } udkRelease
                ? new ProfileTarget(Unreal3, udkRelease, key)
                : TargetError.BuildUnknown(numbers.Build);
        }

        return TargetError.StampMissing(moduleName);
    }

    // Read the version after Release- up to the 1st character that isn't a digit or a dot, like 5.8 in ++UE5+Release-5.8-CL-58902052.
    private static ProfileVersion? ReadReleaseVersion(string? text)
    {
        int start = text?.IndexOf(ReleaseMarker, StringComparison.Ordinal) ?? -1;

        if (start < 0)
        {
            return null;
        }

        start += ReleaseMarker.Length;
        int end = start;

        while (end < text!.Length && (char.IsDigit(text[end]) || text[end] == '.'))
        {
            end++;
        }

        return ParseVersion(text.Substring(start, end - start)) is { Parts: [4 or 5, ..] } release ? release : null;
    }

    private static Result<ProfileTarget> ReadVersion(string text, string key)
    {
        if (ParseVersion(text) is not { } version)
        {
            return TargetError.VersionInvalid(text);
        }

        // Pick the engine by the form of the version, because UDK releases are a year and a month like 2010-09.
        return version.Parts[0] switch
        {
            >= 2000 => new ProfileTarget(Unreal3, version, key),
            >= 4 => new ProfileTarget(Unreal4, version, key),
            _ => TargetError.VersionInvalid(text),
        };
    }

    // List the engine build that each measured UDK release stamps, because the stamp doesn't have the release month. 2011-03-BETA2
    // stamps 7977 like 2011-03-BETA, so that build maps to 2011-03-BETA, which the same document lists.
    private static ProfileVersion? Unreal3Release(int build)
    {
        string? release = build switch
        {
            6829 => "2010-06-BETA",
            6890 => "2010-07-BETA",
            7026 => "2010-08-BETA",
            7258 => "2010-09",
            7422 => "2010-10-BETA",
            7546 => "2010-11-BETA",
            7637 => "2010-12-BETA",
            7748 => "2011-01-BETA",
            7876 => "2011-02-BETA",
            7977 => "2011-03-BETA",
            8364 => "2011-05-BETA",
            8710 => "2011-07-BETA",
            9029 => "2011-10-BETA",
            9140 => "2011-11-BETA",
            9249 => "2011-12-BETA",
            9375 => "2012-01-BETA",
            9505 => "2012-02-BETA",
            9953 => "2012-05-BETA",
            10246 => "2012-07-BETA2",
            10499 => "2012-11-BETA",
            10897 => "2013-02-BETA",
            10900 => "2013-02-BETA2",
            10907 => "2013-07",
            12097 => "2014-02",
            12791 => "2015-02",
            _ => null,
        };

        return release is null ? null : ParseVersion(release);
    }
}
