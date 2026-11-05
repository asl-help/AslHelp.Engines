using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Serializes <see cref="PlayerKey"/> with generated code, so the manifest write needs no reflection.
/// </summary>
/// <remarks>
///     Nothing reads a manifest back. A hit needs the player folder or the archive alone.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(PlayerKey))]
internal sealed partial class PlayerKeyContext : JsonSerializerContext;
