using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class FailedReadTests
{
    private const int LargestBytesPerProbe = 256;
    private const int ProbeCount = 1000;
    private const int WarmUpCount = 16;

    private static readonly Address _array = new(0x8000_0000);
    private static readonly Address _chain = new(0x8010_0000);
    private static readonly Address _data = new(0x8000_1000);
    private static readonly Address _entry = new(0x8000_2000);
    private static readonly NameEntryLayout _entryLayout = new(0, 12);
    private static readonly FieldLayout _fieldLayout = new(0x2C);
    private static readonly ObjectLayout _objectLayout = new(0x04, 0x10, 0x18, 0x24);
    private static readonly StructLayout _structLayout = new(0x40, 0x48, 0x50);
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
                Assert.That(bytes / ProbeCount, Is.LessThan(LargestBytesPerProbe), name);
            }
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
                + $"Check NameEntryLayout.Index (0x{_entryLayout.Index:X}) against this engine version. "
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X}) against this engine version."));
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

    // The limit ends the chain before level 65, so memory that does not read at that level does not fail the call.
    [Test]
    public void DerivesFrom_UnreadableStructAtLevel65_ReturnsFalse()
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
            Assert.That(derives, Is.EqualTo(Result.Ok(false)));
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
                + $"Check ObjectLayout.Name (0x{_objectLayout.Name:X}) against this engine version."));
    }

    // A text past the limit keeps the entry, the address of the text and the layout field around the failure of the string read.
    [Test]
    public void Read_TextWithoutTerminator_KeepsEntryAddressAndCause()
    {
        FakeMemory memory = new();
        memory.AddNameArray(_array, _data, 1, _entry);
        memory.AddNameEntry(_entry, _entryLayout, 0, new string('x', 257));

        Assert.That(
            new FlatNameTable(memory, _array, _entryLayout).Read(0).UnwrapErr().Message,
            Does.StartWith($"Can't read the text of name 0 from its entry at {_entry}: reading {_entry + _entryLayout.Name} failed.")
                .And.Contain("no terminator within 256 characters")
                .And.EndWith($"Check NameEntryLayout.Name (0x{_entryLayout.Name:X}) against this engine version."));
    }

    private static (string Name, Func<bool> Probe)[] Probes()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_type, _objectLayout, int.MaxValue);

        FlatNameTable names = new(memory, _array, _entryLayout);
        var objects = CreateObjects(memory);
        var structs = CreateStructs(memory);
        FlatObjectArray array = new(memory, _unreadable);

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
        ];
    }

    private static ObjectReader CreateObjects(FakeMemory memory)
    {
        return new ObjectReader(memory, new FlatNameTable(memory, _array, _entryLayout), _objectLayout);
    }

    private static StructReader CreateStructs(FakeMemory memory)
    {
        return new StructReader(memory, CreateObjects(memory), _structLayout, _fieldLayout);
    }

    private static FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new();
        memory.AddNames(_array, _entryLayout, "Pawn");

        return memory;
    }
}
