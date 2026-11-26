using AslHelp.Engines.Measuring.Cli.Profiles.Errors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Profiles;

/// <summary>
///     Represents what an engine library needs to attach to a game of 1 version group.
/// </summary>
/// <param name="Engine">The name of the engine.</param>
/// <param name="Key">The key of the group or <see langword="null"/> if the measurer doesn't use keys.</param>
/// <param name="Versions">The versions of the group without the key, in the order of the group.</param>
/// <param name="Shapes">The layout of each type with several layouts or <see langword="null"/> if the engine doesn't list any.</param>
/// <param name="Offsets">The needed offset of each member in hex, by type and member.</param>
/// <param name="Globals">
///     The routes of each global. Each route is under the 1st version it covers, without the key.
///     Each value is a locator in the JSON form that <c>MemoryLocator</c> of AslHelp.Memory reads.
/// </param>
internal sealed record ProfileDocument(
    string Engine,
    string? Key,
    IReadOnlyList<string> Versions,
    IReadOnlyDictionary<string, string>? Shapes,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Offsets,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ProfileStep>>> Globals)
{
    private static readonly ProfileStep _readGlobal = new(null, null, "global");

    /// <summary>
    ///     Gets the file name of the document. It's the engine and the 1st version, followed by the key if the group has 1.
    /// </summary>
    [JsonIgnore]
    public string FileName => Key is null ? $"{Engine}-{Versions[0]}.json" : $"{Engine}-{Versions[0]}-{Key}.json";

    /// <summary>
    ///     Creates the document of <paramref name="group"/>.
    /// </summary>
    /// <remarks>
    ///     Each route becomes a search of the whole module followed by a read of the global at the operand.
    ///     It leaves out a global without a route.
    /// </remarks>
    /// <param name="engine">The name of the engine.</param>
    /// <param name="group">The group with its routes.</param>
    /// <returns>
    ///     The document or the error for 2 routes of 1 global with the same name.
    /// </returns>
    public static Result<ProfileDocument> Create(string engine, VersionGroup group)
    {
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ProfileStep>>> globals = [with(StringComparer.Ordinal)];

        foreach ((string global, var globalRoutes) in group.Routes)
        {
            if (globalRoutes.Routes.Count == 0)
            {
                continue;
            }

            Dictionary<string, IReadOnlyList<ProfileStep>> routes = [with(StringComparer.Ordinal)];

            foreach (var route in globalRoutes.Routes)
            {
                string name = BareVersion(route.Versions[0], group.Key);

                if (!routes.TryAdd(name, [new(route.Forward, route.Offset, null), _readGlobal]))
                {
                    return ProfileError.SameRouteName(global, name, group.Versions);
                }
            }

            globals[global] = routes;
        }

        return new ProfileDocument(
            engine,
            group.Key,
            [.. group.Versions.Select(version => BareVersion(version, group.Key))],
            group.Shapes,
            group.Offsets,
            globals);
    }

    // Cuts the key off the end of the version, because a version of a keyed group ends with the key, like 4.19.2-win-x64-shipping.
    private static string BareVersion(string version, string? key)
    {
        return key is not null && version.EndsWith($"-{key}", StringComparison.Ordinal) ? version[..^(key.Length + 1)] : version;
    }
}

/// <summary>
///     Represents 1 step of a locator route. A step is a search with its offset or a read.
/// </summary>
/// <param name="Forward">The masked bytes to search the module for or <see langword="null"/> for a read.</param>
/// <param name="Offset">The number of bytes from a match to the address operand or <see langword="null"/> for a read.</param>
/// <param name="Read">The kind of the read or <see langword="null"/> for a search.</param>
internal sealed record ProfileStep(
    string? Forward,
    int? Offset,
    string? Read);

/// <summary>
///     Serializes <see cref="ProfileDocument"/> with generated code, so the write doesn't need reflection.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ProfileDocument))]
internal sealed partial class ProfileDocumentContext : JsonSerializerContext;
