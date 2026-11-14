using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Reads <c>releases.json</c> with generated code instead of reflection over <see cref="UnrealRelease"/>.
/// </summary>
/// <remarks>
///     Every key of a row is required, a null is refused where the type does not allow it and a key outside them is refused,
///     so a half-copied or misspelled row fails the parse. A player is read from its tag and an enum from its name, so a bad
///     tag or a number in place of a name fails the parse too.
/// </remarks>
[JsonSourceGenerationOptions(
    Converters = [typeof(NamedEnumConverter<UnrealRuntimeSource>), typeof(UnrealPlayerConverter)],
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(UnrealRelease[]))]
internal sealed partial class UnrealReleaseContext : JsonSerializerContext;
