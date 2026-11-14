using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Reads <see cref="FakeRelease"/> rows with the options of the release files of the tool.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FakeRelease[]))]
internal sealed partial class FakeReleaseContext : JsonSerializerContext;
