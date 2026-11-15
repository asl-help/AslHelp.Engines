using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System.Threading;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealImageTests
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

    // 2 folders with the same bytes give 1 tag of 16 lowercase hex chars, so every machine looks for the same image.
    [Test]
    public void Tag_SameFiles_GivesSameTag()
    {
        string first = WriteImage([1, 2, 3]);
        string second = WriteImage([1, 2, 3]);

        string tag = UnrealImage.Tag(first);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tag, Does.Match("^asl-help/unreal4-build:[0-9a-f]{16}$"));
            Assert.That(UnrealImage.Tag(second), Is.EqualTo(tag));
        }
    }

    // 1 changed byte in a script gives another tag, so an edited image folder needs a new image.
    [Test]
    public void Tag_ChangedByte_GivesOtherTag()
    {
        Assert.That(UnrealImage.Tag(WriteImage([1, 2, 3])), Is.Not.EqualTo(UnrealImage.Tag(WriteImage([1, 2, 4]))));
    }

    // A missing image fails with the docker build command of its tag and the hashed folder.
    [Test]
    public void ThrowIfMissingAsync_ImageMissing_ThrowsWithTheBuildCommand()
    {
        FakeBuildHost host = new(_ => 0) { HasImage = false };
        string image = WriteImage([1]);

        Assert.That(
            async () => await UnrealImage.ThrowIfMissingAsync(host, "asl-help/unreal4-build:0123456789abcdef", image, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains($"docker build -t asl-help/unreal4-build:0123456789abcdef \"{image}\""));
    }

    private string WriteImage(byte[] script)
    {
        string image = _folder.NewFolder();
        WriteBytes(image, "Dockerfile", [9]);
        WriteBytes(image, "cleanup.ps1", script);

        return image;
    }
}
