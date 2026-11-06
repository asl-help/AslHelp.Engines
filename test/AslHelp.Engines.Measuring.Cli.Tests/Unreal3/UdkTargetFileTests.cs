using AslHelp.Engines.Measuring.Cli.Unreal3;
using NUnit.Framework;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkTargetFileTests
{
    private const string TargetsPath = "targets.example";
    private const string ValidFile = """
        {
          "Names": { "$type": "nameArray", "firstNames": ["A", "B"] },
          "Objects": { "$type": "objectArray" }
        }
        """;

    private TestFolder _folder = null!;

    private string FilePath => Path.Combine(_folder.Root, TargetsPath);

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // A file cut off at any byte fails with the path, because a person or a copy can stop in the middle of a save.
    [Test]
    public void Read_FileCutAtAnyByte_FailsWithThePath()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ValidFile);

        for (int length = 0; length < bytes.Length; length++)
        {
            using MemoryStream stream = new(bytes, 0, length);

            Assert.That(UdkTargets.Read(stream, TargetsPath).UnwrapErr().Message, Does.Contain(TargetsPath), $"length: {length}");
        }
    }

    // Bytes that are not UTF-8 fail with the path in a key, in a value and in a file saved as UTF-16.
    [TestCase("{ \"ÿ\": { \"$type\": \"objectArray\" } }")]
    [TestCase("{ \"A\": { \"$type\": \"objectSlot\", \"class\": \"ÿ\" } }")]
    [TestCase("{ \"A\": { \"$type\": \"ÿ\" } }")]
    [TestCase("ÿþ{\0}\0")]
    public void Read_BytesThatAreNotUtf8_FailsWithThePath(string latin1)
    {
        using MemoryStream stream = new(Encoding.Latin1.GetBytes(latin1));

        Assert.That(UdkTargets.Read(stream, TargetsPath).UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // Metadata that the targets do not use and a type that is not a text fail the parse and do not throw another exception.
    [TestCase("""{ "A": { "$type": 1 } }""")]
    [TestCase("""{ "A": { "$type": null } }""")]
    [TestCase("""{ "A": { "$ref": "1" } }""")]
    [TestCase("""{ "A": { "$id": "1", "$type": "objectArray" } }""")]
    [TestCase("""{ "A": { "$type": "objectArray", "$type": "objectArray" } }""")]
    public void Read_BadMetadata_FailsWithThePath(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        Assert.That(UdkTargets.Read(stream, TargetsPath).UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // A file nested past the depth limit of the parser fails like any other bad file.
    [Test]
    public void Read_NestedFarPastTheLimit_FailsWithThePath()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(new string('[', 100_000)));

        Assert.That(UdkTargets.Read(stream, TargetsPath).UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // A file that another program has open is an expected failure of --targets, so it comes back as a result with the path.
    [Test]
    public void ReadFile_FileOpenInAnotherProgram_FailsWithThePath()
    {
        File.WriteAllText(FilePath, ValidFile);

        using FileStream locked = new(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(UdkTargets.ReadFile(FilePath).UnwrapErr().Message, Does.Contain(FilePath));
    }

    // A file in a folder that does not exist fails as a missing file and not as a failed read.
    [Test]
    public void ReadFile_MissingFolder_FailsAsMissing()
    {
        string missing = Path.Combine(_folder.Root, "missing", TargetsPath);

        Assert.That(UdkTargets.ReadFile(missing).UnwrapErr().Message, Is.EqualTo(UdkSR.FormatTargetsNotFound(missing)));
    }

    // A folder at the path exists and does not open, so it fails as a failed read and not as a missing file.
    [Test]
    public void ReadFile_PathIsAFolder_FailsAsUnreadable()
    {
        Assert.That(UdkTargets.ReadFile(_folder.Root).UnwrapErr().Message, Does.StartWith($"Can't read '{_folder.Root}': "));
    }

    // A failed load keeps the list from before, so the targets never end up half read.
    [Test]
    public void LoadTargets_BadFileAfterGoodFile_KeepsTheFirstList()
    {
        File.WriteAllText(FilePath, ValidFile);
        UdkMeasurer measurer = new();
        measurer.LoadTargets(FilePath).Unwrap();
        var first = measurer.Targets;
        File.WriteAllText(FilePath, "{}");

        var loaded = measurer.LoadTargets(FilePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.IsErr, Is.True);
            Assert.That(measurer.Targets, Is.SameAs(first));
        }
    }
}
