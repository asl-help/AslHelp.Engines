using AslHelp.Engines.Measuring.Cli.Routes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Describes the result of a run over the players of 1 engine.
/// </summary>
/// <param name="Engine">The name of the engine.</param>
/// <param name="Players">The rows of the players in version order.</param>
/// <param name="Groups">The groups of versions with equal offsets, in order of their 1st version.</param>
internal sealed record MeasureReport(
    string Engine,
    IReadOnlyList<PlayerRow> Players,
    IReadOnlyList<VersionGroup> Groups);

/// <summary>
///     Describes the values found in the memory of 1 player.
/// </summary>
/// <param name="Globals">The RVAs in the executable of each global, by global.</param>
/// <param name="Offsets">The offset of each member, by <c>Type.Member</c>.</param>
/// <param name="Unmeasured">The reason for each global or member that was not found, by global or <c>Type.Member</c>.</param>
/// <param name="ExecutablePath">The file of the executable whose RVAs <paramref name="Globals"/> lists.</param>
internal sealed record PlayerMeasurement(
    IReadOnlyDictionary<string, IReadOnlyList<uint>> Globals,
    IReadOnlyDictionary<string, int> Offsets,
    IReadOnlyDictionary<string, string> Unmeasured,
    string ExecutablePath);

/// <summary>
///     Describes the result of 1 player.
/// </summary>
/// <param name="Version">The version of the engine.</param>
/// <param name="Error">The failure or <see langword="null"/> if the player was measured.</param>
/// <param name="Globals">The RVAs of each global in hex or <see langword="null"/> if the player failed.</param>
/// <param name="Offsets">The offset of each member in hex, by type and member, or <see langword="null"/> if the player failed.</param>
/// <param name="Unmeasured">The reason for each value that was not found or <see langword="null"/> if the player failed.</param>
internal sealed record PlayerRow(
    string Version,
    string? Error,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Globals,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Offsets,
    IReadOnlyDictionary<string, string>? Unmeasured)
{
    /// <summary>
    ///     Creates the row of a player that failed.
    /// </summary>
    /// <param name="version">The version of the engine.</param>
    /// <param name="error">The failure.</param>
    /// <returns>
    ///     The row with the failure and no values.
    /// </returns>
    public static PlayerRow Failed(string version, string error)
    {
        return new PlayerRow(version, error, null, null, null);
    }

    /// <summary>
    ///     Creates the row of a player that was measured.
    /// </summary>
    /// <param name="version">The version of the engine.</param>
    /// <param name="measurement">The values found.</param>
    /// <returns>
    ///     The row with the values, with the types sorted by name and the members of each type sorted by offset.
    /// </returns>
    public static PlayerRow Measured(string version, PlayerMeasurement measurement)
    {
        var globals = measurement.Globals.ToDictionary(
            static global => global.Key,
            static IReadOnlyList<string> (global) => [.. global.Value.Select(static rva => ToHex(rva))],
            StringComparer.Ordinal);

        var offsets = measurement.Offsets
            .OrderBy(static offset => offset.Value)
            .GroupBy(static offset => offset.Key[..offset.Key.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal)
            .OrderBy(static type => type.Key, StringComparer.Ordinal)
            .ToDictionary(
                static type => type.Key,
                static IReadOnlyDictionary<string, string> (type) => type.ToDictionary(
                    offset => offset.Key[(type.Key.Length + 1)..],
                    static offset => ToHex(offset.Value),
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

        return new PlayerRow(version, null, globals, offsets, measurement.Unmeasured);
    }

    private static string ToHex(long value)
    {
        return $"0x{value:X}";
    }
}

/// <summary>
///     Describes the versions whose players have the same offsets.
/// </summary>
/// <param name="Versions">The versions in the order of the run.</param>
/// <param name="Offsets">The offset of each member in hex, by type and member.</param>
/// <param name="Routes">The signature routes that the versions share, by global.</param>
internal sealed record VersionGroup(
    List<string> Versions,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Offsets,
    IReadOnlyDictionary<string, GlobalRoutes> Routes);

/// <summary>
///     Serializes <see cref="MeasureReport"/> with generated code, so the write needs no reflection.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(MeasureReport))]
internal sealed partial class MeasureReportContext : JsonSerializerContext;
