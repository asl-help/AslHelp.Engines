using AslHelp.Engines.Measuring.Cli.Files;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using NUnit.Framework;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Tests.Files;

public sealed class PeFileTests
{
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

    // A PE file opens with its headers read.
    [Test]
    public void Open_PeFile_ReadsTheHeaders()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(true, 0x0001_4000_0000).Ret().Build());

        using var reader = PeFile.Open(path).Unwrap();

        Assert.That(reader.PEHeaders.PEHeader!.ImageBase, Is.EqualTo(0x0001_4000_0000ul));
    }

    // A file without the PE format fails with its path, whether PEReader throws or reads it as an object file.
    [TestCase(0)]
    [TestCase(16)]
    [TestCase(0x1000)]
    public void Open_NoPeFile_FailsAsNotPe(int length)
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(path, new byte[length]);

        Assert.That(PeFile.Open(path).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNotPe(path)));
    }

    // A path with spaces and letters outside ASCII opens like any other.
    [Test]
    public void Open_PathWithSpacesAndUnicode_Opens()
    {
        string folder = Path.Combine(_folder.Root, "pl\u00E4yer \u30D5\u30A9\u30EB\u30C0");
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Ret().Build());

        using var reader = PeFile.Open(path).Unwrap();

        Assert.That(reader.PEHeaders.PEHeader, Is.Not.Null);
    }

    // A failed open closes the file, so another program can delete it right after.
    [Test]
    public void Open_NoPeFile_ClosesTheFile()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(path, new byte[0x1000]);

        _ = PeFile.Open(path).UnwrapErr();

        Assert.That(() => File.Delete(path), Throws.Nothing);
    }

    // A path that the file APIs refuse fails as unreadable, with the path.
    [TestCase("")]
    [TestCase("file\0.bin")]
    public void Open_InvalidPath_FailsAsUnreadable(string path)
    {
        Assert.That(PeFile.Open(path).UnwrapErr().Message, Does.StartWith(SR.FormatFileUnreadable(path, "")));
    }

    // A missing file fails as unreadable, with the path.
    [Test]
    public void Open_MissingFile_FailsAsUnreadable()
    {
        string path = Path.Combine(_folder.Root, "missing.bin");

        Assert.That(PeFile.Open(path).UnwrapErr().Message, Does.StartWith(SR.FormatFileUnreadable(path, "")));
    }

    // A folder at the path does not open, so it fails as unreadable and does not throw.
    [Test]
    public void Open_PathIsAFolder_FailsAsUnreadable()
    {
        Assert.That(PeFile.Open(_folder.Root).UnwrapErr().Message, Does.StartWith(SR.FormatFileUnreadable(_folder.Root, "")));
    }

    // A file that another process keeps open without sharing fails as unreadable, with the path.
    [Test]
    public void Open_OpenWithoutSharing_FailsAsUnreadable()
    {
        string path = Path.Combine(_folder.Root, "locked.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(false).Ret().Build());

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(PeFile.Open(path).UnwrapErr().Message, Does.StartWith(SR.FormatFileUnreadable(path, "")));
        }
    }
}
