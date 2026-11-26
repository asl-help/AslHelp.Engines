using AslHelp.Memory;
using NUnit.Framework;
using System.Linq;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealProfileEdgeTests
{
    // Trimmed from the 4.18.3 win-x64-shipping profile.
    private const string Item = """
        {
          "key": "win-x64-shipping",
          "versions": ["4.18.3", "4.19.2"],
          "shapes": { "FField": "uproperty", "FNameEntry": "indirect", "FUObjectArray": "flat" },
          "offsets": {
            "FFixedUObjectArray": { "Objects": "0x0", "NumElements": "0xC" },
            "FNameEntry": { "Index": "0x0", "AnsiName": "0x10" },
            "FUObjectArray": { "ObjObjects": "0x10" },
            "FUObjectItem": { "Object": "0x0", "sizeof": "0x18" },
            "UField": { "Next": "0x28" },
            "UObjectBase": { "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
            "UProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44" },
            "UStruct": { "SuperStruct": "0x30", "Children": "0x38" }
          },
          "globals": {
            "GUObjectArray": { "4.18.3": [{ "forward": "89 1D ???????? 85 FF", "offset": 2 }, { "read": "global" }] },
            "GWorld": { "4.18.3": [{ "forward": "48 89 05 ???????? 0F 28 D6", "offset": 3 }, { "read": "global" }] },
            "NameTable": { "4.18.3": [{ "forward": "E8 ???????? 48 8B C3 48 89 1D ????????", "offset": 11 }, { "read": "global" }] }
          }
        }
        """;

    // A negative JSON number would read memory in front of the object, so it fails with its path and value.
    [TestCase("-1")]
    [TestCase("-2147483648")]
    public void Parse_NegativeOffset_FailsWithTheOffsetPath(string value)
    {
        string message = UnrealProfile.Parse(Set(Item, value, "offsets", "UField", "Next")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo($"The profile's offsets.UField.Next is {value}, which is below 0."));
    }

    // A bad value fails before missing parts and the message only has the value.
    [Test]
    public void Parse_NegativeOffsetAndMissingGlobal_FailsWithTheOffset()
    {
        string json = Remove(Set(Item, "-1", "offsets", "UField", "Next"), "globals", "GWorld");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile's offsets.UField.Next is -1, which is below 0."));
    }

    // The largest hex string HexNumber takes reads as int.MaxValue and not as a failure.
    [Test]
    public void Parse_OffsetAtIntMax_Reads()
    {
        var profile = UnrealProfile.Parse(Set(Item, "\"0x7FFFFFFF\"", "offsets", "UField", "Next")).Unwrap();

        Assert.That(profile.ChildLayout.Next, Is.EqualTo(int.MaxValue));
    }

    // Missing versions don't hide missing offsets, because the offsets a document needs only depend on its shapes.
    [Test]
    public void Parse_MissingVersionsAndOffset_ListsBoth()
    {
        string json = Remove(Remove(Item, "versions"), "offsets", "UField");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile doesn't have versions, offsets.UField.Next."));
    }

    // A part of the wrong JSON type counts as missing and doesn't throw from the JSON element.
    [TestCase("offsets", "[]")]
    [TestCase("globals", "\"GWorld\"")]
    [TestCase("shapes", "null")]
    public void Parse_PartNotAnObject_FailsWithThePart(string part, string value)
    {
        Assert.That(UnrealProfile.Parse(Set(Item, value, part)).UnwrapErr().Message, Is.EqualTo($"The profile doesn't have {part}."));
    }

    // An offset type that isn't an object lists each member the shape needs from it.
    [Test]
    public void Parse_OffsetTypeNotAnObject_ListsEachMember()
    {
        Assert.That(
            UnrealProfile.Parse(Set(Item, "\"0x30\"", "offsets", "UStruct")).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have offsets.UStruct.SuperStruct, offsets.UStruct.Children."));
    }

    // A locator that the converter rejects fails with the global in the message and doesn't throw.
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("5")]
    [TestCase("\"48 89 05\"")]
    [TestCase("""{ "4.18.3": [] }""")]
    [TestCase("""{ "4.18.3": [null] }""")]
    [TestCase("""{ "4.18.3": { "read": "global" } }""")]
    [TestCase("""{ "4.18.3": [{ "read": "global" }, { "forward": "48 89 05", "offset": 3 }] }""")]
    public void Parse_GlobalNotALocator_FailsWithTheGlobal(string value)
    {
        Assert.That(
            UnrealProfile.Parse(Set(Item, value, "globals", "GWorld")).UnwrapErr().Message,
            Does.StartWith("The profile's globals.GWorld isn't a locator. "));
    }

    // 2 routes of 1 global with the same name fail with that name, because a locator needs unique route names.
    [Test]
    public void Parse_GlobalWithDuplicateRouteNames_FailsWithTheRouteName()
    {
        string route = """[{ "forward": "48 89 05 ????????", "offset": 3 }, { "read": "global" }]""";
        string globals = $$"""
            "globals": {
              "GUObjectArray": { "4.18.3": {{route}} },
              "GWorld": { "4.18.3": {{route}}, "4.18.3": {{route}} },
              "NameTable": { "4.18.3": {{route}} }
            }
            """;

        Assert.That(
            UnrealProfile.Parse(Prepend(Remove(Item, "globals"), globals)).UnwrapErr().Message,
            Does.StartWith("The profile's globals.GWorld isn't a locator. ").And.Contain("'4.18.3'"));
    }

    // Nesting past the reader's depth limit in a member the parser skips still fails with a result.
    [Test]
    public void Parse_NestingPastTheDepthLimit_FailsWithoutThrowing()
    {
        string extra = $"\"extra\": {new string('[', 100)}{new string(']', 100)}";

        Assert.That(UnrealProfile.Parse(Prepend(Item, extra)).UnwrapErr().Message, Does.StartWith("The profile isn't valid. "));
    }

    // A group with many versions keeps every one in document order.
    [Test]
    public void Parse_TenThousandVersions_KeepsThemInOrder()
    {
        string[] versions = [.. Enumerable.Range(0, 10_000).Select(static i => $"4.{i}.0")];
        string json = Set(Item, $"[{string.Join(",", versions.Select(static v => $"\"{v}\""))}]", "versions");

        Assert.That(UnrealProfile.Parse(json).Unwrap().Versions, Is.EqualTo(versions));
    }

    // Escaped and non-ASCII text reads as the decoded string, so a shape written as \u0066lat is flat.
    [Test]
    public void Parse_EscapedAndNonAsciiText_ReadsTheDecodedText()
    {
        // Prepend the text, because JsonNode would write flat back as flat and ü as ü.
        string parts = """
            "key": "win-x64-ü日本",
            "versions": ["4.18.3-ü"],
            "shapes": { "FField": "uproperty", "FNameEntry": "indirect", "FUObjectArray": "flat" }
            """;

        var profile = UnrealProfile.Parse(Prepend(Remove(Remove(Remove(Item, "key"), "versions"), "shapes"), parts)).Unwrap();

        string[] versions = ["4.18.3-ü"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Key, Is.EqualTo("win-x64-ü日本"));
            Assert.That(profile.Versions, Is.EqualTo(versions));
            Assert.That(profile.ObjectArrayShape.Create(new FakeMemory(), Address.Zero).Unwrap(), Is.TypeOf<ItemObjectArray>());
        }
    }

    // A key that isn't a string reads as no key, the same as a missing key.
    [Test]
    public void Parse_KeyNotAString_ParsesWithKeyNull()
    {
        Assert.That(UnrealProfile.Parse(Set(Item, "5", "key")).Unwrap().Key, Is.Null);
    }

    // An unknown shape fails before its offsets are checked, so the message doesn't list offsets of a shape the library doesn't read.
    [Test]
    public void Parse_UnknownShapeAndMissingOffset_FailsWithTheShape()
    {
        string json = Remove(Set(Item, "\"sparse\"", "shapes", "FUObjectArray"), "offsets", "UField");

        Assert.That(
            UnrealProfile.Parse(json).UnwrapErr().Message,
            Is.EqualTo("The profile's FUObjectArray shape 'sparse' isn't 1 of chunked, flat, indirect, pointers."));
    }

    // Puts raw members in front of the others, for text that JsonEdits would write back in another form.
    private static string Prepend(string json, string members)
    {
        return $"{{{members},{json.TrimStart('{')}";
    }
}
