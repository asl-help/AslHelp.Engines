using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealProfileTests
{
    // Trimmed from the 4.0.2 win-x86-shipping profile, which doesn't have FUObjectItem or FFixedUObjectArray.
    private const string Pointers = """
        {
          "engine": "unreal4",
          "key": "win-x86-shipping",
          "versions": ["4.0.2"],
          "shapes": { "FField": "uproperty", "FNameEntry": "indirect", "FUObjectArray": "pointers" },
          "offsets": {
            "FNameEntry": { "Index": "0x0", "AnsiName": "0x8", "WideName": "0x8" },
            "FUObjectArray": { "ObjObjects": "0xC" },
            "UField": { "Next": "0x1C" },
            "UObjectBase": { "InternalIndex": "0x8", "ClassPrivate": "0xC", "NamePrivate": "0x10", "OuterPrivate": "0x18" },
            "UProperty": { "ArrayDim": "0x20", "ElementSize": "0x24", "Offset_Internal": "0x3C", "PropertyLinkNext": "0x40" },
            "UStruct": { "SuperStruct": "0x20", "Children": "0x24", "PropertiesSize": "0x28" }
          },
          "globals": {
            "GEngine": { "4.0.2": [{ "forward": "A1 ???????? 1B C9", "offset": 1 }, { "read": "global" }] },
            "GUObjectArray": { "4.0.2": [{ "forward": "80 7C 24 04 00 C7 02 ????????", "offset": 7 }, { "read": "global" }] },
            "GWorld": { "4.0.2": [{ "forward": "A3 ???????? 83 CE FF", "offset": 1 }, { "read": "global" }] },
            "NameTable": { "4.0.2": [{ "forward": "83 C4 0C 89 35 ???????? 8B C6", "offset": 5 }, { "read": "global" }] }
          }
        }
        """;

    // Trimmed from the 4.8.3 win-x64-shipping profile, whose GUObjectArray has a 2nd route for 4.10.4.
    private const string Indirect = """
        {
          "engine": "unreal4",
          "key": "win-x64-shipping",
          "versions": ["4.8.3", "4.9.2", "4.10.4"],
          "shapes": { "FField": "uproperty", "FNameEntry": "indirect", "FUObjectArray": "indirect" },
          "offsets": {
            "FNameEntry": { "Index": "0x0", "AnsiName": "0x10", "WideName": "0x10" },
            "FUObjectArray": { "ObjObjects": "0x10" },
            "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>": { "Chunks": "0x0", "NumElements": "0x1000", "NumChunks": "0x1004" },
            "UField": { "Next": "0x28" },
            "UObjectBase": { "InternalIndex": "0xC", "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
            "UProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x4C", "PropertyLinkNext": "0x50" },
            "UStruct": { "SuperStruct": "0x30", "Children": "0x38", "PropertiesSize": "0x40" }
          },
          "globals": {
            "GEngine": { "4.8.3": [{ "forward": "32 DB 48 8B 0D ????????", "offset": 5 }, { "read": "global" }] },
            "GUObjectArray": {
              "4.8.3": [{ "forward": "89 15 ???????? 85 D2", "offset": 2 }, { "read": "global" }],
              "4.10.4": [{ "forward": "33 D2 89 1D ????????", "offset": 4 }, { "read": "global" }]
            },
            "GWorld": { "4.8.3": [{ "forward": "48 8B 01 48 8B 2D ????????", "offset": 6 }, { "read": "global" }] },
            "NameTable": { "4.8.3": [{ "forward": "48 89 35 ???????? 83 3B 00", "offset": 3 }, { "read": "global" }] }
          }
        }
        """;

    // Trimmed from the 4.18.3 win-x64-shipping profile, which also covers 4.19.2.
    private const string Item = """
        {
          "engine": "unreal4",
          "key": "win-x64-shipping",
          "versions": ["4.18.3", "4.19.2"],
          "shapes": { "FField": "uproperty", "FNameEntry": "indirect", "FUObjectArray": "flat" },
          "offsets": {
            "FFixedUObjectArray": { "Objects": "0x0", "NumElements": "0xC" },
            "FNameEntry": { "Index": "0x0", "AnsiName": "0x10", "WideName": "0x10" },
            "FUObjectArray": { "ObjObjects": "0x10" },
            "FUObjectItem": { "Object": "0x0", "SerialNumber": "0x10", "sizeof": "0x18" },
            "UField": { "Next": "0x28" },
            "UObjectBase": { "InternalIndex": "0xC", "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
            "UProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44", "PropertyLinkNext": "0x50" },
            "UStruct": { "SuperStruct": "0x30", "Children": "0x38", "PropertiesSize": "0x40" }
          },
          "globals": {
            "GUObjectArray": { "4.18.3": [{ "forward": "89 1D ???????? 85 FF", "offset": 2 }, { "read": "global" }] },
            "GWorld": { "4.18.3": [{ "forward": "48 89 05 ???????? 0F 28 D6", "offset": 3 }, { "read": "global" }] },
            "GEngine": { "4.18.3": [{ "forward": "32 DB 48 8B 0D ????????", "offset": 5 }, { "read": "global" }] },
            "NameTable": { "4.18.3": [{ "forward": "E8 ???????? 48 8B C3 48 89 1D ????????", "offset": 11 }, { "read": "global" }] }
          }
        }
        """;

    // Trimmed from the 5.8.4 win-x64-shipping profile.
    private const string Chunked = """
        {
          "engine": "unreal4",
          "key": "win-x64-shipping",
          "versions": ["5.8.4"],
          "shapes": { "FField": "ffield", "FNameEntry": "chunked", "FUObjectArray": "chunked" },
          "offsets": {
            "FChunkedFixedUObjectArray": { "Objects": "0x0", "NumElements": "0x8", "NumChunks": "0x10", "NumElementsPerChunk": "0x10000" },
            "FField": { "ClassPrivate": "0x8", "Next": "0x18", "NamePrivate": "0x20" },
            "FFieldClass": { "Name": "0x8" },
            "FNameEntry": { "Header": "0x0", "AnsiName": "0x2", "WideName": "0x2" },
            "FProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44", "PropertyLinkNext": "0x48" },
            "FUObjectArray": { "ObjObjects": "0x0" },
            "FUObjectItem": { "Object": "0x8", "SerialNumber": "0x10", "sizeof": "0x18" },
            "UField": { "Next": "0x28" },
            "UObjectBase": { "InternalIndex": "0xC", "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
            "UStruct": { "SuperStruct": "0x40", "Children": "0x48", "ChildProperties": "0x50", "PropertiesSize": "0x58" }
          },
          "globals": {
            "GUObjectArray": { "5.8.4": [{ "forward": "4C 8B 0D ???????? 8B C1", "offset": 3 }, { "read": "global" }] },
            "GEngine": { "5.8.4": [{ "forward": "90 48 8B 35 ????????", "offset": 4 }, { "read": "global" }] },
            "GWorld": { "5.8.4": [{ "forward": "4C 0F 44 25 ????????", "offset": 4 }, { "read": "global" }] },
            "NamePoolData": { "5.8.4": [{ "forward": "48 8D 0D ???????? 8B FA", "offset": 3 }, { "read": "global" }] }
          }
        }
        """;

    // The 4.18.3 document reads its versions, key and layouts and each shape creates its own reader.
    [Test]
    public void Parse_ItemArrayAndIndirectNames_ReadsLayoutsAndShapes()
    {
        var profile = UnrealProfile.Parse(Item).Unwrap();

        string[] versions = ["4.18.3", "4.19.2"];
        Type[] readers = [typeof(IndirectNameTable), typeof(ItemObjectArray), typeof(ObjectPropertyReader)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Versions, Is.EqualTo(versions));
            Assert.That(profile.Key, Is.EqualTo("win-x64-shipping"));
            Assert.That(profile.ObjectLayout, Is.EqualTo(new ObjectLayout(0x20, 0x18, 0x10)));
            Assert.That(profile.StructLayout, Is.EqualTo(new StructLayout(0x30, 0x38)));
            Assert.That(profile.ChildLayout, Is.EqualTo(new ChildLayout(0x28)));
            Assert.That(profile.ObjectArrayOffset, Is.EqualTo(0x10));
            Assert.That(profile.NameTableShape.Global, Is.EqualTo("NameTable"));
            Assert.That(ReaderTypes(profile), Is.EqualTo(readers));
        }
    }

    // The 5.8.4 document reads its names from NamePoolData, its objects at offset 0 of GUObjectArray and its properties as fields.
    [Test]
    public void Parse_ChunkedArrayChunkedNamesAndFields_ReadsLayoutsAndShapes()
    {
        var profile = UnrealProfile.Parse(Chunked).Unwrap();

        string[] routes = ["5.8.4"];
        Type[] readers = [typeof(ChunkedNameTable), typeof(ChunkedObjectArray), typeof(FieldPropertyReader)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.StructLayout, Is.EqualTo(new StructLayout(0x40, 0x48)));
            Assert.That(profile.ObjectArrayOffset, Is.Zero);
            Assert.That(profile.NameTableShape.Global, Is.EqualTo("NamePoolData"));
            Assert.That(profile.NameTableLocator.Routes.Keys, Is.EqualTo(routes));
            Assert.That(ReaderTypes(profile), Is.EqualTo(readers));
        }
    }

    // The kind offsets map with the prefix of the property shape. The string layout adds FString.Data to the array members.
    [TestCase("uproperty")]
    [TestCase("ffield")]
    public void Parse_KindOffsets_ReadsKindLayouts(string field)
    {
        string json = Set(FakeGame.Document(field, "indirect", "flat"), "\"0x10\"", "offsets", "FString", "Data");

        var layouts = UnrealProfile.Parse(json).Unwrap().KindLayouts;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layouts.Find(Kind<BoolShape>("BoolProperty")).IsOk, Is.True);
            Assert.That(layouts.Find(Kind<int>("EnumProperty")).Unwrap(), Is.EqualTo(0x70));
            Assert.That(layouts.Find(Kind<NameValueLayout>("NameProperty")).Unwrap(), Is.EqualTo(new NameValueLayout(0x0, 0x4)));
            Assert.That(layouts.Find(Kind<StringLayout>("StrProperty")).Unwrap(), Is.EqualTo(new StringLayout(0x10, 0x18)));
            Assert.That(layouts.Find(Kind<int>("StructProperty")).Unwrap(), Is.EqualTo(0x70));
        }
    }

    // A document without the kind offsets still parses and each kind part fails with its class and the paths it needs.
    [Test]
    public void Parse_DocumentWithoutKindOffsets_FailsOnlyTheKindParts()
    {
        var layouts = UnrealProfile.Parse(Item).Unwrap().KindLayouts;

        Assert.That(
            layouts.Find(Kind<int>("StructProperty")).UnwrapErr().Message,
            Is.EqualTo("Can't read StructProperty: the profile doesn't have offsets.UStructProperty.Struct."));
    }

    // A document without the array offsets still parses and only the array kind fails with every path it needs.
    [Test]
    public void Parse_DocumentWithoutCollectionOffsets_FailsOnlyTheCollectionKinds()
    {
        var layouts = UnrealProfile.Parse(Item).Unwrap().KindLayouts;

        Assert.That(
            layouts.Find(Kind<ArrayPropertyLayout>("ArrayProperty")).UnwrapErr().Message,
            Is.EqualTo(
                "Can't read ArrayProperty: the profile doesn't have offsets.UArrayProperty.Inner, offsets.FScriptArray.Data, "
                + "offsets.FScriptArray.ArrayNum."));
    }

    // A field document reads the collection members with the F prefix, so FArrayProperty.Inner and not UArrayProperty.Inner.
    [Test]
    public void Parse_FieldShape_ReadsCollectionOffsetsWithTheFPrefix()
    {
        string json = Set(FakeGame.Document("ffield", "indirect", "flat"), """{ "Inner": "0x78" }""", "offsets", "FArrayProperty");

        Assert.That(UnrealProfile.Parse(json).Unwrap().KindLayouts.Find(Kind<ArrayPropertyLayout>("ArrayProperty")).Unwrap().Inner, Is.EqualTo(0x78));
    }

    // A 5.7 document has the set offsets under FScriptSparseSetLayout and TScriptSparseSet, which read like FScriptSetLayout and FScriptSet.
    [Test]
    public void Parse_SparseSetNames_ReadsTheSetOffsets()
    {
        string json = FakeGame.Document("ffield", "indirect", "flat");
        json = Remove(Remove(json, "offsets", "FScriptSetLayout"), "offsets", "FScriptSet");
        json = Set(json, """{ "Size": "0x1C" }""", "offsets", "FScriptSparseSetLayout");
        json = Set(json, """{ "Elements": "0x8" }""", "offsets", "TScriptSparseSet<FDefaultSetAllocator>");

        var set = UnrealProfile.Parse(json).Unwrap().KindLayouts.Find(Kind<SetPropertyLayout>("SetProperty")).Unwrap().Set;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.Size, Is.EqualTo(0x1C));
            Assert.That(set.Elements, Is.EqualTo(0x8));
        }
    }
    // Each shape requires only its own offsets, so the pointers shape parses without FUObjectItem and FFixedUObjectArray.
    [Test]
    public void Parse_PointerArray_ParsesWithoutItemOffsets()
    {
        var profile = UnrealProfile.Parse(Pointers).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.ObjectLayout, Is.EqualTo(new ObjectLayout(0x18, 0x10, 0xC)));
            Assert.That(ReaderTypes(profile)[1], Is.EqualTo(typeof(PointerObjectArray)));
        }
    }

    // The indirect shape reads its offsets from the template type of the array, as the 4.8 to 4.10 PDBs write it.
    [Test]
    public void Parse_IndirectArray_ReadsTheTemplateTypeOffsets()
    {
        var profile = UnrealProfile.Parse(Indirect).Unwrap();

        Assert.That(ReaderTypes(profile)[1], Is.EqualTo(typeof(IndirectObjectArray)));
    }

    // A global keeps every route in document order, so the route of the 1st version is tried 1st.
    [Test]
    public void Parse_GlobalWithTwoRoutes_KeepsBothRoutesInOrder()
    {
        var profile = UnrealProfile.Parse(Indirect).Unwrap();

        string[] routes = ["4.8.3", "4.10.4"];

        Assert.That(profile.ObjectArrayLocator.Routes.Keys, Is.EqualTo(routes));
    }

    // A document without a key parses, because a group of an engine without keys doesn't have one.
    [Test]
    public void Parse_MissingKey_ParsesWithKeyNull()
    {
        Assert.That(UnrealProfile.Parse(Remove(Item, "key")).Unwrap().Key, Is.Null);
    }

    // Text that isn't JSON fails with the reason of the JSON reader and doesn't throw.
    [TestCase("")]
    [TestCase("versions")]
    [TestCase("""{ "versions": ["4.18.3"] """)]
    public void Parse_NotJson_FailsWithTheJsonReason(string json)
    {
        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Does.StartWith("The profile isn't valid. ").And.Length.GreaterThan(30));
    }

    // A root that isn't an object lists every part and doesn't throw from the JSON element.
    [TestCase("[]")]
    [TestCase("1")]
    [TestCase("null")]
    public void Parse_RootNotAnObject_FailsWithEveryPart(string json)
    {
        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile doesn't have engine, versions, offsets, globals."));
    }

    // Every missing offset comes back in 1 message, so a person fixes the document in 1 go.
    [Test]
    public void Parse_MissingOffsets_FailsWithEveryMissingOffset()
    {
        string json = Remove(Remove(Item, "offsets", "UObjectBase", "OuterPrivate"), "offsets", "UStruct");

        Assert.That(
            UnrealProfile.Parse(json).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have offsets.UObjectBase.OuterPrivate, offsets.UStruct.SuperStruct, offsets.UStruct.Children."));
    }

    // A shape requires its own offsets, so a flat array without the item size and fields without ChildProperties fail with that path.
    [TestCase(Item, "FUObjectItem", "sizeof")]
    [TestCase(Chunked, "UStruct", "ChildProperties")]
    [TestCase(Chunked, "FChunkedFixedUObjectArray", "NumElementsPerChunk")]
    [TestCase(Chunked, "FFieldClass", "Name")]
    [TestCase(Indirect, "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>", "NumChunks")]
    [TestCase(Pointers, "FNameEntry", "AnsiName")]
    public void Parse_MissingShapeOffset_FailsWithThatOffset(string json, string type, string member)
    {
        Assert.That(
            UnrealProfile.Parse(Remove(json, "offsets", type, member)).UnwrapErr().Message,
            Is.EqualTo($"The profile doesn't have offsets.{type}.{member}."));
    }

    // The global of the name table follows the name shape, so a chunked document with NameTable doesn't have NamePoolData.
    [TestCase(Item, "GEngine")]
    [TestCase(Item, "GWorld")]
    [TestCase(Item, "GUObjectArray")]
    [TestCase(Item, "NameTable")]
    [TestCase(Chunked, "NamePoolData")]
    public void Parse_MissingGlobal_FailsWithTheGlobal(string json, string global)
    {
        Assert.That(
            UnrealProfile.Parse(Remove(json, "globals", global)).UnwrapErr().Message,
            Is.EqualTo($"The profile doesn't have globals.{global}."));
    }

    // Missing offsets and a missing global come back together.
    [Test]
    public void Parse_MissingOffsetsAndGlobal_ListsBoth()
    {
        string json = Remove(Remove(Item, "offsets", "UField"), "globals", "GWorld");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile doesn't have offsets.UField.Next, globals.GWorld."));
    }

    // A global of JSON null counts as missing.
    [Test]
    public void Parse_GlobalNull_FailsWithTheGlobal()
    {
        Assert.That(
            UnrealProfile.Parse(Set(Item, "null", "globals", "GWorld")).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have globals.GWorld."));
    }

    // The offsets a document needs depend on its shapes, so a missing shape fails before any offset is checked.
    [TestCase("FField")]
    [TestCase("FNameEntry")]
    [TestCase("FUObjectArray")]
    public void Parse_MissingShape_FailsWithOnlyTheShape(string key)
    {
        string json = Remove(Remove(Item, "shapes", key), "offsets", "UField");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo($"The profile doesn't have shapes.{key}."));
    }

    // A document without the shapes object lists only shapes and not each key.
    [Test]
    public void Parse_NoShapes_FailsWithShapes()
    {
        Assert.That(UnrealProfile.Parse(Remove(Item, "shapes")).UnwrapErr().Message, Is.EqualTo("The profile doesn't have shapes."));
    }

    // A shape value matches case-sensitive, so Flat fails like sparse.
    [TestCase("FField", "Uproperty", "ffield, uproperty")]
    [TestCase("FNameEntry", "flat", "chunked, indirect")]
    [TestCase("FUObjectArray", "sparse", "chunked, flat, indirect, pointers")]
    [TestCase("FUObjectArray", "Flat", "chunked, flat, indirect, pointers")]
    public void Parse_UnknownShape_FailsWithTheValueAndTheKnownValues(string key, string value, string known)
    {
        Assert.That(
            UnrealProfile.Parse(Set(Item, $"\"{value}\"", "shapes", key)).UnwrapErr().Message,
            Is.EqualTo($"The profile's {key} shape '{value}' isn't 1 of {known}."));
    }

    // A shape that isn't a string counts as missing.
    [Test]
    public void Parse_ShapeNotAString_FailsWithTheShape()
    {
        Assert.That(
            UnrealProfile.Parse(Set(Item, "1", "shapes", "FField")).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have shapes.FField."));
    }

    // Versions that are missing, empty or not all strings fail, because a profile without them doesn't show which builds it's for.
    [TestCase(null)]
    [TestCase("[]")]
    [TestCase("""["4.18.3", 4]""")]
    [TestCase("\"4.18.3\"")]
    public void Parse_BadVersions_FailsWithVersions(string? versions)
    {
        string json = versions is null ? Remove(Item, "versions") : Set(Item, versions, "versions");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile doesn't have versions."));
    }

    // An offset that isn't a number or a 0x hex string up to 0x7FFFFFFF fails with its path and its value.
    [TestCase("\"10\"", "'10'")]
    [TestCase("\"0xZZ\"", "'0xZZ'")]
    [TestCase("\"0x80000000\"", "'0x80000000'")]
    [TestCase("true", "must be a number or a hex string")]
    [TestCase("4294967296", "4294967296")]
    public void Parse_OffsetNotHex_FailsWithTheOffsetPath(string value, string shown)
    {
        string message = UnrealProfile.Parse(Set(Item, value, "offsets", "UField", "Next")).UnwrapErr().Message;

        Assert.That(message, Does.StartWith("The profile isn't valid. offsets.UField.Next").And.Contain(shown));
    }

    // An offset written as a plain JSON number reads the same as its hex string.
    [Test]
    public void Parse_OffsetAsJsonNumber_Reads()
    {
        Assert.That(UnrealProfile.Parse(Set(Item, "40", "offsets", "UField", "Next")).Unwrap().ChildLayout, Is.EqualTo(new ChildLayout(0x28)));
    }

    // A route with an unknown step or a bad signature fails with the global and the reason of the locator.
    [TestCase("""{ "jump": "48 89 05" }""", "'jump' isn't a step")]
    [TestCase("""{ "forward": "48 8B 0", "offset": 3 }""", "'48 8B 0' of the 'forward' step isn't a signature")]
    public void Parse_BadLocatorStep_FailsWithTheGlobalAndTheReason(string step, string reason)
    {
        string json = Set(Item, $$"""{ "4.18.3": [{{step}}, { "read": "global" }] }""", "globals", "GWorld");

        Assert.That(
            UnrealProfile.Parse(json).UnwrapErr().Message,
            Does.StartWith("The profile's globals.GWorld isn't a locator. The route '4.18.3' is invalid. ").And.Contain(reason));
    }

    // Gets a kind of the library as the kind of its layout type.
    private static LayoutKind<TLayout> Kind<TLayout>(string className)
        where TLayout : notnull
    {
        return (LayoutKind<TLayout>)PropertyKinds.Find(className);
    }

    // Creates the readers of each shape, so a test checks which reader a shape value maps to.
    private static Type[] ReaderTypes(UnrealProfile profile)
    {
        FakeMemory memory = new() { Layout = MemoryLayout.X64 };
        var names = profile.NameTableShape.Create(memory, Address.Zero);
        ObjectReader objectReader = new(memory, names, profile.ObjectLayout);
        StructReader structReader = new(memory, objectReader, profile.StructLayout, profile.ChildLayout);

        return
        [
            names.GetType(),
            profile.ObjectArrayShape.Create(memory, Address.Zero).Unwrap().GetType(),
            profile.PropertyShape.Create(memory, names, objectReader, structReader).GetType(),
        ];
    }

    // Only documents of 4.22 and later have the subsystem collection of the game instance, so it decides whether the game has subsystems.
    [TestCase(false)]
    [TestCase(true)]
    public void Parse_SubsystemCollection_DecidesHasSubsystems(bool present)
    {
        string json = present ? Set(Item, """{ "SubsystemCollection": "0x1A0" }""", "offsets", "UGameInstance") : Item;

        Assert.That(UnrealProfile.Parse(json).Unwrap().HasSubsystems, Is.EqualTo(present));
    }
}
