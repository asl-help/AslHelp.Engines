using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class StringKindTests(
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

    private Result<IMemberPath> StringValue => _game.GWorld.Path.Member("GameMode").Member("Data").Member("StringValue");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // The count includes the terminator, so the text comes back without it.
    [Test]
    public void Read_String_ReadsTheTextWithoutTheTerminator()
    {
        Assert.That(StringValue.Read<string>().Unwrap(), Is.EqualTo(FakeGame.StringText));
    }

    // Counts 0 and 1 are empty, so the read doesn't touch the data pointer, which points nowhere here.
    [TestCase(0)]
    [TestCase(1)]
    public void Read_StringCountZeroOrOne_ReadsEmpty(int count)
    {
        _fakeGame.Memory.WritePointer(_fakeGame.Data + FakeGame.StringValueOffset, _fakeGame.UnreadableAddress);
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.StringCountOffset, count);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(StringValue.Read<string>().Unwrap(), Is.Empty);
            Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.Data + FakeGame.StringValueOffset), Is.Zero);
        }
    }

    // FString is UTF-16, so characters outside ASCII and outside 1 byte come back as they are.
    [Test]
    public void Read_StringWideChars_ReadsUtf16()
    {
        _fakeGame.WriteString("ü-日本-ё");

        Assert.That(StringValue.Read<string>().Unwrap(), Is.EqualTo("ü-日本-ё"));
    }

    // A count of 514 is 513 characters, past the 512 a read takes, so it fails with both numbers before it reads the characters.
    [Test]
    public void Read_StringPastBound_FailsWithLengthAndBound()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.StringCountOffset, 514);

        Assert.That(
            StringValue.Read<string>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.StringValue. StringValue has 513 characters, past the limit of 512."));
    }

    // 512 characters is the bound itself, so it still reads.
    [Test]
    public void Read_StringAtBound_ReadsTheText()
    {
        string text = new('x', 512);
        _fakeGame.WriteString(text);

        Assert.That(StringValue.Read<string>().Unwrap(), Is.EqualTo(text));
    }

    // A negative count comes from a damaged header and fails with the count.
    [Test]
    public void Read_StringNegativeCount_Fails()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.StringCountOffset, -1);

        Assert.That(
            StringValue.Read<string>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.StringValue. StringValue has the count -1, which is below 0."));
    }

    // A count of 5 with a data pointer of 0 fails with the 4 characters it should have.
    [Test]
    public void Read_StringNullDataWithCount_Fails()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.Data + FakeGame.StringValueOffset, Address.Zero);
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.StringCountOffset, 5);

        Assert.That(
            StringValue.Read<string>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.StringValue. StringValue has 4 characters, but its data pointer is 0."));
    }

    // The same characters in a new buffer come back as the text of the watcher before, so the watcher doesn't change or allocate.
    [Test]
    public void CreateWatcher_SameCharactersInNewBuffer_KeepsTheText()
    {
        ManualTicks ticks = new();
        var watcher = StringValue.Value<string>().Unwrap().CreateWatcher(ticks);
        string? first = watcher.Current;
        var otherBuffer = _fakeGame.StringData + 0x800;

        _fakeGame.Memory.WriteUtf16(otherBuffer, FakeGame.StringText);
        _fakeGame.Memory.WritePointer(_fakeGame.Data + FakeGame.StringValueOffset, otherBuffer);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.SameAs(first));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A document without the string offsets still attaches and only the string read fails with the missing paths.
    [Test]
    public void Read_StringWithoutStringOffsets_FailsWithProfilePath()
    {
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FScriptArray"));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("StringValue").Read<string>().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.StringValue. Can't read StrProperty: the profile doesn't have "
                + "offsets.FScriptArray.Data, offsets.FScriptArray.ArrayNum."));
    }
}
