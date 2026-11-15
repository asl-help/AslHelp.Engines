using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Serializes <see cref="UnrealFixtureManifest"/> with generated code, so the read and the write need no reflection.
/// </summary>
/// <remarks>
///     Enums are written as their member names, such as <c>Shipping</c>.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(UnrealFixtureManifest))]
internal sealed partial class UnrealFixtureManifestContext : JsonSerializerContext;
