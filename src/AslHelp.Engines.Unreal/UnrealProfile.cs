using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory.Locating;
using AslHelp.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents 1 profile document of the measure tool, which lists what <see cref="UnrealGame.Attach"/> needs for the versions of 1 group.
/// </summary>
public sealed class UnrealProfile
{
    /// <summary>
    ///     The key of <c>GWorld</c> under <c>globals</c> in the documents of every engine.
    /// </summary>
    internal const string WorldGlobal = "GWorld";

    /// <summary>
    ///     The key of <c>GEngine</c> under <c>globals</c> in the documents of every engine.
    /// </summary>
    internal const string EngineGlobal = "GEngine";

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealProfile"/> class.
    /// </summary>
    /// <param name="key">The key of the group, like <c>win-x64-shipping</c>.</param>
    /// <param name="versions">The versions of the group.</param>
    /// <param name="nameTableShape">The shape of the name table.</param>
    /// <param name="nameTableLocator">The routes to the global of the name table.</param>
    /// <param name="objectArrayShape">The shape of the object array.</param>
    /// <param name="objectArrayGlobal">The key of the global of the object array under <c>globals</c>.</param>
    /// <param name="objectArrayLocator">The routes to the global of the object array.</param>
    /// <param name="objectArrayOffset">The offset of the object array inside its global.</param>
    /// <param name="propertyShape">The shape of the properties.</param>
    /// <param name="kindLayouts">The offsets that the value kinds read.</param>
    /// <param name="objectLayout">The offsets inside an object.</param>
    /// <param name="structLayout">The offsets inside a struct.</param>
    /// <param name="childLayout">The offsets inside a field on the children chain.</param>
    /// <param name="worldLocator">The routes to <c>GWorld</c>.</param>
    /// <param name="engineLocator">The routes to <c>GEngine</c>.</param>
    /// <param name="hasSubsystems"><see langword="true"/> when the versions of the group have game instance subsystems.</param>
    internal UnrealProfile(
        string? key,
        IReadOnlyList<string> versions,
        NameTableShape nameTableShape,
        MemoryLocator nameTableLocator,
        ObjectArrayShape objectArrayShape,
        string objectArrayGlobal,
        MemoryLocator objectArrayLocator,
        int objectArrayOffset,
        PropertyShape propertyShape,
        KindLayouts kindLayouts,
        ObjectLayout objectLayout,
        StructLayout structLayout,
        ChildLayout childLayout,
        MemoryLocator worldLocator,
        MemoryLocator engineLocator,
        bool hasSubsystems)
    {
        Key = key;
        Versions = versions;
        NameTableShape = nameTableShape;
        NameTableLocator = nameTableLocator;
        ObjectArrayShape = objectArrayShape;
        ObjectArrayGlobal = objectArrayGlobal;
        ObjectArrayLocator = objectArrayLocator;
        ObjectArrayOffset = objectArrayOffset;
        PropertyShape = propertyShape;
        KindLayouts = kindLayouts;
        ObjectLayout = objectLayout;
        StructLayout = structLayout;
        ChildLayout = childLayout;
        WorldLocator = worldLocator;
        EngineLocator = engineLocator;
        HasSubsystems = hasSubsystems;
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
    ///     Gets the routes to <c>GEngine</c>.
    /// </summary>
    internal MemoryLocator EngineLocator { get; }

    /// <summary>
    ///     Gets whether the versions of the group have game instance subsystems, which came with 4.22.
    /// </summary>
    internal bool HasSubsystems { get; }

    /// <summary>
    ///     Gets the offsets that the value kinds read, each a failure when the document doesn't have it.
    /// </summary>
    internal KindLayouts KindLayouts { get; }

    /// <summary>
    ///     Gets the routes to the global of the name table, whose key under <c>globals</c> is <see cref="NameTableShape.Global"/>.
    /// </summary>
    internal MemoryLocator NameTableLocator { get; }

    /// <summary>
    ///     Gets the shape of the name table.
    /// </summary>
    internal NameTableShape NameTableShape { get; }

    /// <summary>
    ///     Gets the key of the global of the object array under <c>globals</c>, like <c>GUObjectArray</c> or <c>GObjObjects</c>.
    /// </summary>
    internal string ObjectArrayGlobal { get; }

    /// <summary>
    ///     Gets the routes to the global of the object array.
    /// </summary>
    internal MemoryLocator ObjectArrayLocator { get; }

    /// <summary>
    ///     Gets the offset of the object array inside its global.
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
    ///     Reads <c>unreal3</c> and <c>unreal4</c> documents and maps each by its <c>engine</c>.
    ///     Reads only the shapes, offsets and globals that attach and the value kinds need, so a document can have more.
    ///     A part with the wrong JSON type counts as missing, except an offset, which fails with its value.
    ///     A document without the offsets of a value kind, like the mask of a bool, still parses and only reads of that kind fail.
    /// </remarks>
    /// <param name="json">The text of the document.</param>
    /// <returns>
    ///     The profile or a failure when the text isn't valid JSON, the engine or a shape is unknown, a value doesn't read or parts are missing.
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
///     Reads the parts that the profile documents of every engine share and collects the path of each missing part.
/// </summary>
/// <param name="root">The root of the document.</param>
file sealed class ProfileReader(
    JsonElement root)
{
    // List 1 mapper per engine, because the measure tool writes a different kind of document for each engine.
    private static readonly ProfileMapper[] _mappers = [new Unreal3ProfileMapper(), new Unreal4ProfileMapper()];

    private readonly List<string> _missing = [];
    private readonly JsonElement _root = root;

    private JsonElement _globals;
    private ProfileError? _invalid;
    private JsonElement _offsets;

    /// <summary>
    ///     Gets the versions of the group or <see langword="null"/> when they're missing.
    /// </summary>
    public string[]? Versions { get; private set; }

    /// <summary>
    ///     Reads the engine and the versions and maps the document with the mapper of its engine.
    /// </summary>
    /// <returns>
    ///     The profile or a failure for an unknown engine, an unknown shape, a bad value or missing parts.
    /// </returns>
    /// <exception cref="JsonException">An offset doesn't read.</exception>
    public Result<UnrealProfile> Read()
    {
        string? engine = ReadEngine();
        var mapper = engine is null ? null : Array.Find(_mappers, candidate => string.Equals(candidate.Engine, engine, StringComparison.Ordinal));

        if (engine is not null && mapper is null)
        {
            return ProfileError.UnknownEngine(engine, string.Join(", ", _mappers.Select(static candidate => candidate.Engine)));
        }

        Versions = ReadVersions();

        if (mapper is null)
        {
            // List only the parts that every document needs, because the other parts depend on the engine.
            _ = ReadOffsetsAndGlobals();

            return MissingError();
        }

        return mapper.Map(this);
    }

    /// <summary>
    ///     Adds the path of a missing part that a mapper reads itself.
    /// </summary>
    /// <param name="path">The path, like <c>shapes.FField</c>.</param>
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
    public ProfileError? GetFailure()
    {
        return _invalid ?? (_missing.Count > 0 ? ProfileError.Missing(_missing) : null);
    }

    /// <summary>
    ///     Creates the error for the parts missing so far.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public ProfileError MissingError()
    {
        return ProfileError.Missing(_missing);
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/> under <c>offsets</c>.
    /// </summary>
    /// <param name="type">The type, like <c>UField</c>.</param>
    /// <param name="member">The member, like <c>Next</c>.</param>
    /// <returns>
    ///     The offset or 0 when it's missing.
    ///     <see cref="GetFailure"/> returns the failure for a missing offset or an offset below 0.
    /// </returns>
    /// <exception cref="JsonException">The offset doesn't read.</exception>
    public int ReadOffset(string type, string member)
    {
        if (!TryReadOffset(type, member, out int offset))
        {
            _missing.Add($"offsets.{type}.{member}");
        }

        return offset;
    }

    /// <summary>
    ///     Reads the offsets of every value kind. A document can be without them.
    /// </summary>
    /// <remarks>
    ///     Doesn't add a missing offset to the parts that <see cref="GetFailure"/> lists, so the document still parses.
    /// </remarks>
    /// <param name="propertyPrefix">The prefix of the property classes, <c>U</c> or <c>F</c>.</param>
    /// <param name="nameIndexMember">The member of the name id in <c>FName</c>, <c>Index</c> or <c>ComparisonIndex</c>.</param>
    /// <param name="bitMaskBools"><see langword="true"/> when a bool reads with 1 4-byte mask.</param>
    /// <returns>
    ///     The layout of each kind or a failure with the class and every missing offset.
    /// </returns>
    /// <exception cref="JsonException">An offset doesn't read.</exception>
    public KindLayouts ReadKindLayouts(string propertyPrefix, string nameIndexMember, bool bitMaskBools)
    {
        return KindLayouts.Read(new KindProfile(propertyPrefix, nameIndexMember, bitMaskBools, ReadOffsetOrNull));
    }

    /// <summary>
    ///     Reads the offset of <paramref name="member"/> in <paramref name="type"/> under <c>offsets</c>. A document can be without it.
    /// </summary>
    /// <param name="type">The type, like <c>FUObjectItem</c>.</param>
    /// <param name="member">The member, like <c>SerialNumber</c>.</param>
    /// <returns>
    ///     The offset or <see langword="null"/> when it's missing. <see cref="GetFailure"/> returns the failure for an offset below 0.
    /// </returns>
    /// <exception cref="JsonException">The offset doesn't read.</exception>
    public int? ReadOffsetOrNull(string type, string member)
    {
        return TryReadOffset(type, member, out int offset) ? offset : null;
    }

    /// <summary>
    ///     Reads the key of the group.
    /// </summary>
    /// <returns>
    ///     The key or <see langword="null"/> when it's missing or isn't a string.
    /// </returns>
    public string? ReadKey()
    {
        return _root.TryGetProperty("key", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    ///     Reads the routes of the global with the key <paramref name="name"/> under <c>globals</c>.
    /// </summary>
    /// <param name="name">The key of the global.</param>
    /// <returns>
    ///     The locator or <see langword="null"/> when the global is missing or isn't a locator.
    ///     <see cref="GetFailure"/> returns that failure.
    /// </returns>
    public MemoryLocator? ReadGlobal(string name)
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

    /// <summary>
    ///     Reads the object with the key <paramref name="name"/> at the root.
    /// </summary>
    /// <param name="name">The key.</param>
    /// <returns>
    ///     The object or <see langword="null"/> when it's missing or isn't an object.
    ///     <see cref="GetFailure"/> returns that failure.
    /// </returns>
    public JsonElement? ReadObject(string name)
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

    /// <summary>
    ///     Reads <c>offsets</c> and <c>globals</c> for <see cref="ReadOffset"/> and <see cref="ReadGlobal"/>.
    /// </summary>
    /// <returns>
    ///     <see langword="true"/> when both are objects.
    /// </returns>
    public bool ReadOffsetsAndGlobals()
    {
        var offsets = ReadObject("offsets");
        var globals = ReadObject("globals");

        _offsets = offsets.GetValueOrDefault();
        _globals = globals.GetValueOrDefault();

        return offsets is not null && globals is not null;
    }

    private bool TryReadOffset(string type, string member, out int offset)
    {
        if (!_offsets.TryGetProperty(type, out var members)
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
            _invalid ??= ProfileError.OffsetNegative(path, offset);
        }

        return true;
    }

    private string? ReadEngine()
    {
        if (_root.ValueKind == JsonValueKind.Object
            && _root.TryGetProperty("engine", out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        _missing.Add("engine");
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

/// <summary>
///     Represents the mapping of the documents of 1 engine to the shapes and layouts of an <see cref="UnrealProfile"/>.
/// </summary>
file abstract class ProfileMapper
{
    /// <summary>
    ///     Gets the value of <c>engine</c> in the documents that the mapper reads.
    /// </summary>
    public abstract string Engine { get; }

    /// <summary>
    ///     Maps the document of <paramref name="reader"/> after the reader read its engine and versions.
    /// </summary>
    /// <param name="reader">The reader of the document.</param>
    /// <returns>
    ///     The profile or a failure for an unknown shape, a bad value or missing parts.
    /// </returns>
    /// <exception cref="JsonException">An offset doesn't read.</exception>
    public abstract Result<UnrealProfile> Map(ProfileReader reader);
}

file sealed class Unreal3ProfileMapper : ProfileMapper
{
    private const string ObjectArrayGlobal = "GObjObjects";

    public override string Engine => "unreal3";

    public override Result<UnrealProfile> Map(ProfileReader reader)
    {
        // Return before the offsets when offsets or globals is missing, because there's nothing to read them from.
        if (!reader.ReadOffsetsAndGlobals())
        {
            return reader.MissingError();
        }

        ObjectLayout objectLayout = new(
            reader.ReadOffset("UObject", "Outer"),
            reader.ReadOffset("UObject", "Name"),
            reader.ReadOffset("UObject", "Class"));
        StructLayout structLayout = new(reader.ReadOffset("UStruct", "SuperField"), reader.ReadOffset("UStruct", "Children"));
        ChildLayout childLayout = new(reader.ReadOffset("UField", "Next"));
        var nameTableShape = NameTableShape.Flat(
            new NameEntryLayout(reader.ReadOffset("FNameEntry", "Index"), reader.ReadOffset("FNameEntry", "Name")));
        var propertyShape = PropertyShape.Object(
            new PropertyLayout(
                reader.ReadOffset("UProperty", "ArrayDim"),
                reader.ReadOffset("UProperty", "ElementSize"),
                reader.ReadOffset("UProperty", "Offset")));

        var kindLayouts = reader.ReadKindLayouts("U", "Index", bitMaskBools: true);
        var nameTableLocator = reader.ReadGlobal(nameTableShape.Global);
        var objectArrayLocator = reader.ReadGlobal(ObjectArrayGlobal);
        var worldLocator = reader.ReadGlobal(UnrealProfile.WorldGlobal);
        var engineLocator = reader.ReadGlobal(UnrealProfile.EngineGlobal);

        if (reader.GetFailure() is { } failure)
        {
            return failure;
        }

        // Read the objects at offset 0, because GObjObjects is the array of object pointers itself.
        return new UnrealProfile(
            reader.ReadKey(),
            reader.Versions!,
            nameTableShape,
            nameTableLocator!,
            ObjectArrayShape.Pointer(),
            ObjectArrayGlobal,
            objectArrayLocator!,
            0,
            propertyShape,
            kindLayouts,
            objectLayout,
            structLayout,
            childLayout,
            worldLocator!,
            engineLocator!,
            hasSubsystems: false);
    }
}

file sealed class Unreal4ProfileMapper : ProfileMapper
{
    private const string IndirectArrayType = "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>";
    private const string ObjectArrayGlobal = "GUObjectArray";

    public override string Engine => "unreal4";

    public override Result<UnrealProfile> Map(ProfileReader reader)
    {
        var shapes = reader.ReadObject("shapes");
        bool hasParts = reader.ReadOffsetsAndGlobals();

        string? nameTable = ReadShape(reader, shapes, "FNameEntry");
        string? objectArray = ReadShape(reader, shapes, "FUObjectArray");
        string? property = ReadShape(reader, shapes, "FField");

        // Return before the offsets when a part or a shape is missing, because the offsets a document needs depend on its shapes.
        if (!hasParts || nameTable is null || objectArray is null || property is null)
        {
            return reader.MissingError();
        }

        int objectArrayOffset = reader.ReadOffset("FUObjectArray", "ObjObjects");
        ObjectLayout objectLayout = new(
            reader.ReadOffset("UObjectBase", "OuterPrivate"),
            reader.ReadOffset("UObjectBase", "NamePrivate"),
            reader.ReadOffset("UObjectBase", "ClassPrivate"));
        StructLayout structLayout = new(reader.ReadOffset("UStruct", "SuperStruct"), reader.ReadOffset("UStruct", "Children"));
        ChildLayout childLayout = new(reader.ReadOffset("UField", "Next"));

        if (!ReadNameTable(reader, nameTable).TryUnwrap(out var nameTableShape, out var error)
            || !ReadObjectArray(reader, objectArray).TryUnwrap(out var objectArrayShape, out error)
            || !ReadProperties(reader, property).TryUnwrap(out var propertyShape, out error))
        {
            return error;
        }

        // Read the property members with the prefix of the property shape, because the kinds are UBoolProperty and the like through 4.24
        // and FBoolProperty and the like from 4.25.
        var kindLayouts = reader.ReadKindLayouts(property == "ffield" ? "F" : "U", "ComparisonIndex", bitMaskBools: false);
        var nameTableLocator = reader.ReadGlobal(nameTableShape.Global);
        var objectArrayLocator = reader.ReadGlobal(ObjectArrayGlobal);
        var worldLocator = reader.ReadGlobal(UnrealProfile.WorldGlobal);
        var engineLocator = reader.ReadGlobal(UnrealProfile.EngineGlobal);

        // Take the subsystem collection of the game instance as the sign of subsystems, because only 4.22 and later have it.
        bool hasSubsystems = reader.ReadOffsetOrNull("UGameInstance", "SubsystemCollection") is not null;

        if (reader.GetFailure() is { } failure)
        {
            return failure;
        }

        return new UnrealProfile(
            reader.ReadKey(),
            reader.Versions!,
            nameTableShape,
            nameTableLocator!,
            objectArrayShape,
            ObjectArrayGlobal,
            objectArrayLocator!,
            objectArrayOffset,
            propertyShape,
            kindLayouts,
            objectLayout,
            structLayout,
            childLayout,
            worldLocator!,
            engineLocator!,
            hasSubsystems);
    }

    // Read the serial number as optional, because only weak pointers read it.
    private static ObjectItemLayout ReadItem(ProfileReader reader)
    {
        return new ObjectItemLayout(
            reader.ReadOffset("FUObjectItem", "Object"),
            reader.ReadOffset("FUObjectItem", "sizeof"),
            reader.ReadOffsetOrNull("FUObjectItem", "SerialNumber"));
    }

    private static Result<NameTableShape> ReadNameTable(ProfileReader reader, string shape)
    {
        return shape switch
        {
            "chunked" => NameTableShape.Chunked(),
            "indirect" => NameTableShape.Indirect(
                new NameEntryLayout(reader.ReadOffset("FNameEntry", "Index"), reader.ReadOffset("FNameEntry", "AnsiName"))),
            _ => ProfileError.UnknownShape("FNameEntry", shape, "chunked, indirect"),
        };
    }

    private static Result<ObjectArrayShape> ReadObjectArray(ProfileReader reader, string shape)
    {
        return shape switch
        {
            "chunked" => ObjectArrayShape.Chunked(
                new ChunkedObjectArrayLayout(
                    reader.ReadOffset("FChunkedFixedUObjectArray", "Objects"),
                    reader.ReadOffset("FChunkedFixedUObjectArray", "NumElements"),
                    reader.ReadOffset("FChunkedFixedUObjectArray", "NumChunks"),
                    reader.ReadOffset("FChunkedFixedUObjectArray", "NumElementsPerChunk")),
                ReadItem(reader)),
            "flat" => ObjectArrayShape.Item(
                new ItemObjectArrayLayout(reader.ReadOffset("FFixedUObjectArray", "Objects"), reader.ReadOffset("FFixedUObjectArray", "NumElements")),
                ReadItem(reader)),
            "indirect" => ObjectArrayShape.Indirect(
                new IndirectObjectArrayLayout(
                    reader.ReadOffset(IndirectArrayType, "Chunks"),
                    reader.ReadOffset(IndirectArrayType, "NumElements"),
                    reader.ReadOffset(IndirectArrayType, "NumChunks"))),
            "pointers" => ObjectArrayShape.Pointer(),
            _ => ProfileError.UnknownShape("FUObjectArray", shape, "chunked, flat, indirect, pointers"),
        };
    }

    private static Result<PropertyShape> ReadProperties(ProfileReader reader, string shape)
    {
        return shape switch
        {
            "ffield" => PropertyShape.Field(
                new FieldStructLayout(reader.ReadOffset("UStruct", "ChildProperties")),
                new FieldPropertyLayout(
                    reader.ReadOffset("FField", "ClassPrivate"),
                    reader.ReadOffset("FField", "Next"),
                    reader.ReadOffset("FField", "NamePrivate")),
                new FieldClassLayout(reader.ReadOffset("FFieldClass", "Name")),
                new PropertyLayout(
                    reader.ReadOffset("FProperty", "ArrayDim"),
                    reader.ReadOffset("FProperty", "ElementSize"),
                    reader.ReadOffset("FProperty", "Offset_Internal"))),
            "uproperty" => PropertyShape.Object(
                new PropertyLayout(
                    reader.ReadOffset("UProperty", "ArrayDim"),
                    reader.ReadOffset("UProperty", "ElementSize"),
                    reader.ReadOffset("UProperty", "Offset_Internal"))),
            _ => ProfileError.UnknownShape("FField", shape, "ffield, uproperty"),
        };
    }

    private static string? ReadShape(ProfileReader reader, JsonElement? shapes, string key)
    {
        if (shapes is not { } members)
        {
            return null;
        }

        if (members.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        reader.AddMissing($"shapes.{key}");
        return null;
    }
}
