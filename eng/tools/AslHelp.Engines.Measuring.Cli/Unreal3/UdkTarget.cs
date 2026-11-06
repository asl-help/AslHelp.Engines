using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Describes 1 global of <c>targets.json</c> and where its search starts.
/// </summary>
[JsonDerivedType(typeof(UdkNameArrayTarget), "nameArray")]
[JsonDerivedType(typeof(UdkObjectArrayTarget), "objectArray")]
[JsonDerivedType(typeof(UdkObjectSlotTarget), "objectSlot")]
internal abstract record UdkTarget;

/// <summary>
///     Describes the name array, found by the names it starts with.
/// </summary>
/// <param name="FirstNames">The first names of the array in order.</param>
internal sealed record UdkNameArrayTarget(
    IReadOnlyList<string> FirstNames) : UdkTarget;

/// <summary>
///     Describes the object array, found by the index in each of its objects.
/// </summary>
internal sealed record UdkObjectArrayTarget : UdkTarget;

/// <summary>
///     Describes a global that points at an object, found by the class of the object.
/// </summary>
/// <param name="Class">The class that the object derives from.</param>
/// <param name="Object">The name of the object or <see langword="null"/> if any object of the class fits.</param>
internal sealed record UdkObjectSlotTarget(
    string Class,
    string? Object = null) : UdkTarget;

/// <summary>
///     Reads <c>targets.json</c> with generated code, so the tool needs no reflection over <see cref="UdkTarget"/>.
/// </summary>
/// <remarks>
///     Uses metadata mode, because polymorphic records do not work with the fast path.
///     Allows <c>$type</c> after other keys, because a person edits the file by hand.
///     Refuses a missing key, a null where the type does not allow it, an unknown key and a global written twice, so a misspelled
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
[JsonSerializable(typeof(Dictionary<string, UdkTarget>))]
internal sealed partial class UdkTargetContext : JsonSerializerContext;
