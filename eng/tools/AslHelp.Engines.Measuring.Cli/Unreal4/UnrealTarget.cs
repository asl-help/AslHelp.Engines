using System;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Describes 1 value of <c>targets.json</c>, how to find it and the versions it exists on.
/// </summary>
/// <param name="From">The 1st minor version with the value or <see langword="null"/> if every earlier version has it.</param>
/// <param name="Until">The last minor version with the value or <see langword="null"/> if every later version has it.</param>
[JsonDerivedType(typeof(UnrealConstantTarget), "constant")]
[JsonDerivedType(typeof(UnrealFunctionStaticTarget), "functionStatic")]
[JsonDerivedType(typeof(UnrealGlobalTarget), "global")]
[JsonDerivedType(typeof(UnrealLayoutTarget), "layout")]
internal closed record UnrealTarget(
    string? From,
    string? Until)
{
    /// <summary>
    ///     Gets the 1st minor version with the value or <see langword="null"/> if every earlier version has it.
    /// </summary>
    public string? From
    {
        get;
        init
        {
            field = value;
            FromMinor = UnrealVersions.ParseMinor(value);
        }
    } = From;

    /// <summary>
    ///     Gets the last minor version with the value or <see langword="null"/> if every later version has it.
    /// </summary>
    public string? Until
    {
        get;
        init
        {
            field = value;
            UntilMinor = UnrealVersions.ParseMinor(value);
        }
    } = Until;

    /// <summary>
    ///     Gets <see cref="From"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="From"/> after a with expression, because the init accessor of <see cref="From"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? FromMinor { get; private set; } = UnrealVersions.ParseMinor(From);

    /// <summary>
    ///     Gets <see cref="Until"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="Until"/> after a with expression, because the init accessor of <see cref="Until"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? UntilMinor { get; private set; } = UnrealVersions.ParseMinor(Until);
}
