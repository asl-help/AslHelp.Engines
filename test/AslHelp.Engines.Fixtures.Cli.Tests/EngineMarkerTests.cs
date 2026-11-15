using NUnit.Framework;
using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class EngineMarkerTests
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

    // A folder without a marker reads as unfinished and doesn't throw.
    [Test]
    public void Read_MissingMarker_ReturnsNull()
    {
        Assert.That(EngineMarker.Read(_folder.NewFolder()), Is.Null);
    }

    // A folder that doesn't exist yet reads as unfinished, because the 1st install of an engine starts that way.
    [Test]
    public void Read_MissingFolder_ReturnsNull()
    {
        Assert.That(EngineMarker.Read(Path.Combine(_folder.Root, "missing")), Is.Null);
    }

    // A written marker reads back and a line ending from an editor doesn't change the value.
    [Test]
    public void Write_ThenRead_RoundTripsTrimmed()
    {
        string written = _folder.NewFolder();
        string edited = _folder.NewFolder();
        EngineMarker.Write(written, Sha1);
        File.WriteAllText(Path.Combine(edited, EngineMarker.FileName), $"{Sha1}{Environment.NewLine}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EngineMarker.Read(written), Is.EqualTo(Sha1));
            Assert.That(EngineMarker.Read(edited), Is.EqualTo(Sha1));
        }
    }

    // The time comes from the marker and not the folder, because a build writing into the folder doesn't make the install newer.
    [Test]
    public void WrittenAt_Marker_ReturnsItsWriteTime()
    {
        string folder = _folder.NewFolder();
        DateTime time = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        EngineMarker.Write(folder, Sha1);
        File.SetLastWriteTimeUtc(Path.Combine(folder, EngineMarker.FileName), time);

        Assert.That(EngineMarker.WrittenAt(folder), Is.EqualTo(time));
    }
}
