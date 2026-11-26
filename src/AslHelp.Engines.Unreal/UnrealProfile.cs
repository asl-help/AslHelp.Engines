using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory.Locating;
using AslHelp.Options;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents 1 profile document of the measure tool, which lists what <see cref="UnrealGame.Attach"/> needs for the versions of 1 group.
/// </summary>
public sealed class UnrealProfile
{
    /// <summary>
    ///     The key of <c>GUObjectArray</c> under <c>globals</c>.
    /// </summary>
    internal const string ObjectArrayGlobal = "GUObjectArray";

    /// <summary>
    ///     The key of <c>GWorld</c> under <c>globals</c>.
    /// </summary>
    internal const string WorldGlobal = "GWorld";

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealProfile"/> class.
    /// </summary>
    /// <param name="key">The key of the group, like <c>win-x64-shipping</c>.</param>
    /// <param name="versions">The versions of the group.</param>
    /// <param name="nameTableShape">The shape of the name table.</param>
    /// <param name="nameTableLocator">The routes to the global of the name table.</param>
    /// <param name="objectArrayShape">The shape of the object array.</param>
    /// <param name="objectArrayLocator">The routes to <c>GUObjectArray</c>.</param>
    /// <param name="objectArrayOffset">The offset of the object array inside <c>GUObjectArray</c>.</param>
    /// <param name="propertyShape">The shape of the properties.</param>
    /// <param name="objectLayout">The offsets inside an object.</param>
    /// <param name="structLayout">The offsets inside a struct.</param>
    /// <param name="childLayout">The offsets inside a field on the children chain.</param>
    /// <param name="worldLocator">The routes to <c>GWorld</c>.</param>
    internal UnrealProfile(
        string? key,
        IReadOnlyList<string> versions,
        NameTableShape nameTableShape,
        MemoryLocator nameTableLocator,
        ObjectArrayShape objectArrayShape,
        MemoryLocator objectArrayLocator,
        int objectArrayOffset,
        PropertyShape propertyShape,
        ObjectLayout objectLayout,
        StructLayout structLayout,
        ChildLayout childLayout,
        MemoryLocator worldLocator)
    {
        Key = key;
        Versions = versions;
        NameTableShape = nameTableShape;
        NameTableLocator = nameTableLocator;
        ObjectArrayShape = objectArrayShape;
        ObjectArrayLocator = objectArrayLocator;
        ObjectArrayOffset = objectArrayOffset;
        PropertyShape = propertyShape;
        ObjectLayout = objectLayout;
        StructLayout = structLayout;
        ChildLayout = childLayout;
        WorldLocator = worldLocator;
    }

    /// <summary>
    ///     Gets the key of the group like <c>win-x64-shipping</c> or <see langword="null"/> for a document without a key.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    ///     Gets the versions of the group in the order of the document.
    /// </summary>
    public IReadOnlyList<string> Versions { get; }

    /// <summary>
    ///     Gets the offsets inside a field on the children chain of a struct.
    /// </summary>
    internal ChildLayout ChildLayout { get; }

    /// <summary>
    ///     Gets the routes to the global of the name table, whose key under <c>globals</c> is <see cref="NameTableShape.Global"/>.
    /// </summary>
    internal MemoryLocator NameTableLocator { get; }

    /// <summary>
    ///     Gets the shape of the name table.
    /// </summary>
    internal NameTableShape NameTableShape { get; }

    /// <summary>
    ///     Gets the routes to <c>GUObjectArray</c>.
    /// </summary>
    internal MemoryLocator ObjectArrayLocator { get; }

    /// <summary>
    ///     Gets the offset of the object array inside <c>GUObjectArray</c>.
    /// </summary>
    internal int ObjectArrayOffset { get; }

    /// <summary>
    ///     Gets the shape of the object array.
    /// </summary>
    internal ObjectArrayShape ObjectArrayShape { get; }

    /// <summary>
    ///     Gets the offsets inside an object.
    /// </summary>
    internal ObjectLayout ObjectLayout { get; }

    /// <summary>
    ///     Gets the shape of the properties.
    /// </summary>
    internal PropertyShape PropertyShape { get; }

    /// <summary>
    ///     Gets the offsets inside a struct.
    /// </summary>
    internal StructLayout StructLayout { get; }

    /// <summary>
    ///     Gets the routes to <c>GWorld</c>.
    /// </summary>
    internal MemoryLocator WorldLocator { get; }

    /// <summary>
    ///     Reads a profile document.
    /// </summary>
    /// <remarks>
    ///     Reads only the shapes, offsets and globals that attach needs, so a document can have more.
    ///     A part with the wrong JSON type counts as missing, except an offset, which fails with its value.
    /// </remarks>
    /// <param name="json">The text of the document.</param>
    /// <returns>
    ///     The profile or a failure when the text isn't valid JSON, a value doesn't read, a shape is unknown or parts are missing.
    ///     A failure for missing offsets and globals lists all of them.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    public static Result<UnrealProfile> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Catch here, because JsonDocument and HexNumber throw for a bad document.
        try
        {
            using var document = JsonDocument.Parse(json);

            return new ProfileReader(document.RootElement).Read();
        }
        catch (JsonException ex)
        {
            return ProfileError.Invalid(ex.Message);
        }
    }
}

/// <summary>
///     Reads the parts of 1 profile document and collects the path of each missing part.
/// </summary>
/// <param name="root">The root of the document.</param>
file sealed class ProfileReader(
    JsonElement root)
{
    private const string IndirectArrayType = "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>";

    private readonly List<string> _missing = [];
    private readonly JsonElement _root = root;

    private JsonElement _globals;
    private ProfileError? _invalid;
    private JsonElement _offsets;

    /// <summary>
    ///     Reads the profile.
    /// </summary>
    /// <returns>
    ///     The profile or a failure for an unknown shape, a bad value or missing parts.
    /// </returns>
    /// <exception cref="JsonException">An offset doesn't read.</exception>
    public Result<UnrealProfile> Read()
    {
        var versions = ReadVersions();
        int missingVersions = _missing.Count;
        var shapes = ReadObject("shapes");
        _offsets = ReadObject("offsets").GetValueOrDefault();
        _globals = ReadObject("globals").GetValueOrDefault();

        string? nameTable = ReadShape(shapes, "FNameEntry");
        string? objectArray = ReadShape(shapes, "FUObjectArray");
        string? property = ReadShape(shapes, "FField");

        // Return before the offsets when a part or a shape is missing, because the offsets a document needs depend on its shapes.
        if (_missing.Count > missingVersions)
        {
            return ProfileError.Missing(_missing);
        }

        int objectArrayOffset = Offset("FUObjectArray", "ObjObjects");
        ObjectLayout objectLayout = new(
            Offset("UObjectBase", "OuterPrivate"),
            Offset("UObjectBase", "NamePrivate"),
            Offset("UObjectBase", "ClassPrivate"));
        StructLayout structLayout = new(Offset("UStruct", "SuperStruct"), Offset("UStruct", "Children"));
        ChildLayout childLayout = new(Offset("UField", "Next"));

        if (!ReadNameTable(nameTable!).TryUnwrap(out var nameTableShape, out var error)
            || !ReadObjectArray(objectArray!).TryUnwrap(out var objectArrayShape, out error)
            || !ReadProperties(property!).TryUnwrap(out var propertyShape, out error))
        {
            return error;
        }

        var nameTableLocator = ReadGlobal(nameTableShape.Global);
        var objectArrayLocator = ReadGlobal(UnrealProfile.ObjectArrayGlobal);
        var worldLocator = ReadGlobal(UnrealProfile.WorldGlobal);

        if (_invalid is { } invalid)
        {
            return invalid;
        }

        if (_missing.Count > 0)
        {
            return ProfileError.Missing(_missing);
        }

        string? key = _root.TryGetProperty("key", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        return new UnrealProfile(
            key,
            versions!,
            nameTableShape,
            nameTableLocator!,
            objectArrayShape,
            objectArrayLocator!,
            objectArrayOffset,
            propertyShape,
            objectLayout,
            structLayout,
            childLayout,
            worldLocator!);
    }

    private int Offset(string type, string member)
    {
        if (!_offsets.TryGetProperty(type, out var members)
            || members.ValueKind != JsonValueKind.Object
            || !members.TryGetProperty(member, out var value))
        {
            _missing.Add($"offsets.{type}.{member}");
            return 0;
        }

        string path = $"offsets.{type}.{member}";
        int offset = HexNumber.Read(value, path);

        // Fail a negative offset, because HexNumber takes a negative JSON number and a member never sits in front of its object.
        if (offset < 0)
        {
            _invalid ??= ProfileError.OffsetNegative(path, offset);
        }

        return offset;
    }

    private MemoryLocator? ReadGlobal(string name)
    {
        if (!_globals.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
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
            _invalid ??= ProfileError.GlobalUnreadable(name, ex.Message);
            return null;
        }
    }

    private ObjectItemLayout ReadItem()
    {
        return new ObjectItemLayout(Offset("FUObjectItem", "Object"), Offset("FUObjectItem", "sizeof"));
    }

    private Result<NameTableShape> ReadNameTable(string shape)
    {
        return shape switch
        {
            "chunked" => NameTableShape.Chunked(),
            "indirect" => NameTableShape.Indirect(new NameEntryLayout(Offset("FNameEntry", "Index"), Offset("FNameEntry", "AnsiName"))),
            _ => ProfileError.UnknownShape("FNameEntry", shape, "chunked, indirect"),
        };
    }

    private JsonElement? ReadObject(string name)
    {
        if (_root.ValueKind == JsonValueKind.Object
            && _root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        _missing.Add(name);
        return null;
    }

    private Result<ObjectArrayShape> ReadObjectArray(string shape)
    {
        return shape switch
        {
            "chunked" => ObjectArrayShape.Chunked(
                new ChunkedObjectArrayLayout(
                    Offset("FChunkedFixedUObjectArray", "Objects"),
                    Offset("FChunkedFixedUObjectArray", "NumElements"),
                    Offset("FChunkedFixedUObjectArray", "NumChunks"),
                    Offset("FChunkedFixedUObjectArray", "NumElementsPerChunk")),
                ReadItem()),
            "flat" => ObjectArrayShape.Item(
                new ItemObjectArrayLayout(Offset("FFixedUObjectArray", "Objects"), Offset("FFixedUObjectArray", "NumElements")),
                ReadItem()),
            "indirect" => ObjectArrayShape.Indirect(
                new IndirectObjectArrayLayout(
                    Offset(IndirectArrayType, "Chunks"),
                    Offset(IndirectArrayType, "NumElements"),
                    Offset(IndirectArrayType, "NumChunks"))),
            "pointers" => ObjectArrayShape.Pointer(),
            _ => ProfileError.UnknownShape("FUObjectArray", shape, "chunked, flat, indirect, pointers"),
        };
    }

    private Result<PropertyShape> ReadProperties(string shape)
    {
        return shape switch
        {
            "ffield" => PropertyShape.Field(
                new FieldStructLayout(Offset("UStruct", "ChildProperties")),
                new FieldPropertyLayout(Offset("FField", "ClassPrivate"), Offset("FField", "Next"), Offset("FField", "NamePrivate")),
                new FieldClassLayout(Offset("FFieldClass", "Name")),
                new PropertyLayout(Offset("FProperty", "ArrayDim"), Offset("FProperty", "ElementSize"), Offset("FProperty", "Offset_Internal"))),
            "uproperty" => PropertyShape.Object(
                new PropertyLayout(Offset("UProperty", "ArrayDim"), Offset("UProperty", "ElementSize"), Offset("UProperty", "Offset_Internal"))),
            _ => ProfileError.UnknownShape("FField", shape, "ffield, uproperty"),
        };
    }

    private string? ReadShape(JsonElement? shapes, string key)
    {
        if (shapes is not { } members)
        {
            return null;
        }

        if (members.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        _missing.Add($"shapes.{key}");
        return null;
    }

    private string[]? ReadVersions()
    {
        if (_root.ValueKind == JsonValueKind.Object
            && _root.TryGetProperty("versions", out var value)
            && value.ValueKind == JsonValueKind.Array
            && value.GetArrayLength() > 0)
        {
            string[] versions = new string[value.GetArrayLength()];
            int count = 0;

            foreach (var version in value.EnumerateArray())
            {
                if (version.ValueKind != JsonValueKind.String)
                {
                    break;
                }

                versions[count++] = version.GetString()!;
            }

            if (count == versions.Length)
            {
                return versions;
            }
        }

        _missing.Add("versions");
        return null;
    }
}
