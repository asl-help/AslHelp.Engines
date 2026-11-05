using System.Text.Json.Serialization;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Reads <c>installers.json</c> with generated code, so the tool needs no reflection over <see cref="UdkInstaller"/>.
/// </summary>
/// <remarks>
///     Every key of a row is required, a null is refused where the type does not allow it and a key outside them is refused,
///     so a half-copied or misspelled row fails the parse.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(UdkInstaller[]))]
internal sealed partial class UdkInstallerContext : JsonSerializerContext;
