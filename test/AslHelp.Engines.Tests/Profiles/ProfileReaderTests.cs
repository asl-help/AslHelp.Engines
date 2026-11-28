using AslHelp.Engines.Profiles;
using NUnit.Framework;
using static AslHelp.Engines.Tests.Profiles.FakeProfile;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileReaderTests
{
    // The made-up engine maps its own offset and global through the public reader, the same way an engine library does.
    [Test]
    public void Read_FakeEngineDocument_MapsOffsetsAndGlobals()
    {
        var profile = ProfileReader.Read(Document("x64", "1.0"), Map, FakeVersion.Parse).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.SpeedOffset, Is.EqualTo(0x10));
            Assert.That(profile.ThingsLocator.Routes.Keys, Is.EqualTo(["1.0"]));
        }
    }

    // The engine, the key and the versions read before the mapper runs.
    [Test]
    public void Read_FakeEngineDocument_ReadsEngineKeyAndVersionsForTheMapper()
    {
        ProfileReader? seen = null;

        _ = ProfileReader.Read(
            Document("x64", "1.0", "1.1"),
            reader =>
            {
                seen = reader;
                return Map(reader);
            },
            FakeVersion.Parse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen!.Engine, Is.EqualTo(Engine));
            Assert.That(seen.Key, Is.EqualTo("x64"));
            Assert.That(seen.Versions, Is.EqualTo([FakeVersion.Parse("1.0"), FakeVersion.Parse("1.1")]));
        }
    }

    // A missing offset and a missing global come back in 1 message with their paths.
    [Test]
    public void Read_MissingOffsetAndGlobal_ListsBothPaths()
    {
        string json = """{ "engine": "fake", "versions": ["1.0"], "offsets": { "Thing": {} }, "globals": {} }""";

        Assert.That(
            ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile doesn't have offsets.Thing.Speed, globals.Things."));
    }

    // A negative offset fails with its path and value, because a member never sits in front of its object.
    [Test]
    public void Read_NegativeOffset_FailsWithPathAndValue()
    {
        string json = """{ "engine": "fake", "versions": ["1.0"], "offsets": { "Thing": { "Speed": -4 } }, "globals": {} }""";

        Assert.That(
            ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message,
            Is.EqualTo("The profile's offsets.Thing.Speed is -4, which is below 0."));
    }

    // A global that isn't a locator fails with the global and the reason of the locator converter.
    [Test]
    public void Read_GlobalThatIsNotALocator_FailsWithTheGlobal()
    {
        string json = """{ "engine": "fake", "versions": ["1.0"], "offsets": { "Thing": { "Speed": 4 } }, "globals": { "Things": 5 } }""";

        Assert.That(
            ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message,
            Does.StartWith("The profile's globals.Things isn't a locator. "));
    }

    // A typo in a key at the root fails the document, so it doesn't pass as a document without a key.
    [Test]
    public void Read_UnknownRootKey_FailsWithTheKey()
    {
        string json = DocumentWith("\"kee\": \"x64\",");

        Assert.That(ProfileReader.Read(json, Map, FakeVersion.Parse).UnwrapErr().Message, Is.EqualTo("The profile has the unknown keys kee."));
    }
}
