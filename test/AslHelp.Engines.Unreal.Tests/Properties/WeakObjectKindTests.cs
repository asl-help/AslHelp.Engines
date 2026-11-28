using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using NUnit.Framework;
using System;
using System.Text.Json;
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
            Assert.That(Data.Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(_fakeGame.World));
            Assert.That(Data.Member("WeakItem").Member("GameMode").Read<Address>().Unwrap(), Is.EqualTo(_fakeGame.GameMode));
        }
    }

    // The item got a new serial number, so the stale pointer reads as 0 and a path past it fails like a null object.
    [Test]
    public void Read_StaleSerial_ReturnsZeroAndFailsThePathAsNullObject()
    {
        _fakeGame.WriteItemSerial(1, 78);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(Address.Zero));
            Assert.That(
                Data.Member("WeakItem").Member("Hp").Read<int>().UnwrapErr().Message,
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

        Assert.That(Data.Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(Address.Zero));
    }

    // A document without FUObjectItem.SerialNumber gives items without serial numbers, so a weak pointer fails with the property.
    [Test]
    public void Read_ItemsWithoutSerialNumbers_Fails()
    {
        var game = _fakeGame.Attach(Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FUObjectItem", "SerialNumber"));

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("WeakItem").Read<Address>().UnwrapErr().Message,
            Is.EqualTo("Can't read GWorld.GameMode.Data.WeakItem. Can't resolve WeakItem: the object array doesn't have serial numbers."));
    }

    // 4.0 to 4.10 keep the serial numbers in blocks apart from the items, so a weak pointer reads its serial number from the block of its index.
    [TestCase(77, true)]
    [TestCase(78, false)]
    public void Read_SerialNumberBlocks_ComparesTheSerialOfTheBlock(int blockSerial, bool matches)
    {
        var game = AttachWithSerialNumberBlocks(out var block);
        _fakeGame.Memory.WriteInt32(block + (1 * sizeof(int)), blockSerial);

        Assert.That(
            game.GWorld.Path.Member("GameMode").Member("Data").Member("WeakItem").Read<Address>().Unwrap(),
            Is.EqualTo(matches ? _fakeGame.World : Address.Zero));
    }

    // The engine allocates a block on the 1st weak pointer into it, so a slot whose block is missing reads as a stale pointer.
    [Test]
    public void Read_SerialNumberBlockNotAllocated_ReturnsZero()
    {
        var game = AttachWithSerialNumberBlocks(out _, allocateBlock: false);

        Assert.That(game.GWorld.Path.Member("GameMode").Member("Data").Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(Address.Zero));
    }

    // The engine allocates the table on the 1st weak pointer, so a table pointer of 0 reads as a stale pointer.
    [Test]
    public void Read_SerialNumberTableNotAllocated_ReturnsZero()
    {
        var game = AttachWithSerialNumberBlocks(out _);
        _fakeGame.Memory.WritePointer(_fakeGame.Module.Range.Start + 0x900, Address.Zero);

        Assert.That(game.GWorld.Path.Member("GameMode").Member("Data").Member("WeakItem").Read<Address>().Unwrap(), Is.EqualTo(Address.Zero));
    }

    // The block of the index doesn't read, so the weak pointer fails and doesn't read as a stale pointer.
    [Test]
    public void Read_SerialNumberBlockUnreadable_Fails()
    {
        var game = AttachWithSerialNumberBlocks(out var block);
        _fakeGame.Memory.Fail(block + (1 * sizeof(int)));

        Assert.That(game.GWorld.Path.Member("GameMode").Member("Data").Member("WeakItem").Read<Address>().IsErr, Is.True);
    }

    // A block has the serial numbers of 16384 slots, so the slot 16385 reads the 2nd int of the block in the 2nd table entry.
    [Test]
    public void Read_IndexInSecondBlock_ReadsTheBlockOfTheSecondTableEntry()
    {
        var memory = _fakeGame.Memory;
        var root = _fakeGame.Module.Range.Start + 0x900;
        var table = start + 0x7C_0000;
        var secondBlock = start + 0x7C_1000;

        memory.Add(table, new byte[0x1000]);
        memory.Add(secondBlock, new byte[0x1000]);
        memory.WriteGlobalReference(_fakeGame.Module.Range.Start + 0x600, [0xF1, 0xF2, 0xF3], root);
        memory.WritePointer(root, table);
        memory.WritePointer(table + memory.Layout.PointerSize, secondBlock);
        memory.WriteInt32(secondBlock + sizeof(int), 91);

        var locator = JsonSerializer.Deserialize<MemoryLocator>("""{ "4.0": [{ "forward": "F1 F2 F3 ????????", "offset": 3 }, { "read": "global" }] }""")!;
        SerialNumberBlocks blocks = new(memory, new GlobalPointer(memory, _fakeGame.Module, locator, TimeSpan.Zero));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocks.Read((16 * 1024) + 1).Unwrap(), Is.EqualTo(91));
            Assert.That(blocks.Read(1).Unwrap(), Is.Zero);
        }
    }

    // Attach with a document whose items don't have serial numbers and whose GSerialNumberBlocksForDebugVisualizersRoot points at a
    // table with 1 block for the indices 0 to 16383.
    private UnrealGame AttachWithSerialNumberBlocks(out Address block, bool allocateBlock = true)
    {
        var memory = _fakeGame.Memory;
        var root = _fakeGame.Module.Range.Start + 0x900;
        var table = start + 0x7C_0000;
        block = start + 0x7C_1000;

        memory.Add(table, new byte[0x1000]);
        memory.Add(block, new byte[16 * 1024 * sizeof(int)]);
        memory.WriteGlobalReference(_fakeGame.Module.Range.Start + 0x600, [0xF1, 0xF2, 0xF3], root);
        memory.WritePointer(root, table);
        memory.WritePointer(table, allocateBlock ? block : Address.Zero);

        string document = Remove(FakeGame.Document(shape, "indirect", "flat"), "offsets", "FUObjectItem", "SerialNumber");
        document = Set(
            document,
            """{ "4.0": [{ "forward": "F1 F2 F3 ????????", "offset": 3 }, { "read": "global" }] }""",
            "globals",
            "GSerialNumberBlocksForDebugVisualizersRoot");

        return _fakeGame.Attach(document);
    }
}
