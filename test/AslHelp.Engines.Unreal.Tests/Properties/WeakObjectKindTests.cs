using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class WeakObjectKindTests(
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

    // The serial number matches the item at index 1, so the pointer reads as the world and a path goes on through it.
    [Test]
    public void Read_MatchingSerial_ReturnsTheObject()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("WeakItem").Value<Address>().Read().Unwrap(), Is.EqualTo(_fakeGame.World));
            Assert.That(Data.Member("WeakItem").Member("GameMode").Read().Unwrap(), Is.EqualTo(_fakeGame.GameMode));
        }
    }

    // The item got a new serial number, so the stale pointer reads as 0 and a path past it fails like a null object.
    [Test]
    public void Read_StaleSerial_ReturnsZeroAndFailsThePathAsNullObject()
    {
        _fakeGame.WriteItemSerial(1, 78);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("WeakItem").Value<Address>().Read().Unwrap(), Is.EqualTo(Address.Zero));
            Assert.That(
                Data.Member("WeakItem").Member("Hp").Value<int>().Read().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.WeakItem.Hp: GWorld.GameMode.Data.WeakItem is null."));
        }
    }

    // A serial number of 0 or an index below 0 is an empty pointer, which reads as 0.
    [TestCase(1, 0)]
    [TestCase(-1, 77)]
    [TestCase(500, 77)]
    public void Read_EmptyOrPastTheArray_ReturnsZero(int index, int serial)
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.WeakItemOffset, index);
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.WeakItemOffset + 4, serial);

        Assert.That(Data.Member("WeakItem").Value<Address>().Read().Unwrap(), Is.EqualTo(Address.Zero));
    }

    // A document without FUObjectItem.SerialNumber gives items without serial numbers, so a weak pointer fails with the property.
    [Test]
    public void Read_ItemsWithoutSerialNumbers_Fails()
    {
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FUObjectItem", "SerialNumber"));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("WeakItem").Value<Address>().Read().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.WeakItem. Can't resolve WeakItem: the object array doesn't have serial numbers."));
    }
}
