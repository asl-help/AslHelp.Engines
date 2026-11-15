using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Reads <c>targets.json</c> with generated code instead of reflection over <see cref="UnrealTarget"/>.
/// </summary>
/// <remarks>
///     Uses metadata mode, because polymorphic records do not work with the fast path.
///     Allows <c>$type</c> after other keys, because a person edits the file by hand.
///     Refuses a missing key, a null where the type does not allow it, an unknown key and a target written twice, so a misspelled
///     file fails the parse.
/// </remarks>
[JsonSourceGenerationOptions(
    AllowDuplicateProperties = false,
    AllowOutOfOrderMetadataProperties = true,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Dictionary<string, UnrealTarget>))]
internal sealed partial class UnrealTargetContext : JsonSerializerContext;
