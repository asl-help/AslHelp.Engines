using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using static AslHelp.Engines.Unreal.Tests.FakeUdkGame;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealProfileUdkTests
{
    // The 2010-09 document reads its versions and layouts, its name table from GNames and its objects at offset 0 of GObjObjects.
    [Test]
    public void Parse_RealDocument_ReadsLayoutsAndReaders()
    {
        var profile = UnrealProfile.Parse(Document).Unwrap();

        string[] versions = ["2010-09"];
        Type[] readers = [typeof(FlatNameTable), typeof(PointerObjectArray), typeof(ObjectPropertyReader)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Versions, Is.EqualTo(versions));
            Assert.That(profile.Key, Is.Null);
            Assert.That(profile.ObjectLayout, Is.EqualTo(new ObjectLayout(0x28, 0x2C, 0x34)));
            Assert.That(profile.StructLayout, Is.EqualTo(new StructLayout(0x48, 0x4C)));
            Assert.That(profile.ChildLayout, Is.EqualTo(new ChildLayout(0x3C)));
            Assert.That(profile.ObjectArrayOffset, Is.Zero);
            Assert.That(profile.NameTableShape.Global, Is.EqualTo("GNames"));
            Assert.That(ReaderTypes(profile), Is.EqualTo(readers));
        }
    }

    // ArrayDim comes from the document at 0x3C, so the property reader doesn't read the 4 bytes in front of ElementSize.
    [Test]
    public void Parse_Unreal3_ReadsArrayDimFromDocument()
    {
        var profile = UnrealProfile.Parse(Set(Document, "\"0x3C\"", "offsets", "UProperty", "ArrayDim")).Unwrap();
        FakeMemory memory = new();
        Address property = new(0x8000_0000);
        memory.Add(property, new byte[0x100]);
        memory.WriteInt32(property + 0x3C, 3);
        memory.WriteInt32(property + 0x40, 99);
        memory.WriteInt32(property + 0x44, 4);
        memory.WriteInt32(property + 0x60, 0x88);

        var reader = PropertyReader(profile, memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.ArrayDimOf(property).Unwrap(), Is.EqualTo(3));
            Assert.That(reader.ElementSizeOf(property).Unwrap(), Is.EqualTo(4));
            Assert.That(reader.OffsetOf(property).Unwrap(), Is.EqualTo(0x88));
        }
    }

    // Parse reads only what attach needs, so a document without the unused offsets parses.
    [Test]
    public void Parse_WithoutUnusedOffsets_Parses()
    {
        string json = Remove(Remove(Document, "offsets", "UEnum"), "offsets", "UObjectProperty");
        json = Remove(Remove(json, "offsets", "UObject", "ObjectInternalInteger"), "offsets", "UProperty", "PropertyLinkNext");
        json = Remove(json, "offsets", "UStruct", "PropertySize");

        Assert.That(UnrealProfile.Parse(json).Unwrap().ObjectLayout, Is.EqualTo(new ObjectLayout(0x28, 0x2C, 0x34)));
    }

    // A document without engine lists engine with the other missing parts, because the parts it needs depend on the engine.
    [TestCase(false, "engine")]
    [TestCase(true, "engine, versions")]
    public void Parse_MissingEngine_ListsEngineWithTheOtherMissingParts(bool removeVersions, string missing)
    {
        string json = Remove(Document, "engine");

        if (removeVersions)
        {
            json = Remove(json, "versions");
        }

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo($"The profile doesn't have {missing}."));
    }

    // An engine that isn't a string counts as missing, like the other parts.
    [TestCase("3")]
    [TestCase("null")]
    [TestCase("[]")]
    public void Parse_EngineNotAString_FailsWithEngine(string value)
    {
        Assert.That(UnrealProfile.Parse(Set(Document, value, "engine")).UnwrapErr().Message, Is.EqualTo("The profile doesn't have engine."));
    }

    // The engine match is case-sensitive, so Unreal3 fails like unity.
    [TestCase("unity")]
    [TestCase("Unreal3")]
    [TestCase("")]
    public void Parse_UnknownEngine_FailsWithTheEngineAndTheKnownEngines(string engine)
    {
        Assert.That(
            UnrealProfile.Parse(Set(Document, $"\"{engine}\"", "engine")).UnwrapErr().Message,
            Is.EqualTo($"The profile's engine '{engine}' isn't 1 of unreal3, unreal4."));
    }

    // The parts a document needs depend on its engine, so an unknown engine fails before any part is checked.
    [Test]
    public void Parse_UnknownEngineAndMissingParts_FailsWithTheEngine()
    {
        string json = Remove(Remove(Set(Document, "\"unity\"", "engine"), "versions"), "offsets");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile's engine 'unity' isn't 1 of unreal3, unreal4."));
    }

    // Every missing offset comes back in 1 message, so a person fixes the document in 1 go.
    [Test]
    public void Parse_MissingOffsets_FailsWithEveryMissingOffset()
    {
        string json = Remove(Remove(Remove(Document, "offsets", "FNameEntry"), "offsets", "UField"), "offsets", "UObject");
        json = Remove(Remove(json, "offsets", "UProperty", "ElementSize"), "offsets", "UProperty", "Offset");
        json = Remove(Remove(json, "offsets", "UStruct", "SuperField"), "offsets", "UStruct", "Children");

        Assert.That(
            UnrealProfile.Parse(json).UnwrapErr().Message,
            Is.EqualTo(
                "The profile doesn't have offsets.UObject.Outer, offsets.UObject.Name, offsets.UObject.Class, offsets.UStruct.SuperField, "
                + "offsets.UStruct.Children, offsets.UField.Next, offsets.FNameEntry.Index, offsets.FNameEntry.Name, offsets.UProperty.ElementSize, "
                + "offsets.UProperty.Offset."));
    }

    // ArrayDim is a required offset like ElementSize, so a document without it fails with its path.
    [Test]
    public void Parse_Unreal3WithoutArrayDim_FailsWithMissingOffset()
    {
        Assert.That(
            UnrealProfile.Parse(Remove(Document, "offsets", "UProperty", "ArrayDim")).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have offsets.UProperty.ArrayDim."));
    }

    // A document without the offsets of the value kinds still parses.
    [Test]
    public void Parse_Unreal3WithoutKindOffsets_Parses()
    {
        string json = Remove(Remove(Document, "offsets", "UBoolProperty"), "offsets", "UStructProperty");
        json = Remove(Remove(json, "offsets", "FScriptArray"), "offsets", "FName");

        Assert.That(UnrealProfile.Parse(json).IsOk, Is.True);
    }

    // Each global that attach needs fails with its own key.
    [TestCase("GEngine")]
    [TestCase("GNames")]
    [TestCase("GObjObjects")]
    [TestCase("GWorld")]
    public void Parse_MissingGlobal_FailsWithTheGlobal(string global)
    {
        Assert.That(
            UnrealProfile.Parse(Remove(Document, "globals", global)).UnwrapErr().Message,
            Is.EqualTo($"The profile doesn't have globals.{global}."));
    }

    // A missing offset and a missing global come back together.
    [Test]
    public void Parse_MissingOffsetAndGlobal_ListsBoth()
    {
        string json = Remove(Remove(Document, "offsets", "UField"), "globals", "GWorld");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile doesn't have offsets.UField.Next, globals.GWorld."));
    }

    // The engine picks the parts a document needs, so an unreal4 document marked unreal3 fails with the unreal3 parts it doesn't have.
    [Test]
    public void Parse_Unreal4DocumentAsUnreal3_ListsTheUnreal3Parts()
    {
        string json = Set(FakeGame.Document("uproperty", "indirect", "flat"), "\"unreal3\"", "engine");

        Assert.That(
            UnrealProfile.Parse(json).UnwrapErr().Message,
            Is.EqualTo(
                "The profile doesn't have offsets.UObject.Outer, offsets.UObject.Name, offsets.UObject.Class, offsets.UStruct.SuperField, "
                + "offsets.FNameEntry.Name, offsets.UProperty.Offset, globals.GNames, globals.GObjObjects."));
    }

    // An unreal3 document marked unreal4 fails with shapes, because only unreal4 documents have them.
    [Test]
    public void Parse_Unreal3DocumentAsUnreal4_FailsWithShapes()
    {
        Assert.That(
            UnrealProfile.Parse(Set(Document, "\"unreal4\"", "engine")).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have shapes."));
    }

    // A part of the wrong JSON type counts as missing and doesn't throw from the JSON element.
    [TestCase("offsets", "[]")]
    [TestCase("globals", "\"GWorld\"")]
    public void Parse_PartNotAnObject_FailsWithThePart(string part, string value)
    {
        Assert.That(UnrealProfile.Parse(Set(Document, value, part)).UnwrapErr().Message, Is.EqualTo($"The profile doesn't have {part}."));
    }

    // A negative offset fails with its path and value, like in an unreal4 document.
    [Test]
    public void Parse_NegativeOffset_FailsWithTheOffsetPath()
    {
        Assert.That(
            UnrealProfile.Parse(Set(Document, "-1", "offsets", "UObject", "Class")).UnwrapErr().Message,
            Is.EqualTo("The profile's offsets.UObject.Class is -1, which is below 0."));
    }

    // A global keeps the route of every version in document order, so attach tries the route of the 1st version 1st.
    [Test]
    public void Parse_GlobalWithTwoVersions_KeepsBothRoutesInOrder()
    {
        string routes = """
            {
              "2010-09": [{ "forward": "89 07 A1 ????????", "offset": 3 }, { "read": "global" }],
              "2011-01": [{ "forward": "89 0D ????????", "offset": 2 }, { "read": "global" }]
            }
            """;

        string[] versions = ["2010-09", "2011-01"];

        Assert.That(UnrealProfile.Parse(Set(Document, routes, "globals", "GWorld")).Unwrap().WorldLocator.Routes.Keys, Is.EqualTo(versions));
    }

    // Creates the property reader of the profile over the memory.
    private static IPropertyReader PropertyReader(UnrealProfile profile, FakeMemory memory)
    {
        var names = profile.NameTableShape.Create(memory, Address.Zero);
        ObjectReader objectReader = new(memory, names, profile.ObjectLayout);
        StructReader structReader = new(memory, objectReader, profile.StructLayout, profile.ChildLayout);

        return profile.PropertyShape.Create(memory, names, objectReader, structReader);
    }

    // Creates the readers of the profile, so a test checks which reader the document maps to.
    private static Type[] ReaderTypes(UnrealProfile profile)
    {
        FakeMemory memory = new();
        var names = profile.NameTableShape.Create(memory, Address.Zero);

        return
        [
            names.GetType(),
            profile.ObjectArrayShape.Create(memory, Address.Zero).Unwrap().GetType(),
            PropertyReader(profile, memory).GetType(),
        ];
    }

    // Unreal Engine 3 doesn't have subsystems, so an unreal3 document never has them.
    [Test]
    public void Parse_Unreal3Document_DoesNotHaveSubsystems()
    {
        Assert.That(UnrealProfile.Parse(Document).Unwrap().HasSubsystems, Is.False);
    }
}
