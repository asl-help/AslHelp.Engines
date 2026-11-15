using System;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Describes a global, whose name is the key of the target. It has a public symbol or lives in a static that a constructor runs on.
/// </summary>
/// <param name="From">The 1st minor version with the global or <see langword="null"/> if every earlier version has it.</param>
/// <param name="Until">The last minor version with the global or <see langword="null"/> if every later version has it.</param>
/// <param name="ConstructedType">The type of the static or <see langword="null"/> if every version has the public.</param>
/// <param name="ConstructedFrom">
///     The 1st minor version with the static or <see langword="null"/> if every version has the public or every version has the static.
/// </param>
/// <param name="ConstructedUntil">
///     The last minor version with the static or <see langword="null"/> if every version has the public or every version has the static.
/// </param>
internal sealed record UnrealGlobalTarget(
    string? From = null,
    string? Until = null,
    string? ConstructedType = null,
    string? ConstructedFrom = null,
    string? ConstructedUntil = null) : UnrealTarget(From, Until)
{
    /// <summary>
    ///     Gets the 1st minor version with the static or <see langword="null"/> if every version has the public or every version has the static.
    /// </summary>
    public string? ConstructedFrom
    {
        get;
        init
        {
            field = value;
            ConstructedFromMinor = UnrealVersions.ParseMinor(value);
        }
    } = ConstructedFrom;

    /// <summary>
    ///     Gets the last minor version with the static or <see langword="null"/> if every version has the public or every version has the static.
    /// </summary>
    public string? ConstructedUntil
    {
        get;
        init
        {
            field = value;
            ConstructedUntilMinor = UnrealVersions.ParseMinor(value);
        }
    } = ConstructedUntil;

    /// <summary>
    ///     Gets <see cref="ConstructedFrom"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="ConstructedFrom"/> after a with expression, because the init accessor of <see cref="ConstructedFrom"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? ConstructedFromMinor { get; private set; } = UnrealVersions.ParseMinor(ConstructedFrom);

    /// <summary>
    ///     Gets <see cref="ConstructedUntil"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="ConstructedUntil"/> after a with expression, because the init accessor of <see cref="ConstructedUntil"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? ConstructedUntilMinor { get; private set; } = UnrealVersions.ParseMinor(ConstructedUntil);
}
