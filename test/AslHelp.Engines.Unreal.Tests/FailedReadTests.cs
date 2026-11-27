using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class FailedReadTests
{
    private const int LargestBytesPerNestedProbe = 384;
    private const int LargestBytesPerProbe = 256;
    private const int ProbeCount = 1000;
    private const int WarmUpCount = 16;

    private static readonly Address _array = new(0x8000_0000);
    private static readonly Address _chain = new(0x8010_0000);
    private static readonly ChildLayout _childLayout = new(0x2C);
    private static readonly Address _data = new(0x8000_1000);
    private static readonly Address _entry = new(0x8000_2000);
    private static readonly NameEntryLayout _entryLayout = new(0, 12);
    private static readonly ObjectLayout _objectLayout = new(0x10, 0x18, 0x24);
    private static readonly PropertyLayout _propertyLayout = new(0x20, 0x24, 0x34);
    private static readonly StructLayout _structLayout = new(0x40, 0x48);
    private static readonly Address _type = new(0x8000_5000);
    private static readonly Address _unreadable = new(0x8000_9000);

    // A search makes millions of failed reads and reads none of the messages, so a failed read stays below 256 bytes, less than 1 formatted message.
    [Test]
    public void Reader_FailedProbes_AllocateLessThanAMessage()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach ((string name, var probe) in Probes())
            {
                Assert.That(BytesPerProbe(name, probe), Is.LessThan(LargestBytesPerProbe), name);
            }
        }
    }

    // The object shape runs 2 iterators because it enumerates StructReader.ChildrenOf, so its limit is 384 bytes, less than 1 formatted message.
    [Test]
    public void PropertiesOf_ObjectShapeFailedProbe_AllocatesLessThanAMessage()
    {
        var memory = MemoryWithNames();
        ObjectPropertyReader reader = new(memory, CreateObjects(memory), CreateStructs(memory), _propertyLayout);

        long bytes = BytesPerProbe("failed", () => reader.PropertiesOf(_unreadable).First().IsErr);
        long formatted = BytesPerProbe("formatted", () => reader.PropertiesOf(_unreadable).First().UnwrapErr().Message.Length > 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes, Is.LessThan(LargestBytesPerNestedProbe));
            Assert.That(formatted, Is.GreaterThan(LargestBytesPerNestedProbe));
        }
    }

    // A wrong index 3 levels down keeps every level in the message, from the struct to the layout field of the entry.
    [Test]
    public void DerivesFrom_EntryWithAnotherIndex_KeepsEveryLevelInOrder()
    {
        FakeMemory memory = new();
        memory.AddNameArray(_array, _data, 1, _entry);
        memory.AddNameEntry(_entry, _entryLayout, 5 << 1, "Pawn");
        memory.AddObject(_type, _objectLayout, 0);

        Assert.That(
            CreateStructs(memory).DerivesFrom(_type, "Pawn").UnwrapErr().Message,
            Is.EqualTo(
                $"Can't follow the super chain of the struct at {_type} at level 1. "
                + $"Can't read the name of the object at {_type} with the name id 0. "
                + $"The entry of name 0 at {_entry} has index 10, which is name 5. "
                + $"Check NameEntryLayout.Index (0x{_entryLayout.Index:X}). "
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X})."));
    }

    // A match ends the chain, so a super pointer that does not read behind the match does not fail the call.
    [Test]
    public void DerivesFrom_MatchInFrontOfUnreadableSuper_ReturnsTrueWithoutTheRead()
    {
        var memory = MemoryWithNames();
        memory.Add(_type, new byte[_structLayout.SuperStruct]);
        memory.WriteInt32(_type + _objectLayout.Name, 0);

        var derives = CreateStructs(memory).DerivesFrom(_type, "Pawn");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(derives, Is.EqualTo(Result.Ok(true)));
            Assert.That(memory.ReadsAt(_type + _structLayout.SuperStruct), Is.Zero);
        }
    }

    // The limit ends the chain before level 65, so the call fails past the limit and doesn't read the struct at that level.
    [Test]
    public void DerivesFrom_UnreadableStructAtLevel65_FailsPastLimit()
    {
        const int Spacing = 0x100;

        var memory = MemoryWithNames();
        memory.Add(_chain, new byte[64 * Spacing]);

        for (int level = 0; level < 64; level++)
        {
            var type = _chain + (level * Spacing);
            memory.WritePointer(type + _structLayout.SuperStruct, level == 63 ? _unreadable : type + Spacing);
        }

        var derives = CreateStructs(memory).DerivesFrom(_chain, "Missing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(derives.UnwrapErr().Message, Does.StartWith($"The super chain of the struct at {_chain} goes on past 64 levels."));
            Assert.That(memory.ReadsAt(_unreadable + _objectLayout.Name), Is.Zero);
        }
    }

    // A name array that does not read keeps its array and its failed read behind the object and the name id.
    [Test]
    public void NameOf_UnreadableNameArray_WrapsTheHeaderFailure()
    {
        FakeMemory memory = new();
        memory.AddObject(_type, _objectLayout, 7);

        var count = _array + memory.Layout.PointerSize;

        Assert.That(
            CreateObjects(memory).NameOf(_type).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_type} with the name id 7. "
                + $"Can't read the header of the name array at {_array}: reading {count} failed. No block contains {count}. "
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X})."));
    }

    // A text past the limit fails with the entry, the address of the text, the limit and the layout field.
    [Test]
    public void Read_TextWithoutTerminator_FailsWithEntryAddressAndLimit()
    {
        FakeMemory memory = new();
        memory.AddNameArray(_array, _data, 1, _entry);
        memory.AddNameEntry(_entry, _entryLayout, 0, new string('x', 1025));

        Assert.That(
            new FlatNameTable(memory, _array, _entryLayout).Read(0).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the text of name 0 from its entry at {_entry}: the text at {_entry + _entryLayout.Name} doesn't have a "
                + $"terminator within 1024 characters. Check NameEntryLayout.Name (0x{_entryLayout.Name:X})."));
    }

    private static long BytesPerProbe(string name, Func<bool> probe)
    {
        for (int warmUp = 0; warmUp < WarmUpCount; warmUp++)
        {
            _ = probe();
        }

        int failed = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            failed += probe() ? 1 : 0;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(failed, Is.EqualTo(ProbeCount), name);

        return bytes / ProbeCount;
    }

    private static (string Name, Func<bool> Probe)[] Probes()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_type, _objectLayout, int.MaxValue);

        FlatNameTable names = new(memory, _array, _entryLayout);
        var objects = CreateObjects(memory);
        var structs = CreateStructs(memory);
        PointerObjectArray array = new(memory, _unreadable);
        IndirectNameTable indirectNames = new(memory, _unreadable, _entryLayout);
        ChunkedNameTable chunkedNames = new(memory, _unreadable);
        IndirectObjectArray indirectObjects = new(memory, _unreadable, new IndirectObjectArrayLayout(0, 0x800, 0x804));
        ObjectItemLayout item = new(0, 0x10);
        ItemObjectArray itemObjects = new(memory, _unreadable, new ItemObjectArrayLayout(0, 0x8), item);
        ChunkedObjectArray chunkedObjects = new(memory, _unreadable, new ChunkedObjectArrayLayout(0, 0xC, 0x14, 65536), item);
        ObjectPropertyReader objectProperties = new(memory, objects, structs, _propertyLayout);
        FieldPropertyLayout fieldLayout = new(0x04, 0x10, 0x14);
        FieldPropertyReader fieldProperties = new(memory, names, new FieldStructLayout(0x30), fieldLayout, new FieldClassLayout(0), _propertyLayout);

        return
        [
            ("Table.Read", () => names.Read(int.MaxValue).IsErr),
            ("ClassOf", () => objects.ClassOf(_unreadable).IsErr),
            ("OuterOf", () => objects.OuterOf(_unreadable).IsErr),
            ("NameOf.Unreadable", () => objects.NameOf(_unreadable).IsErr),
            ("NameOf.OutsideTheTable", () => objects.NameOf(_type).IsErr),
            ("SuperOf", () => structs.SuperOf(_unreadable).IsErr),
            ("DerivesFrom", () => structs.DerivesFrom(_unreadable, "Pawn").IsErr),
            ("ChildrenOf", () => structs.ChildrenOf(_unreadable).First().IsErr),
            ("ReadCount", () => array.ReadCount().IsErr),
            ("Array.Read", () => array.Read(1).First().IsErr),
            ("IndirectNameTable.Read", () => indirectNames.Read(0).IsErr),
            ("ChunkedNameTable.Read", () => chunkedNames.Read(0).IsErr),
            ("IndirectObjectArray.Read", () => indirectObjects.Read(1).First().IsErr),
            ("ItemObjectArray.Read", () => itemObjects.Read(1).First().IsErr),
            ("ChunkedObjectArray.Read", () => chunkedObjects.Read(1).First().IsErr),
            ("ObjectPropertyReader.ClassNameOf", () => objectProperties.ClassNameOf(_unreadable).IsErr),
            ("ObjectPropertyReader.OffsetOf", () => objectProperties.OffsetOf(_unreadable).IsErr),
            ("FieldPropertyReader.PropertiesOf", () => fieldProperties.PropertiesOf(_unreadable).First().IsErr),
            ("FieldPropertyReader.NameOf", () => fieldProperties.NameOf(_unreadable).IsErr),
            ("FieldPropertyReader.ClassNameOf", () => fieldProperties.ClassNameOf(_unreadable).IsErr),
            ("FieldPropertyReader.ArrayDimOf", () => fieldProperties.ArrayDimOf(_unreadable).IsErr),
        ];
    }

    private static ObjectReader CreateObjects(FakeMemory memory)
    {
        return new ObjectReader(memory, new FlatNameTable(memory, _array, _entryLayout), _objectLayout);
    }

    private static StructReader CreateStructs(FakeMemory memory)
    {
        return new StructReader(memory, CreateObjects(memory), _structLayout, _childLayout);
    }

    private static FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new();
        memory.AddNames(_array, _entryLayout, "Pawn");

        return memory;
    }
}
