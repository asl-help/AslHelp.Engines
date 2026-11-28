using AslHelp.Engines.Profiles;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text;
using static AslHelp.Engines.Tests.Profiles.FakeProfile;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileCatalogEdgeTests
{
    private ProfileCatalog<FakeProfile> _catalog = null!;
    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ProfileCatalog<FakeProfile>(Map, FakeVersion.Parse);
        _folder = Path.Combine(Path.GetTempPath(), $"catalog-{TestContext.CurrentContext.Test.ID}");
        _ = Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_folder, recursive: true);
    }

    // Text that isn't JSON fails with the source and the reason of the JSON reader and leaves the catalog as it was.
    [Test]
    public void Add_InvalidJson_FailsAndKeepsTheCatalog()
    {
        _ = _catalog.Add("file.json", Document("x64", "1.0.0")).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _catalog.Add("file.json", "{ \"engine\": ").UnwrapErr().Message,
                Does.StartWith("Can't read the profile file.json. The profile isn't valid. "));
            Assert.That(_catalog.Entries, Has.Count.EqualTo(1));
        }
    }

    // A document without engine and versions lists both in 1 message.
    [Test]
    public void Add_DocumentWithoutEngineOrVersions_ListsBoth()
    {
        Assert.That(
            _catalog.Add("file.json", "{}").UnwrapErr().Message,
            Is.EqualTo("Can't read the profile file.json. The profile doesn't have engine, versions."));
    }

    // A mapper that doesn't check the reader still can't add a document with missing parts.
    [Test]
    public void Add_MapperSkipsFailure_FailsWithTheMissingParts()
    {
        ProfileCatalog<FakeProfile> catalog = new(static reader => new FakeProfile(0, null!), FakeVersion.Parse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                catalog.Add("file.json", """{ "engine": "fake" }""").UnwrapErr().Message,
                Is.EqualTo("Can't read the profile file.json. The profile doesn't have versions."));
            Assert.That(catalog.Entries, Is.Empty);
        }
    }

    // A version that the parser of the engine rejects can't be selected, so the document fails with that version and the engine.
    [Test]
    public void Add_VersionTheParserRejects_FailsWithTheVersionAndTheEngine()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _catalog.Add("file.json", Document("x64", "1.0.0", "beta")).UnwrapErr().Message,
                Is.EqualTo("Can't read the profile file.json. The profile's version 'beta' isn't a version of fake."));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    // A document added as text keeps the passed source.
    [Test]
    public void Add_Text_SetsSourceToThePassedName()
    {
        Assert.That(_catalog.Add("files.example", Document("x64", "1.0.0")).Unwrap().Source, Is.EqualTo("files.example"));
    }

    // A target without a patch matches a listed patch 0 of its line, so 1.2 doesn't come with a warning for a listed 1.2.0.
    [Test]
    public void Select_TargetWithoutAPatchAndPatch0Listed_IsListed()
    {
        _ = _catalog.Add("file.json", Document("x64", "1.2.0")).Unwrap();

        var selection = _catalog.Select(new ProfileTarget(Engine, FakeVersion.Parse("1.2")!, "x64"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("1.2.0"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
        }
    }

    // The mapper's own error comes back after the source and nothing is added.
    [Test]
    public void Add_MapperFailure_ReturnsTheMapperError()
    {
        string json = """{ "engine": "other", "versions": ["1.0.0"] }""";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _catalog.Add("file.json", json).UnwrapErr().Message,
                Is.EqualTo("Can't read the profile file.json. The fake mapper doesn't read 'other'."));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    // A file that isn't there is a failure with its path and not an exception.
    [Test]
    public void AddFile_MissingFile_FailsWithThePath()
    {
        string path = Path.Combine(_folder, "file.json");

        Assert.That(_catalog.AddFile(path).UnwrapErr().Message, Does.StartWith($"Can't read the profile {path}. "));
    }

    // A file document keeps its path as the source, so a message about the pick can show it.
    [Test]
    public void AddFile_Document_SetsSourceToThePath()
    {
        string path = Path.Combine(_folder, "file.json");
        File.WriteAllText(path, Document("x64", "1.0.0", "1.1.0"));

        var entry = _catalog.AddFile(path).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Source, Is.EqualTo(path));
            Assert.That(entry.Engine, Is.EqualTo(Engine));
            Assert.That(entry.Key, Is.EqualTo("x64"));
            Assert.That(entry.Versions.Select(static version => version.Text), Is.EqualTo(["1.0.0", "1.1.0"]));
            Assert.That(entry.Profile.SpeedOffset, Is.EqualTo(0x10));
        }
    }

    // A file that another handle keeps open without sharing fails with its path instead of throwing.
    [Test]
    public void AddFile_FileLockedByAnotherHandle_FailsWithThePath()
    {
        string path = Path.Combine(_folder, "file.json");
        File.WriteAllText(path, Document("x64", "1.0.0"));

        using FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_catalog.AddFile(path).UnwrapErr().Message, Does.StartWith($"Can't read the profile {path}. "));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    // A stream that ends in the middle of a document, like a partly written file, fails without adding anything.
    [Test]
    public void Add_StreamCutOffInTheDocument_FailsAsInvalid()
    {
        byte[] document = Encoding.UTF8.GetBytes(Document("x64", "1.0.0"));
        using MemoryStream stream = new(document, 0, document.Length / 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _catalog.Add("file.json", stream).UnwrapErr().Message,
                Does.StartWith("Can't read the profile file.json. The profile isn't valid. "));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    // A file whose document doesn't map fails with its path and the reason, so the file to fix is clear.
    [Test]
    public void AddFile_DocumentThatDoesNotMap_FailsWithThePathAndTheReason()
    {
        string path = Path.Combine(_folder, "file.json");
        File.WriteAllText(path, "{}");

        Assert.That(
            _catalog.AddFile(path).UnwrapErr().Message,
            Is.EqualTo($"Can't read the profile {path}. The profile doesn't have engine, versions."));
    }

    // A stream reads as UTF-8 JSON and its entry keeps the passed source.
    [Test]
    public void Add_Stream_SetsSourceToThePassedName()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(Document(null, "1.0.0")));

        var entry = _catalog.Add("files.example", stream).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Source, Is.EqualTo("files.example"));
            Assert.That(entry.Key, Is.Null);
            Assert.That(_catalog.Entries, Is.EqualTo([entry]));
        }
    }

    // A stream with a UTF-8 BOM reads like a stream without a BOM.
    [Test]
    public void Add_StreamWithUtf8Bom_ReadsTheDocument()
    {
        using MemoryStream stream = new([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Document("x64", "1.0"))]);

        Assert.That(_catalog.Add("file.json", stream).Unwrap().Profile.SpeedOffset, Is.EqualTo(0x10));
    }

    // An empty stream isn't a document and fails without adding anything.
    [Test]
    public void Add_EmptyStream_FailsAsInvalid()
    {
        using MemoryStream stream = new();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _catalog.Add("file.json", stream).UnwrapErr().Message,
                Does.StartWith("Can't read the profile file.json. The profile isn't valid. "));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    // An assembly other than AslHelp.Engines embeds its own documents, which load in ordinal order of their names.
    [Test]
    public void AddEmbedded_PrefixOfTheTestAssembly_AddsBothDocumentsInNameOrder()
    {
        _catalog.AddEmbedded(typeof(FakeProfile).Assembly, "Documents/").Unwrap();

        Assert.That(_catalog.Entries.Select(static entry => entry.Source), Is.EqualTo(["Documents/fake-1.0.0.json", "Documents/fake-1.2.0.json"]));
    }

    // A prefix without resources adds nothing and doesn't fail.
    [Test]
    public void AddEmbedded_PrefixWithoutResources_AddsNothing()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_catalog.AddEmbedded(typeof(FakeProfile).Assembly, "Missing/").IsOk, Is.True);
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    // A resource that doesn't map fails with its name and the document in front of it doesn't get added either.
    [Test]
    public void AddEmbedded_ResourceThatDoesNotMap_FailsWithTheNameAndAddsNothing()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _catalog.AddEmbedded(typeof(FakeProfile).Assembly, "Broken/").UnwrapErr().Message,
                Is.EqualTo("Can't read the profile Broken/file.json. The profile doesn't have versions, offsets, globals."));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }
}
