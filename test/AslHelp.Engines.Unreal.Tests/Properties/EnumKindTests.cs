using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class EnumKindTests(
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

    private Result<IMemberPath> Data => _game.GWorld.Path.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // Kind has a ByteProperty as its underlying property, so it reads as a byte at the offset of the enum.
    [Test]
    public void Read_EnumWithByteUnderlying_ReadsByte()
    {
        Assert.That(Data.Member("Kind").Read<byte>().Unwrap(), Is.EqualTo(FakeGame.Kind));
    }

    // WideKind has an IntProperty as its underlying property, so it reads as 4 bytes.
    [Test]
    public void Read_EnumWithIntUnderlying_ReadsInt32()
    {
        Assert.That(Data.Member("WideKind").Read<int>().Unwrap(), Is.EqualTo(FakeGame.WideKind));
    }

    // LongKind has an Int64Property as its underlying property, so it reads as 8 bytes.
    [Test]
    public void Read_EnumWithInt64Underlying_ReadsInt64()
    {
        Assert.That(Data.Member("LongKind").Read<long>().Unwrap(), Is.EqualTo(FakeGame.LongKind));
    }

    // An enum read as another type than its underlying property fails with the enum, its class and both types.
    [Test]
    public void Read_EnumAsAnotherType_FailsWithTheUnderlyingType()
    {
        Assert.That(
            Data.Member("Kind").Read<int>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.Kind. Kind is EnumProperty, which reads as Byte, not Int32."));
    }

    // An underlying property pointer of 0 fails with the enum and its address.
    [Test]
    public void Read_EnumUnderlyingNull_Fails()
    {
        var kind = _fakeGame.PropertyAddress("Kind");
        _fakeGame.Memory.WritePointer(kind + FakeGame.StructTypeOffset, Address.Zero);

        Assert.That(
            Data.Member("Kind").Read<byte>().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data.Kind. The underlying property pointer of Kind at {kind} is 0. "
                + "Check UnderlyingProp of EnumProperty in the profile (0x70)."));
    }

    // A document without the enum offset still attaches and only the enum read fails with the missing path.
    [Test]
    public void Read_EnumWithoutEnumOffset_FailsWithProfilePath()
    {
        string enumType = shape == "ffield" ? "FEnumProperty" : "UEnumProperty";
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", enumType));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("Kind").Read<byte>().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data.Kind. Can't read EnumProperty: the profile doesn't have offsets.{enumType}.UnderlyingProp."));
    }
}
