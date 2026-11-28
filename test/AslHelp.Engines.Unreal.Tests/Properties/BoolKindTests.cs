using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class BoolKindTests(
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

    private string BoolType => shape == "ffield" ? "FBoolProperty" : "UBoolProperty";

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // A native bool has the field mask 0xFF, so any set bit of its byte reads true and 0 reads false.
    [Test]
    public void Read_NativeBool_ReadsTrueAndFalse()
    {
        var boolValue = Data(_game).Member("BoolValue");

        bool before = boolValue.Read<bool>().Unwrap();
        _fakeGame.Memory.Write<byte>(_fakeGame.Data + 0x69, 0);

        Assert.That((before, boolValue.Read<bool>().Unwrap()), Is.EqualTo((true, false)));
    }

    // bFirst, bSecond and bThird share 1 byte with 1 and 4 set, so each reads its own bit through its field mask.
    [Test]
    public void Read_ThreeBitfieldsInOneByte_ReadsEachBit()
    {
        var data = Data(_game);

        Assert.That(
            (
                data.Member("bFirst").Read<bool>().Unwrap(),
                data.Member("bSecond").Read<bool>().Unwrap(),
                data.Member("bThird").Read<bool>().Unwrap()),
            Is.EqualTo((true, false, true)));
    }

    // bFourth has the byte offset 1 and its mask is clear in the 1st byte, so a read of the 1st byte would give false.
    [Test]
    public void Read_BitfieldInSecondByte_UsesByteOffset()
    {
        Assert.That(Data(_game).Member("bFourth").Read<bool>().Unwrap(), Is.True);
    }

    // A document without the bool offsets still attaches and only the bool read fails, with every missing path.
    [Test]
    public void Read_BoolWithoutBoolOffsets_FailsWithProfilePath()
    {
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", BoolType));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Data(game).Member("BoolValue").Read<bool>().UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read GWorld.GameMode.Data.BoolValue. Can't read BoolProperty: the profile doesn't have "
                    + $"offsets.{BoolType}.ByteOffset, offsets.{BoolType}.FieldMask."));
            Assert.That(Data(game).Member("Hp").Read<int>().Unwrap(), Is.EqualTo(FakeGame.Hp));
        }
    }

    // A mask that doesn't read fails the read and isn't kept, so the read works once the mask is there.
    [Test]
    public void Read_BoolMaskUnreadable_FailsAndReadsAgainNextTime()
    {
        var game = _fakeGame.Attach(Set(FakeGame.Document(shape, "indirect", "flat"), "\"0x800\"", "offsets", BoolType, "FieldMask"));
        var boolValue = Data(game).Member("BoolValue");
        var mask = _fakeGame.PropertyAddress("BoolValue") + 0x800;

        string failure = boolValue.Read<bool>().UnwrapErr().Message;
        _fakeGame.Memory.Add(mask, [0xFF]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                failure,
                Does.StartWith($"Can't read GWorld.GameMode.Data.BoolValue. Can't read the bool mask of BoolValue: reading {mask} failed. "));
            Assert.That(boolValue.Read<bool>().Unwrap(), Is.True);
        }
    }

    private static Result<IMemberPath> Data(UnrealGame game)
    {
        return game.GWorld.Path.Member("GameMode").Member("Data");
    }
}
