using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.FakeUdkGame;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealProfileUdkEdgeTests
{
    // GEngine is a root like GWorld, so a GEngine that isn't a locator fails the document with its key.
    [TestCase("5")]
    [TestCase("{}")]
    public void Parse_GEngineNotALocator_FailsWithTheGlobal(string value)
    {
        Assert.That(
            UnrealProfile.Parse(Set(Document, value, "globals", "GEngine")).UnwrapErr().Message,
            Does.StartWith("The profile's globals.GEngine isn't a locator. "));
    }

    // The unreal3 mapper doesn't read shapes, so shapes in an unreal3 document fail as an unknown key instead of passing unread.
    [Test]
    public void Parse_ShapesInUnreal3Document_FailsAsAnUnknownKey()
    {
        string shapes = """{ "FField": "ffield", "FNameEntry": "chunked", "FUObjectArray": "chunked" }""";

        Assert.That(UnrealProfile.Parse(Set(Document, shapes, "shapes")).UnwrapErr().Message, Is.EqualTo("The profile has the unknown keys shapes."));
    }

    // GNames or GObjObjects that isn't a locator fails with its key and doesn't throw.
    [TestCase("GNames")]
    [TestCase("GObjObjects")]
    public void Parse_GlobalNotALocator_FailsWithTheGlobal(string global)
    {
        Assert.That(
            UnrealProfile.Parse(Set(Document, "5", "globals", global)).UnwrapErr().Message,
            Does.StartWith($"The profile's globals.{global} isn't a locator. "));
    }

    // An ElementSize that doesn't read fails with the ElementSize path.
    [Test]
    public void Parse_ElementSizeNotHex_FailsWithTheElementSizePath()
    {
        string message = UnrealProfile.Parse(Set(Document, "\"0xZZ\"", "offsets", "UProperty", "ElementSize")).UnwrapErr().Message;

        Assert.That(message, Does.StartWith("The profile isn't valid. offsets.UProperty.ElementSize").And.Contain("'0xZZ'"));
    }

    // The mapper returns before it reads any offset when offsets and globals are missing and the message still lists versions.
    [Test]
    public void Parse_MissingVersionsOffsetsAndGlobals_ListsAll()
    {
        string json = Remove(Remove(Remove(Document, "versions"), "offsets"), "globals");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile doesn't have versions, offsets, globals."));
    }

    // A bad value fails before missing parts and the message only has the value.
    [Test]
    public void Parse_NegativeOffsetAndMissingGlobal_FailsWithTheOffset()
    {
        string json = Remove(Set(Document, "-4", "offsets", "UField", "Next"), "globals", "GNames");

        Assert.That(UnrealProfile.Parse(json).UnwrapErr().Message, Is.EqualTo("The profile's offsets.UField.Next is -4, which is below 0."));
    }
}
