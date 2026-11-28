using AslHelp.Engines.Profiles;
using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents 1 profile document of the measure tool,
///     which lists what <see cref="UnrealGame.Attach(IMemoryReader, MemoryModule, UnrealProfile, ValueBounds)"/> needs for the versions of 1 group.
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
    /// <param name="engine">The engine of the document, <see cref="UnrealTarget.Unreal3"/> or <see cref="UnrealTarget.Unreal4"/>.</param>
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
    /// <param name="subsystemMapOffset">The offset of the subsystem map in the game instance from 4.22 on, otherwise <see langword="null"/>.</param>
    /// <param name="serialNumberBlocksLocator">The routes to the serial number blocks of 4.0 to 4.10 when the document has them.</param>
    internal UnrealProfile(
        string engine,
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
        int? subsystemMapOffset,
        MemoryLocator? serialNumberBlocksLocator = null)
    {
        Engine = engine;
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
        SubsystemMapOffset = subsystemMapOffset;
        SerialNumberBlocksLocator = serialNumberBlocksLocator;
    }

    /// <summary>
    ///     Gets the engine of the document, <see cref="UnrealTarget.Unreal3"/> or <see cref="UnrealTarget.Unreal4"/>.
    /// </summary>
    public string Engine { get; }

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
    ///     Gets the offset of the subsystem map in the game instance or <see langword="null"/> for versions before 4.22, which don't have subsystems.
    /// </summary>
    internal int? SubsystemMapOffset { get; }

    /// <summary>
    ///     Gets the routes to the serial number blocks of 4.0 to 4.10 when the document has them, otherwise <see langword="null"/>.
    /// </summary>
    internal MemoryLocator? SerialNumberBlocksLocator { get; }

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
    ///     Creates a catalog with the documents that ship with the library, 1 per group of the measured players.
    /// </summary>
    /// <remarks>
    ///     Maps all of them on each call. Add your own documents after, so they win over the shipped documents for the versions they list.
    /// </remarks>
    /// <returns>
    ///     The catalog.
    /// </returns>
    public static ProfileCatalog<UnrealProfile> CreateCatalog()
    {
        ProfileCatalog<UnrealProfile> catalog = new(Map, UnrealTarget.ParseVersion);
        catalog.AddEmbedded(typeof(UnrealProfile).Assembly, "Profiles/").Unwrap();

        return catalog;
    }

    /// <summary>
    ///     Reads a profile document.
    /// </summary>
    /// <remarks>
    ///     Reads <c>unreal3</c> and <c>unreal4</c> documents and maps each by its <c>engine</c>.
    ///     Reads only the shapes, offsets and globals that attach and the value kinds need, so a document can have more of them.
    ///     Any other key at the root fails, like <c>shapes</c> in an <c>unreal3</c> document.
    ///     A part with the wrong JSON type counts as missing, except an offset, which fails with its value.
    ///     A document without the offsets of a value kind, like the mask of a bool, still parses and only reads of that kind fail.
    /// </remarks>
    /// <param name="json">The text of the document.</param>
    /// <returns>
    ///     The profile or a failure when the text isn't valid JSON, the engine, a shape or a key is unknown,
    ///     a value doesn't read or parts are missing.
    ///     A failure for missing offsets and globals lists all of them.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="json"/> is <see langword="null"/>.
    /// </exception>
    public static Result<UnrealProfile> Parse(string json)
    {
        return ProfileReader.Read(json, Map, UnrealTarget.ParseVersion);
    }

    /// <summary>
    ///     Maps a document with the mapper of its <c>engine</c>.
    /// </summary>
    /// <param name="reader">The reader of the document.</param>
    /// <returns>
    ///     The profile or a failure for an unknown engine, an unknown shape, a bad value or missing parts.
    /// </returns>
    /// <exception cref="JsonException">An offset doesn't read.</exception>
    internal static Result<UnrealProfile> Map(ProfileReader reader)
    {
        return ProfileMappers.Find(reader.Engine) is { } mapper
            ? mapper.Map(reader)
            : ProfileMapError.UnknownEngine(reader.Engine, ProfileMappers.Known);
    }
}

/// <summary>
///     Provides the mapper of each engine.
/// </summary>
file static class ProfileMappers
{
    // List 1 mapper per engine, because the measure tool writes a different kind of document for each engine.
    private static readonly ProfileMapper[] _mappers = [new Unreal3ProfileMapper(), new Unreal4ProfileMapper()];

    /// <summary>
    ///     Gets the engine of every mapper, joined for a message.
    /// </summary>
    public static string Known => string.Join(", ", _mappers.Select(static mapper => mapper.Engine));

    /// <summary>
    ///     Finds the mapper of <paramref name="engine"/>.
    /// </summary>
    /// <param name="engine">The value of <c>engine</c>.</param>
    /// <returns>
    ///     The mapper or <see langword="null"/> for an engine without a mapper.
    /// </returns>
    public static ProfileMapper? Find(string engine)
    {
        return Array.Find(_mappers, mapper => string.Equals(mapper.Engine, engine, StringComparison.Ordinal));
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

    /// <summary>
    ///     Reads the offsets of every value kind. A document can be without them.
    /// </summary>
    /// <remarks>
    ///     Doesn't add a missing offset to the parts that <see cref="ProfileReader.GetFailure"/> lists, so the document still parses.
    /// </remarks>
    /// <param name="reader">The reader of the document.</param>
    /// <param name="propertyPrefix">The prefix of the property classes, <c>U</c> or <c>F</c>.</param>
    /// <param name="nameIndexMember">The member of the name id in <c>FName</c>, <c>Index</c> or <c>ComparisonIndex</c>.</param>
    /// <param name="bitMaskBools"><see langword="true"/> when a bool reads with 1 4-byte mask.</param>
    /// <returns>
    ///     The layout of each kind or a failure with the class and every missing offset.
    /// </returns>
    /// <exception cref="JsonException">An offset doesn't read.</exception>
    protected static KindLayouts ReadKindLayouts(ProfileReader reader, string propertyPrefix, string nameIndexMember, bool bitMaskBools)
    {
        return KindLayouts.Read(new KindProfile(propertyPrefix, nameIndexMember, bitMaskBools, reader.ReadOffsetOrNull));
    }
}

file sealed class Unreal3ProfileMapper : ProfileMapper
{
    private const string ObjectArrayGlobal = "GObjObjects";

    public override string Engine => UnrealTarget.Unreal3;

    public override Result<UnrealProfile> Map(ProfileReader reader)
    {
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

        var kindLayouts = ReadKindLayouts(reader, "U", "Index", bitMaskBools: true);
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
            reader.Engine,
            reader.Key,
            [.. reader.Versions.Select(static version => version.Text)],
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
            subsystemMapOffset: null);
    }
}

file sealed class Unreal4ProfileMapper : ProfileMapper
{
    private const string IndirectArrayType = "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>";
    private const string ObjectArrayGlobal = "GUObjectArray";

    public override string Engine => UnrealTarget.Unreal4;

    public override Result<UnrealProfile> Map(ProfileReader reader)
    {
        var shapes = reader.ReadObject("shapes");

        string? nameTable = ReadShape(reader, shapes, "FNameEntry");
        string? objectArray = ReadShape(reader, shapes, "FUObjectArray");
        string? property = ReadShape(reader, shapes, "FField");

        // Return before the offsets when a shape is missing, because the offsets a document needs depend on its shapes.
        if (nameTable is null || objectArray is null || property is null)
        {
            return reader.GetFailure()!;
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
        var kindLayouts = ReadKindLayouts(reader, property == "ffield" ? "F" : "U", "ComparisonIndex", bitMaskBools: false);
        var nameTableLocator = reader.ReadGlobal(nameTableShape.Global);
        var objectArrayLocator = reader.ReadGlobal(ObjectArrayGlobal);
        var worldLocator = reader.ReadGlobal(UnrealProfile.WorldGlobal);
        var engineLocator = reader.ReadGlobal(UnrealProfile.EngineGlobal);

        // Read the subsystem map as optional, because only 4.22 and later have subsystems.
        int? subsystemCollection = reader.ReadOffsetOrNull("UGameInstance", "SubsystemCollection");
        int? subsystemMap = reader.ReadOffsetOrNull("FSubsystemCollectionBase", "SubsystemMap");
        int? subsystemMapOffset = subsystemCollection + subsystemMap;

        // Read the serial number blocks as optional, because only 4.0 to 4.10 keep the serial numbers apart from the object array.
        var serialNumberBlocksLocator = reader.ReadGlobalOrNull(SerialNumberBlocks.Global);

        if (reader.GetFailure() is { } failure)
        {
            return failure;
        }

        return new UnrealProfile(
            reader.Engine,
            reader.Key,
            [.. reader.Versions.Select(static version => version.Text)],
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
            subsystemMapOffset,
            serialNumberBlocksLocator);
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
            _ => ProfileMapError.UnknownShape("FNameEntry", shape, "chunked, indirect"),
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
            _ => ProfileMapError.UnknownShape("FUObjectArray", shape, "chunked, flat, indirect, pointers"),
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
            _ => ProfileMapError.UnknownShape("FField", shape, "ffield, uproperty"),
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
