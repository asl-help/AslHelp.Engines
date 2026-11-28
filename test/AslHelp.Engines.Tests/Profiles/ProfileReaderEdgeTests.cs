using AslHelp.Engines.Profiles;
using NUnit.Framework;
using System.Text.Json;
using static AslHelp.Engines.Tests.Profiles.FakeProfile;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileReaderEdgeTests
{
    // A document without offsets and globals lists each of them once and not every path under them.
    [Test]
    public void Read_DocumentWithoutOffsetsAndGlobals_ListsEachSectionOnce()
    {
        Assert.That(
            ProfileReader.Read("""{ "engine": "fake", "versions": ["1.0"] }""", Map, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have offsets, globals."));
    }

    // A mapper can read a global before any offset and still gets both.
    [Test]
    public void Read_MapperReadsTheGlobalFirst_MapsBoth()
    {
        var profile = ProfileReader.Read(
            Document("x64", "1.0"),
            Result<FakeProfile> (reader) =>
            {
                var thingsLocator = reader.ReadGlobal("Things");
                int speedOffset = reader.ReadOffset("Thing", "Speed");

                return new FakeProfile(speedOffset, thingsLocator!);
            },
            FakeVersion.Parse).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.SpeedOffset, Is.EqualTo(0x10));
            Assert.That(profile.ThingsLocator.Routes.Keys, Is.EqualTo(["1.0"]));
        }
    }

    // An object that a mapper keeps still reads after the document is gone.
    [Test]
    public void Read_MapperKeepsAnObject_ObjectStillReads()
    {
        JsonElement? offsets = null;

        _ = ProfileReader.Read(
            Document("x64", "1.0"),
            reader =>
            {
                offsets = reader.ReadObject("offsets");
                return Map(reader);
            },
            FakeVersion.Parse).Unwrap();

        Assert.That(offsets!.Value.GetProperty("Thing").GetProperty("Speed").GetString(), Is.EqualTo("0x10"));
    }

    // A root that isn't an object lists the parts that every document needs instead of throwing.
    [TestCase("[]")]
    [TestCase("42")]
    [TestCase("\"fake\"")]
    [TestCase("null")]
    public void Read_RootThatIsNotAnObject_ListsEngineAndVersions(string json)
    {
        Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message, Is.EqualTo("The profile doesn't have engine, versions."));
    }

    // versions counts as missing when 1 of its versions isn't a string.
    [Test]
    public void Read_VersionThatIsNotAString_ListsVersionsAsMissing()
    {
        string json = """{ "engine": "fake", "versions": ["1.0", 2], "offsets": { "Thing": { "Speed": 4 } }, "globals": { "Things": null } }""";

        Assert.That(
            ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have versions, globals.Things."));
    }

    // A version the parser rejects fails before the mapper, with the text of that version and the engine.
    [Test]
    public void Read_VersionTheParserRejects_FailsWithTheVersionAndTheEngine()
    {
        bool mapped = false;

        string message = ProfileReader.Read(
            Document("x64", "1.0", "v1.2"),
            reader =>
            {
                mapped = true;
                return Map(reader);
            },
            FakeVersion.Parse).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Is.EqualTo("The profile's version 'v1.2' isn't a version of fake."));
            Assert.That(mapped, Is.False);
        }
    }

    // Every unknown key comes back in 1 message in the order of the document.
    [Test]
    public void Read_2UnknownRootKeys_ListsBothInDocumentOrder()
    {
        string json = DocumentWith("\"zeta\": 1, \"alpha\": 2,");

        Assert.That(
            ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile has the unknown keys zeta, alpha."));
    }

    // A key that the mapper reads through ReadObject is a part of its engine. The same key fails for a mapper that doesn't read it.
    [Test]
    public void Read_ObjectTheMapperRead_IsNotUnknown()
    {
        string json = DocumentWith("\"shapes\": { \"Thing\": \"flat\" },");

        var withRead = ProfileReader.Read(
            json,
            reader =>
            {
                _ = reader.ReadObject("shapes");
                return Map(reader);
            },
            FakeVersion.Parse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withRead.IsOk, Is.True);
            Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message, Is.EqualTo("The profile has the unknown keys shapes."));
        }
    }

    // A typo of a needed part shows up as missing first, because the missing parts list what the engine needs.
    [Test]
    public void Read_MissingPartAndUnknownKey_FailsWithTheMissingPart()
    {
        string json = """{ "engine": "fake", "versions": ["1.0"], "offsets": { "Thing": { "Speed": 4 } }, "global": {} }""";

        Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message, Is.EqualTo("The profile doesn't have globals."));
    }

    // A typo of an optional offset reads as unknown, because its type misses an optional offset and has a member that nobody read.
    [Test]
    public void Read_OptionalOffsetWithATypo_FailsWithTheUnreadOffset()
    {
        string json = Offsets("\"Speed\": \"0x10\", \"Sise\": \"0x8\"");

        Assert.That(
            ProfileReader.Read(json, MapWithSize, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile has the unknown keys offsets.Thing.Sise."));
    }

    // A measured document has more offsets than a mapper reads, so an unread offset passes when the optional offsets of its type are there.
    [Test]
    public void Read_UnreadOffsetBesideAReadOptionalOffset_Maps()
    {
        string json = Offsets("\"Speed\": \"0x10\", \"Size\": \"0x8\", \"Extra\": \"0x4\"");

        Assert.That(ProfileReader.Read(json, MapWithSize, FakeVersion.Parse).Unwrap().SpeedOffset, Is.EqualTo(0x10));
    }

    // Only a type with a missing optional offset gets checked, so an unread offset passes for a mapper that reads every offset as needed.
    [Test]
    public void Read_UnreadOffsetWithoutAnOptionalRead_Maps()
    {
        string json = Offsets("\"Speed\": \"0x10\", \"Extra\": \"0x4\"");

        Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).Unwrap().SpeedOffset, Is.EqualTo(0x10));
    }

    // Keys compare by case like JSON does, so Key isn't key.
    [Test]
    public void Read_RootKeyInAnotherCase_IsUnknown()
    {
        string json = DocumentWith("\"Key\": \"x64\",");

        Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message, Is.EqualTo("The profile has the unknown keys Key."));
    }

    // An offset that doesn't read as a 4-byte number fails as an invalid document and doesn't throw.
    [TestCase("\"0x80000000\"")]
    [TestCase("\"0xZZ\"")]
    [TestCase("\"0x\"")]
    [TestCase("\"16\"")]
    [TestCase("4294967296")]
    [TestCase("true")]
    public void Read_OffsetThatDoesNotRead_FailsAsInvalid(string offset)
    {
        string json = $$"""{ "engine": "fake", "versions": ["1.0"], "offsets": { "Thing": { "Speed": {{offset}} } }, "globals": {} }""";

        Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message, Does.StartWith("The profile isn't valid. "));
    }

    // An empty version list can't be selected, so it counts as missing.
    [Test]
    public void Read_EmptyVersionList_ListsVersionsAsMissing()
    {
        Assert.That(
            ProfileReader.Read(Document("x64"), Map, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have versions."));
    }

    // A document with many versions keeps every one of them in order.
    [Test]
    public void Read_10000Versions_KeepsThemInOrder()
    {
        string[] versions = new string[10_000];

        for (int i = 0; i < versions.Length; i++)
        {
            versions[i] = $"1.{i}.0";
        }

        ProfileReader? seen = null;
        _ = ProfileReader.Read(
            Document("x64", versions),
            reader =>
            {
                seen = reader;
                return Map(reader);
            },
            FakeVersion.Parse).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen!.Versions, Has.Count.EqualTo(10_000));
            Assert.That(seen.Versions[9_999].Text, Is.EqualTo("1.9999.0"));
        }
    }

    private static Result<FakeProfile> MapWithSize(ProfileReader reader)
    {
        _ = reader.ReadOffsetOrNull("Thing", "Size");
        return Map(reader);
    }

    private static string Offsets(string thingMembers)
    {
        return $$"""
            {
              "engine": "fake",
              "versions": ["1.0"],
              "offsets": { "Thing": { {{thingMembers}} } },
              "globals": { "Things": { "1.0": [{ "forward": "A1 ????????", "offset": 1 }, { "read": "global" }] } }
            }
            """;
    }
}
