using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Describes a type of the PDB, whose name is the key of the target. The report always has its size as <c>sizeof</c>.
/// </summary>
/// <param name="Members">The members to report, by name. An empty list reports the size alone.</param>
/// <param name="From">The 1st minor version with the type or <see langword="null"/> if every earlier version has it.</param>
/// <param name="Until">The last minor version with the type or <see langword="null"/> if every later version has it.</param>
/// <param name="SizeNeeded">Whether a library reads the size, so it decides the groups.</param>
internal sealed record UnrealLayoutTarget(
    IReadOnlyDictionary<string, UnrealMemberTarget> Members,
    string? From = null,
    string? Until = null,
    bool SizeNeeded = false) : UnrealTarget(From, Until)
{
    /// <summary>
    ///     Represents the key of the size in the report, next to the members.
    /// </summary>
    public const string SizeofMember = "sizeof";

    /// <summary>
    ///     Checks whether a library reads a value of the type, so the value decides the groups.
    /// </summary>
    /// <param name="member">The name of a member in the report or <see cref="SizeofMember"/>.</param>
    /// <returns>
    ///     <see cref="SizeNeeded"/> for the size, <see cref="UnrealMemberTarget.Needed"/> for a listed member and
    ///     <see langword="false"/> for any other name.
    /// </returns>
    public bool Needs(string member)
    {
        return member == SizeofMember ? SizeNeeded : Members.TryGetValue(member, out var target) && target.Needed;
    }
}

/// <summary>
///     Describes the versions that a member of a type exists on and the name of the member in older PDBs.
/// </summary>
/// <param name="From">The 1st minor version with the member or <see langword="null"/> if every earlier version has it.</param>
/// <param name="Until">The last minor version with the member or <see langword="null"/> if every later version has it.</param>
/// <param name="OldName">The name of the member in PDBs up to <paramref name="OldNameUntil"/> or <see langword="null"/> if it never changed.</param>
/// <param name="OldNameUntil">
///     The last minor version whose PDB lists the member as <paramref name="OldName"/> or <see langword="null"/> without an old name.
/// </param>
/// <param name="Needed">Whether a library reads the member, so it decides the groups.</param>
internal sealed record UnrealMemberTarget(
    string? From = null,
    string? Until = null,
    string? OldName = null,
    string? OldNameUntil = null,
    bool Needed = true)
{
    /// <summary>
    ///     Gets the 1st minor version with the member or <see langword="null"/> if every earlier version has it.
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
    ///     Gets the last minor version with the member or <see langword="null"/> if every later version has it.
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
    ///     Gets the last minor version whose PDB lists the member as <see cref="OldName"/> or <see langword="null"/> without an old name.
    /// </summary>
    public string? OldNameUntil
    {
        get;
        init
        {
            field = value;
            OldNameUntilMinor = UnrealVersions.ParseMinor(value);
        }
    } = OldNameUntil;

    /// <summary>
    ///     Gets <see cref="From"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="From"/> after a with expression, because the init accessor of <see cref="From"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? FromMinor { get; private set; } = UnrealVersions.ParseMinor(From);

    /// <summary>
    ///     Gets <see cref="OldNameUntil"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="OldNameUntil"/> after a with expression, because the init accessor of <see cref="OldNameUntil"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? OldNameUntilMinor { get; private set; } = UnrealVersions.ParseMinor(OldNameUntil);

    /// <summary>
    ///     Gets <see cref="Until"/> parsed once at load or <see langword="null"/> if it is open or not a minor version.
    /// </summary>
    /// <remarks>
    ///     Stays in step with <see cref="Until"/> after a with expression, because the init accessor of <see cref="Until"/> parses it again.
    /// </remarks>
    [JsonIgnore]
    public Version? UntilMinor { get; private set; } = UnrealVersions.ParseMinor(Until);

    /// <summary>
    ///     Gets the name of the member in the PDB of <paramref name="minor"/>.
    /// </summary>
    /// <param name="member">The name of the member in the report.</param>
    /// <param name="minor">The minor version of the player.</param>
    /// <returns>
    ///     <see cref="OldName"/> up to <see cref="OldNameUntil"/>, else <paramref name="member"/>.
    /// </returns>
    public string PdbName(string member, Version minor)
    {
        return OldName is not null && UnrealVersions.Within(minor, null, OldNameUntilMinor) ? OldName : member;
    }
}
