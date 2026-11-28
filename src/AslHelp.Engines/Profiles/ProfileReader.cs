using AslHelp.Engines.Profiles.Errors;
using AslHelp.Memory.Locating;
using AslHelp.Options;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AslHelp.Engines.Profiles;

/// <summary>
///     Reads the parts that the profile documents of every engine share and collects the path of each missing part for the mapper of 1 engine.
/// </summary>
/// <remarks>
///     A document is a JSON object with <c>engine</c>, an optional <c>key</c>, a list of <c>versions</c>,
///     <c>offsets</c> as an object of types with an offset per member and <c>globals</c> with a <see cref="MemoryLocator"/> per global.
///     The mapper reads anything else of its engine through <see cref="ReadObject"/>.
///     Any other key at the root fails the document. A type under <c>offsets</c> with a missing optional offset fails the document
///     when it has a member that nobody read, so a typo doesn't pass as a missing optional offset.
///     A part with the wrong JSON type counts as missing, except an offset, which fails with its value.
/// </remarks>
public sealed class ProfileReader
{
    private static readonly string[] _sharedKeys = ["engine", "globals", "key", "offsets", "versions"];

    private readonly List<string> _missing = [];
    private readonly List<string> _objectNames = [];
    private readonly HashSet<(string Type, string Member)> _readOffsets = [];
    private readonly JsonElement _root;
    private readonly HashSet<string> _typesWithOptionalMiss = [];
    private readonly Func<string, ProfileVersion?> _versionParser;

    private JsonElement? _globals;
    private ProfileDocumentError? _invalid;
    private JsonElement? _offsets;

    private ProfileReader(JsonElement root, Func<string, ProfileVersion?> versionParser)
    {
        _root = root;
        _versionParser = versionParser;
    }

    /// <summary>
    ///     Gets the value of <c>engine</c>.
    /// </summary>
    public string Engine { get; private set; } = "";

    /// <summary>
    ///     Gets the value of <c>key</c> or <see langword="null"/> when it's missing or isn't a string.
    /// </summary>
    public string? Key { get; private set; }

    /// <summary>
    ///     Gets the versions under <c>versions</c> in the order of the document, empty when they're missing.
    /// </summary>
    public IReadOnlyList<ProfileVersion> Versions { get; private set; } = [];

    /// <summary>
    ///     Reads a profile document with the mapper of an engine.
    /// </summary>
    /// <remarks>
    ///     Reads <c>engine</c>, <c>key</c> and <c>versions</c> first. A document without <c>engine</c> fails before the mapper.
    ///     Otherwise <paramref name="mapper"/> gets the reader, checks <see cref="Engine"/> and returns its own error for an engine it doesn't read.
    ///     A missing part or a bad value fails the document after a successful map too, so a mapper doesn't have to call <see cref="GetFailure"/>.
    ///     Then a key at the root that neither the reader nor the mapper read fails the document.
    ///     So does a member that nobody read in a type under <c>offsets</c> where <see cref="ReadOffsetOrNull"/> missed a member.
    /// </remarks>
    /// <typeparam name="TProfile">The profile type of the engine.</typeparam>
    /// <param name="json">The text of the document.</param>
    /// <param name="mapper">The mapper of the engine.</param>
    /// <param name="versionParser">The version parser of the engine, which returns <see langword="null"/> for text that isn't a version.</param>
    /// <returns>
    ///     The profile or a failure when the text isn't valid JSON, a version doesn't read, parts are missing,
    ///     <paramref name="mapper"/> fails or the document has unknown keys. A failure for missing parts or unknown keys lists all of them.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="json"/>, <paramref name="mapper"/> or <paramref name="versionParser"/> is <see langword="null"/>.
    /// </exception>
    public static Result<TProfile> Read<TProfile>(
        string json,
        Func<ProfileReader, Result<TProfile>> mapper,
        Func<string, ProfileVersion?> versionParser)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(versionParser);

        return ReadDocument(() => JsonDocument.Parse(json), mapper, versionParser, static (_, profile) => profile);
    }

    /// <summary>
    ///     Reads a profile document with the mapper of an engine into a catalog entry.
    /// </summary>
    /// <typeparam name="TProfile">The profile type of the engine.</typeparam>
    /// <param name="source">The file path, the resource name or the name passed with the text or stream of the document.</param>
    /// <param name="parse">The parse of the text or the stream of the document.</param>
    /// <param name="mapper">The mapper of the engine.</param>
    /// <param name="versionParser">The version parser of the engine.</param>
    /// <returns>
    ///     The entry or the failure of <see cref="Read{TProfile}"/>.
    /// </returns>
    internal static Result<ProfileEntry<TProfile>> ReadEntry<TProfile>(
        string source,
        Func<JsonDocument> parse,
        Func<ProfileReader, Result<TProfile>> mapper,
        Func<string, ProfileVersion?> versionParser)
    {
        return ReadDocument(
            parse,
            mapper,
            versionParser,
            (reader, profile) => new ProfileEntry<TProfile>(reader.Engine, reader.Key, reader.Versions, profile, source));
    }

    /// <summary>
    ///     Adds the path of a missing part that a mapper reads itself.
    /// </summary>
    /// <param name="path">The path, like <c>shapes.Thing</c>.</param>
    public void AddMissing(string path)
    {
        _missing.Add(path);
    }

    /// <summary>
    ///     Returns the 1st bad value or the missing parts so far.
    /// </summary>
    /// <returns>
    ///     The failure or <see langword="null"/> when every part read so far is there and reads.
    /// </returns>
    public ResultError? GetFailure()
    {
        return _invalid ?? (_missing.Count > 0 ? ProfileDocumentError.Missing(_missing) : null);
    }

    /// <summary>
    ///     Reads the routes of the global with the key <paramref name="name"/> under <c>globals</c>.
    /// </summary>
    /// <param name="name">The key of the global.</param>
    /// <returns>
    ///     The locator or <see langword="null"/> when the global is missing or isn't a locator.
    ///     <see cref="GetFailure"/> returns that failure. A document without <c>globals</c> lists only <c>globals</c> as missing.
    /// </returns>
    public MemoryLocator? ReadGlobal(string name)
    {
        _globals ??= FindSection("globals");

        if (_globals is not { ValueKind: JsonValueKind.Object } globals)
        {
            return null;
        }

        if (!globals.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            _missing.Add($"globals.{name}");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MemoryLocator>(value);
        }
        catch (JsonException ex)
        {
            // Catch here, because the locator converter only throws.
            _invalid ??= ProfileDocumentError.GlobalUnreadable(name, ex.Message);
            return null;
        }
    }

    /// <summary>
    ///     Reads the object with the key <paramref name="name"/> at the root.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>
    ///     A copy of the object or <see langword="null"/> when it's missing or isn't an object. The copy stays usable after the read.
    ///     <see cref="GetFailure"/> returns that failure. The key doesn't count as unknown after this call.
    /// </returns>
    public JsonElement? ReadObject(string name)
    {
        _objectNames.Add(name);

        if (FindObject(name) is { } value)
        {
            return value.Clone();
        }

        _missing.Add(name);
        return null;
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/> under <c>offsets</c>.
    /// </summary>
    /// <param name="type">The type, like <c>Thing</c>.</param>
    /// <param name="member">The member, like <c>Speed</c>.</param>
    /// <returns>
    ///     The offset or 0 when it's missing.
    ///     <see cref="GetFailure"/> returns the failure for a missing offset or an offset below 0.
    ///     A document without <c>offsets</c> lists only <c>offsets</c> as missing.
    /// </returns>
    /// <exception cref="JsonException">The offset doesn't read.</exception>
    public int ReadOffset(string type, string member)
    {
        if (!TryReadOffset(type, member, out int offset) && _offsets is { ValueKind: JsonValueKind.Object })
        {
            _missing.Add($"offsets.{type}.{member}");
        }

        return offset;
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/> under <c>offsets</c>. A document can be without it.
    /// </summary>
    /// <remarks>
    ///     When the offset is missing, a member of <paramref name="type"/> that nobody reads fails the document as an unknown key.
    /// </remarks>
    /// <param name="type">The type, like <c>Thing</c>.</param>
    /// <param name="member">The member, like <c>Speed</c>.</param>
    /// <returns>
    ///     The offset or <see langword="null"/> when it's missing. <see cref="GetFailure"/> returns the failure for an offset below 0
    ///     and for a document without <c>offsets</c>.
    /// </returns>
    /// <exception cref="JsonException">The offset doesn't read.</exception>
    public int? ReadOffsetOrNull(string type, string member)
    {
        if (TryReadOffset(type, member, out int offset))
        {
            return offset;
        }

        _ = _typesWithOptionalMiss.Add(type);
        return null;
    }

    // Build the result inside the scope of the document, because the reader points into it.
    private static Result<TResult> ReadDocument<TProfile, TResult>(
        Func<JsonDocument> parse,
        Func<ProfileReader, Result<TProfile>> mapper,
        Func<string, ProfileVersion?> versionParser,
        Func<ProfileReader, TProfile, TResult> resultSelector)
    {
        // Catch here, because JsonDocument and HexNumber throw for a bad document.
        try
        {
            using var document = parse();
            ProfileReader reader = new(document.RootElement, versionParser);

            if (reader.ReadEngineKeyAndVersions() is { } failure)
            {
                return failure;
            }

            if (!mapper(reader).TryUnwrap(out var profile, out var error))
            {
                return error;
            }

            // Check again after the map, so a mapper that skips GetFailure doesn't add a document with missing parts.
            if (reader.GetFailure() is { } mapFailure)
            {
                return mapFailure;
            }

            if (reader.FindUnknownKeys() is { } unknownKeys)
            {
                return unknownKeys;
            }

            return resultSelector(reader, profile);
        }
        catch (JsonException ex)
        {
            return ProfileDocumentError.Invalid(ex.Message);
        }
    }

    // Run after the map, because the mapper adds the objects and offsets it reads to the known keys.
    // Check the members of a type only after an optional miss in it, because measured documents have more offsets than a mapper reads.
    private ProfileDocumentError? FindUnknownKeys()
    {
        List<string>? unknownKeys = null;

        foreach (var property in _root.EnumerateObject())
        {
            if (Array.IndexOf(_sharedKeys, property.Name) < 0 && !_objectNames.Contains(property.Name))
            {
                (unknownKeys ??= []).Add(property.Name);
            }
        }

        if (_typesWithOptionalMiss.Count > 0 && _offsets is { ValueKind: JsonValueKind.Object } offsets)
        {
            foreach (var type in offsets.EnumerateObject())
            {
                if (!_typesWithOptionalMiss.Contains(type.Name) || type.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var member in type.Value.EnumerateObject())
                {
                    if (!_readOffsets.Contains((type.Name, member.Name)))
                    {
                        (unknownKeys ??= []).Add($"offsets.{type.Name}.{member.Name}");
                    }
                }
            }
        }

        return unknownKeys is null ? null : ProfileDocumentError.UnknownKeys(unknownKeys);
    }

    // Find offsets or globals on the 1st read and list it as missing once, so a mapper can read its parts in any order.
    private JsonElement FindSection(string name)
    {
        if (FindObject(name) is { } value)
        {
            return value;
        }

        _missing.Add(name);
        return default;
    }

    private JsonElement? FindObject(string name)
    {
        return _root.ValueKind == JsonValueKind.Object
            && _root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
    }

    // Read engine, key and versions. Return a failure before the mapper for a missing engine and for a version that doesn't read,
    // because the other parts depend on the mapper of the engine.
    private ProfileDocumentError? ReadEngineKeyAndVersions()
    {
        string? engine = ReadString("engine");

        if (engine is null)
        {
            _missing.Add("engine");
        }

        Engine = engine ?? "";
        Key = ReadString("key");
        ReadVersions();

        return engine is null ? ProfileDocumentError.Missing(_missing) : _invalid;
    }

    private string? ReadString(string name)
    {
        return _root.ValueKind == JsonValueKind.Object
            && _root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private void ReadVersions()
    {
        if (_root.ValueKind != JsonValueKind.Object
            || !_root.TryGetProperty("versions", out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() == 0)
        {
            _missing.Add("versions");
            return;
        }

        var versions = new ProfileVersion[value.GetArrayLength()];
        int count = 0;

        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                _missing.Add("versions");
                return;
            }

            string text = element.GetString()!;

            if (_versionParser(text) is not { } version)
            {
                _invalid ??= ProfileDocumentError.VersionInvalid(text, Engine);
                return;
            }

            versions[count++] = version;
        }

        Versions = versions;
    }

    private bool TryReadOffset(string type, string member, out int offset)
    {
        _offsets ??= FindSection("offsets");
        _ = _readOffsets.Add((type, member));

        if (_offsets is not { ValueKind: JsonValueKind.Object } offsets
            || !offsets.TryGetProperty(type, out var members)
            || members.ValueKind != JsonValueKind.Object
            || !members.TryGetProperty(member, out var value))
        {
            offset = 0;
            return false;
        }

        string path = $"offsets.{type}.{member}";
        offset = HexNumber.Read(value, path);

        // Fail a negative offset, because HexNumber takes a negative JSON number and a member never sits in front of its object.
        if (offset < 0)
        {
            _invalid ??= ProfileDocumentError.OffsetNegative(path, offset);
        }

        return true;
    }
}
