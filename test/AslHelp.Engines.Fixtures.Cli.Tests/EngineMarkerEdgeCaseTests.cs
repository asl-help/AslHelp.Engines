using NUnit.Framework;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class EngineMarkerEdgeCaseTests
{
    private const string Sha1 = "0000000000000000000000000000000000000001";

    private TestFolder _folder = null!;

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

    // An empty file reads as an empty pin and not as a missing marker, because a crash during the write leaves one and it doesn't match any pin.
    [Test]
    public void Read_EmptyFile_ReturnsEmptyAndNotNull()
    {
        string folder = _folder.NewFolder();
        File.WriteAllBytes(Path.Combine(folder, EngineMarker.FileName), []);

        Assert.That(EngineMarker.Read(folder), Is.Empty);
    }

    // A byte order mark from an editor stays out of the value, so the marker still matches its pin.
    [Test]
    public void Read_MarkerWithByteOrderMark_ReturnsThePin()
    {
        string folder = _folder.NewFolder();
        File.WriteAllText(Path.Combine(folder, EngineMarker.FileName), Sha1, Encoding.UTF8);

        Assert.That(EngineMarker.Read(folder), Is.EqualTo(Sha1));
    }

    // A write over a longer marker leaves only the new pin, because a leftover tail would read as another pin.
    [Test]
    public void Write_OverLongerMarker_LeavesOnlyTheNewPin()
    {
        string folder = _folder.NewFolder();
        File.WriteAllText(Path.Combine(folder, EngineMarker.FileName), new string('f', 100));

        EngineMarker.Write(folder, Sha1);

        Assert.That(EngineMarker.Read(folder), Is.EqualTo(Sha1));
    }

    // A folder with chars outside ASCII and spaces reads and writes like any other.
    [Test]
    public void Write_FolderWithOddName_RoundTrips()
    {
        string folder = Path.Combine(_folder.Root, "ünï 日本語 [x]{0}");
        _ = Directory.CreateDirectory(folder);

        EngineMarker.Write(folder, Sha1);

        Assert.That(EngineMarker.Read(folder), Is.EqualTo(Sha1));
    }
}
