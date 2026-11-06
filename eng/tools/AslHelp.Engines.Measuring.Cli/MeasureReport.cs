using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli;

/// <summary>
///     Describes the result of a run over the players of 1 engine.
/// </summary>
/// <param name="Engine">The name of the engine.</param>
/// <param name="Players">The rows of the players in version order.</param>
internal sealed record MeasureReport(
    string Engine,
    IReadOnlyList<PlayerRow> Players);

/// <summary>
///     Describes the result of 1 player.
/// </summary>
/// <param name="Version">The version of the engine.</param>
/// <param name="Error">The failure or <see langword="null"/> if the player was measured.</param>
internal sealed record PlayerRow(
    string Version,
    string? Error);

/// <summary>
///     Serializes <see cref="MeasureReport"/> with generated code, so the write needs no reflection.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(MeasureReport))]
internal sealed partial class MeasureReportContext : JsonSerializerContext;
