using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class NameKindTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), "uproperty"),
        new(MemoryLayout.X86, new Address(0x8000_0000), "ffield"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "uproperty"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "ffield"),
    ];

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // The name number 0 reads as the text of the id alone.
    [Test]
    public void Read_NameWithoutNumber_ReadsBaseName()
    {
        Assert.That(NameValue(_game).Read().Unwrap(), Is.EqualTo(FakeGame.NameText));
    }

    // The engine stores the printed number plus 1, so the number 3 reads as _2.
    [Test]
    public void Read_NameWithNumber_AppendsNumberMinusOne()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.NameValueOffset + 4, 3);

        Assert.That(NameValue(_game).Read().Unwrap(), Is.EqualTo(FakeGame.NameText + "_2"));
    }

    // A document with the number at 8, like a build with a display index, reads the number there and not 4 bytes after the id.
    [Test]
    public void Read_NameNumberAtOffsetEight_UsesProfileLayout()
    {
        var game = _fakeGame.Attach(Set(FakeGame.Document(shape, "indirect", "flat"), "\"0x8\"", "offsets", "FName", "Number"));
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.NameValueOffset + 4, 99);
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.NameValueOffset + 8, 3);

        Assert.That(NameValue(game).Read().Unwrap(), Is.EqualTo(FakeGame.NameText + "_2"));
    }

    // A number offset 1 MB past the id fails as unreadable at that address.
    [Test]
    public void Read_NameNumberFarFromIndex_FailsUnreadable()
    {
        var game = _fakeGame.Attach(Set(FakeGame.Document(shape, "indirect", "flat"), "\"0x100000\"", "offsets", "FName", "Number"));
        var numberAddress = _fakeGame.Data + FakeGame.NameValueOffset + 0x100000;

        Assert.That(NameValue(game).Read().UnwrapErr().Message, Does.Contain($"reading {numberAddress} failed."));
    }

    // A name read keeps its last text, so a name that doesn't change comes back as the same instance.
    [Test]
    public void Read_SameNameTwice_ReturnsTheSameText()
    {
        var nameValue = NameValue(_game);
        string first = nameValue.Read().Unwrap();

        Assert.That(nameValue.Read().Unwrap(), Is.SameAs(first));
    }

    // A document without the FName offsets still attaches and only the name read fails with the missing paths.
    [Test]
    public void Read_NameWithoutNameOffsets_FailsWithProfilePath()
    {
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FName"));

        Assert.That(
            NameValue(game).Read().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.NameValue. Can't read NameProperty: the profile doesn't have "
                + "offsets.FName.ComparisonIndex, offsets.FName.Number."));
    }

    private static Result<IWatcherSource<string>> NameValue(UnrealGame game)
    {
        return game.GWorld.Path.Member("GameMode").Member("Data").Member("NameValue").Value<string>();
    }
}
