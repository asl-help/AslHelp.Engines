using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class StructReaderTests(
    MemoryLayout memoryLayout,
    Address start,
    ObjectLayout objectLayout,
    StructLayout layout,
    int fieldNext)
{
    private const int ActorId = 1;
    private const int BlockSpacing = 0x100;
    private const int ObjectId = 0;
    private const int PawnId = 2;
    private const int UnknownOffset = -1;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new ObjectLayout(0x04, 0x10, 0x18, 0x24), new StructLayout(0x40, 0x48, 0x50), 0x2C),
        new(MemoryLayout.X86, new Address(0x8000_0000), new ObjectLayout(0x04, 0x10, 0x18, 0x24), new StructLayout(0x64, 0x5C, 0x70), 0x38),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ObjectLayout(0x0C, 0x10, 0x18, 0x28), new StructLayout(0x60, 0x68, 0x70), 0x48),
    ];

    private readonly Address _actor = start + 0x6000;
    private readonly Address _chain = start + 0x10000;
    private readonly Address _field1 = start + 0x8000;
    private readonly Address _field2 = start + 0x8100;
    private readonly Address _field3 = start + 0x8200;
    private readonly Address _nameArray = start;
    private readonly Address _object = start + 0x7000;
    private readonly Address _pawn = start + 0x5000;
    private readonly Address _unreadable = start + 0x9000;

    // SuperOf returns the pointer at the super offset of the layout.
    [Test]
    public void SuperOf_ReadableType_ReturnsPointer()
    {
        Assert.That(CreateReader(MemoryWithTypes()).SuperOf(_pawn).Unwrap(), Is.EqualTo(_actor));
    }

    // A super pointer of 0 is a success, because the top of a chain does not have a super struct.
    [Test]
    public void SuperOf_ZeroPointer_ReturnsOkZero()
    {
        Assert.That(CreateReader(MemoryWithTypes()).SuperOf(_object), Is.EqualTo(Result.Ok(Address.Zero)));
    }

    // A failed read fails with the struct, the address of the read, the super offset and the failed read.
    [Test]
    public void SuperOf_UnreadableType_FailsWithSuperOffset()
    {
        var address = _pawn + layout.SuperStruct;

        Assert.That(
            CreateReader(MemoryWithNames()).SuperOf(_pawn).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the super struct of the struct at {_pawn}: reading {address} failed. No block contains {address}. "
                + $"Check StructLayout.SuperStruct (0x{layout.SuperStruct:X}) against this engine version."));
    }

    // A changed super returns the new pointer, because the game can reuse the address of a freed struct.
    [Test]
    public void SuperOf_SameTypeAfterMemoryChanged_ReturnsNewPointer()
    {
        var memory = MemoryWithTypes();
        var reader = CreateReader(memory);

        var first = reader.SuperOf(_pawn).Unwrap();
        memory.WritePointer(_pawn + layout.SuperStruct, _object);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(_actor));
            Assert.That(reader.SuperOf(_pawn).Unwrap(), Is.EqualTo(_object));
        }
    }

    // The name of the type itself matches.
    [Test]
    public void DerivesFrom_TypeItself_ReturnsTrue()
    {
        Assert.That(CreateReader(MemoryWithTypes()).DerivesFrom(_pawn, "Pawn").Unwrap(), Is.True);
    }

    // A name 2 levels up the super chain matches.
    [Test]
    public void DerivesFrom_Ancestor_ReturnsTrue()
    {
        Assert.That(CreateReader(MemoryWithTypes()).DerivesFrom(_pawn, "Object").Unwrap(), Is.True);
    }

    // A name that is not on the chain returns false, even when it differs from a name on the chain only in case.
    [Test]
    public void DerivesFrom_NoMatch_ReturnsFalse()
    {
        var reader = CreateReader(MemoryWithTypes());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.DerivesFrom(_pawn, "Missing").Unwrap(), Is.False);
            Assert.That(reader.DerivesFrom(_pawn, "object").Unwrap(), Is.False);
        }
    }

    // Address 0 returns false with no memory read, even when address 0 has an object with the name.
    [Test]
    public void DerivesFrom_AddressZero_ReturnsFalseWithNoRead()
    {
        var memory = MemoryWithNames();
        memory.AddObject(Address.Zero, objectLayout, PawnId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CreateReader(memory).DerivesFrom(Address.Zero, "Pawn").Unwrap(), Is.False);
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A struct with a negative name number fails the chain with the level and that number, because the real name of the struct stays unknown.
    [Test]
    public void DerivesFrom_UnnamedTypeOnChain_FailsWithNegativeNumber()
    {
        var memory = MemoryWithTypes();
        memory.WriteInt32(_actor + objectLayout.Name + sizeof(int), -1);

        Assert.That(
            CreateReader(memory).DerivesFrom(_pawn, "Object").UnwrapErr().Message,
            Does.StartWith($"Can't follow the super chain of the struct at {_pawn} at level 2.")
                .And.Contain($"The object at {_actor} has the name number -1"));
    }

    // A struct with a number matches by its base name and not by a name with the number as a suffix.
    [Test]
    public void DerivesFrom_TypeWithNumber_MatchesBaseName()
    {
        var memory = MemoryWithTypes();
        memory.WriteInt32(_actor + objectLayout.Name + sizeof(int), 5);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.DerivesFrom(_pawn, "Actor").Unwrap(), Is.True);
            Assert.That(reader.DerivesFrom(_pawn, "Actor_4").Unwrap(), Is.False);
        }
    }

    // An empty name on a struct whose name does not read fails, because a failed read is not an empty text.
    [Test]
    public void DerivesFrom_EmptyNameOnUnnamedType_Fails()
    {
        var memory = MemoryWithTypes();
        memory.WriteInt32(_pawn + objectLayout.Name + sizeof(int), -1);

        Assert.That(CreateReader(memory).DerivesFrom(_pawn, string.Empty).UnwrapErr().Message, Does.Contain("at level 1."));
    }

    // A super pointer into unreadable memory fails with the level and the failed read of the name and does not return false.
    [Test]
    public void DerivesFrom_UnreadableAncestor_FailsWithLevelAndCause()
    {
        var memory = MemoryWithTypes();
        memory.WritePointer(_pawn + layout.SuperStruct, _unreadable);

        var nameAddress = _unreadable + objectLayout.Name;

        Assert.That(
            CreateReader(memory).DerivesFrom(_pawn, "Object").UnwrapErr().Message,
            Does.StartWith($"Can't follow the super chain of the struct at {_pawn} at level 2.")
                .And.Contain($"Can't read the name of the object at {_unreadable}")
                .And.Contain($"No block contains {nameAddress}."));
    }

    // A super pointer that does not read fails with the level of the struct it belongs to.
    [Test]
    public void DerivesFrom_UnreadableSuperPointer_FailsWithSuperOffset()
    {
        var memory = MemoryWithNames();
        memory.Add(_pawn, new byte[layout.SuperStruct]);
        memory.WriteInt32(_pawn + objectLayout.Name, PawnId);

        Assert.That(
            CreateReader(memory).DerivesFrom(_pawn, "Object").UnwrapErr().Message,
            Does.StartWith($"Can't follow the super chain of the struct at {_pawn} at level 1.")
                .And.Contain($"StructLayout.SuperStruct (0x{layout.SuperStruct:X})"));
    }

    // A type that is its own super ends after 64 levels with no failure, because a damaged chain can loop.
    [Test]
    public void DerivesFrom_LoopingChain_ReturnsFalseAfter64Levels()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_pawn, objectLayout, PawnId);
        memory.WritePointer(_pawn + layout.SuperStruct, _pawn);

        var derives = CreateReader(memory).DerivesFrom(_pawn, "Object");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(derives, Is.EqualTo(Result.Ok(false)));
            Assert.That(memory.ReadsAt(_pawn + objectLayout.Name), Is.EqualTo(64));
        }
    }

    // The type itself is level 1, so a match at level 64 counts and a match at level 65 does not.
    [TestCase(64, true)]
    [TestCase(65, false)]
    public void DerivesFrom_MatchAtLevel_ChecksOnly64Levels(int level, bool expected)
    {
        var memory = MemoryWithNames();
        memory.Add(_chain, new byte[level * BlockSpacing]);

        for (int i = 1; i < level; i++)
        {
            var type = _chain + ((i - 1) * BlockSpacing);
            memory.WriteInt32(type + objectLayout.Name, ActorId);
            memory.WritePointer(type + layout.SuperStruct, type + BlockSpacing);
        }

        memory.WriteInt32(_chain + ((level - 1) * BlockSpacing) + objectLayout.Name, ObjectId);

        Assert.That(CreateReader(memory).DerivesFrom(_chain, "Object").Unwrap(), Is.EqualTo(expected));
    }

    // Text that is equal only under a culture compare does not match, because the compare is ordinal.
    [TestCase("Å", "Å")]
    [TestCase("Pawn", "Pa­wn")]
    public void DerivesFrom_NamesEqualUnderCulture_ReturnsFalse(string text, string name)
    {
        var data = _nameArray + BlockSpacing;
        var entry = _nameArray + (2 * BlockSpacing);
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNameArray(_nameArray, data, 1, entry);
        memory.AddNameEntry(entry, _entryLayout, 1, text);
        memory.AddObject(_pawn, objectLayout, 0);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.DerivesFrom(_pawn, text).Unwrap(), Is.True);
            Assert.That(reader.DerivesFrom(_pawn, name).Unwrap(), Is.False);
        }
    }

    // A changed super changes the result, because the game can reuse the address of a freed struct.
    [Test]
    public void DerivesFrom_SameTypeAfterSuperChanged_ReadsAgain()
    {
        var memory = MemoryWithTypes();
        var reader = CreateReader(memory);

        bool first = reader.DerivesFrom(_pawn, "Object").Unwrap();
        memory.WritePointer(_pawn + layout.SuperStruct, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.True);
            Assert.That(reader.DerivesFrom(_pawn, "Object").Unwrap(), Is.False);
        }
    }

    // DerivesFrom reads only the name and super offsets, so the reader works before the measurer has found the other offsets.
    [Test]
    public void DerivesFrom_OnlyNameAndSuperOffsetsKnown_ReturnsTrue()
    {
        var memory = MemoryWithTypes();
        var reader = CreateReader(
            memory,
            objectLayout with { Index = UnknownOffset, Outer = UnknownOffset, Class = UnknownOffset },
            layout with { Children = UnknownOffset, PropertySize = UnknownOffset });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.DerivesFrom(_pawn, "Object").Unwrap(), Is.True);
            Assert.That(memory.ReadsAt(_pawn + UnknownOffset), Is.Zero);
        }
    }

    // A null name throws with its parameter name before any memory read.
    [Test]
    public void DerivesFrom_NullName_ThrowsArgumentNullException()
    {
        var memory = MemoryWithTypes();
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => reader.DerivesFrom(_pawn, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("name"));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // The children pointer and then the next pointers of the fields give the fields in chain order.
    [Test]
    public void ChildrenOf_ChainOfThree_ReturnsAllInOrder()
    {
        Address[] expected = [_field1, _field2, _field3];

        Assert.That(Fields(CreateReader(MemoryWithChain()), _pawn), Is.EqualTo(expected));
    }

    // A null children pointer enumerates nothing.
    [Test]
    public void ChildrenOf_NullChildren_ReturnsNothing()
    {
        Assert.That(CreateReader(MemoryWithTypes()).ChildrenOf(_pawn), Is.Empty);
    }

    // An unreadable type yields 1 failure with the children offset.
    [Test]
    public void ChildrenOf_UnreadableType_YieldsOneFailure()
    {
        var items = CreateReader(MemoryWithNames()).ChildrenOf(_pawn).ToList();
        var address = _pawn + layout.Children;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the fields of the struct at {_pawn}: reading {address} failed. No block contains {address}. "
                    + $"Check StructLayout.Children (0x{layout.Children:X}) against this engine version."));
        }
    }

    // A 1st field with another outer enumerates nothing.
    [Test]
    public void ChildrenOf_FirstFieldWithAnotherOuter_ReturnsNothing()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_field1 + objectLayout.Outer, _actor);

        Assert.That(CreateReader(memory).ChildrenOf(_pawn), Is.Empty);
    }

    // A field with another outer ends the chain before that field with no failure.
    [Test]
    public void ChildrenOf_FieldWithAnotherOuter_StopsWithoutFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_field2 + objectLayout.Outer, _actor);

        Address[] expected = [_field1];

        Assert.That(Fields(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A next pointer into unreadable memory ends the chain with a failure that has the struct, the position of the field and its failed outer read.
    [Test]
    public void ChildrenOf_UnreadableField_YieldsFieldThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_field1 + fieldNext, _unreadable);

        var items = CreateReader(memory).ChildrenOf(_pawn).ToList();
        var address = _unreadable + objectLayout.Outer;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_field1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read field 2 of the struct at {_pawn}. "
                    + $"Can't read the outer object of the object at {_unreadable}: reading {address} failed. No block contains {address}. "
                    + $"Check ObjectLayout.Outer (0x{objectLayout.Outer:X}) against this engine version."));
        }
    }

    // A garbage next pointer at the top of the address range wraps around to unreadable memory and ends the chain with a failure.
    [Test]
    public void ChildrenOf_NextPointerAtEndOfAddressRange_YieldsFieldThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_field1 + fieldNext, new Address(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue));

        var items = CreateReader(memory).ChildrenOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_field1));
            Assert.That(items[1].IsErr, Is.True);
        }
    }

    // A field with an unreadable next pointer still comes back and the failure after it has the next offset.
    [Test]
    public void ChildrenOf_UnreadableNextPointer_YieldsFieldThenFailure()
    {
        var memory = MemoryWithTypes();
        memory.WritePointer(_pawn + layout.Children, _field1);
        memory.Add(_field1, new byte[fieldNext]);
        memory.WritePointer(_field1 + objectLayout.Outer, _pawn);

        var items = CreateReader(memory).ChildrenOf(_pawn).ToList();
        var address = _field1 + fieldNext;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_field1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the next field after the field at {_field1}: reading {address} failed. No block contains {address}. "
                    + $"Check FieldLayout.Next (0x{fieldNext:X}) against this engine version."));
        }
    }

    // A field that is its own next field ends after 4096 fields with no failure, because a damaged chain can loop.
    [Test]
    public void ChildrenOf_LoopingChain_StopsAt4096()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_field1 + fieldNext, _field1);

        Assert.That(Fields(CreateReader(memory), _pawn), Has.Count.EqualTo(4096));
    }

    // A chain of 4096 ends at its null pointer and a chain of 4097 drops the last field with no read of its outer pointer.
    [TestCase(4096)]
    [TestCase(4097)]
    public void ChildrenOf_ChainAroundLimit_ReturnsFirst4096(int length)
    {
        var memory = MemoryWithTypes();
        memory.Add(_chain, new byte[length * BlockSpacing]);
        memory.WritePointer(_pawn + layout.Children, _chain);

        for (int i = 0; i < length; i++)
        {
            var field = _chain + (i * BlockSpacing);
            memory.WritePointer(field + objectLayout.Outer, _pawn);
            memory.WritePointer(field + fieldNext, i + 1 < length ? field + BlockSpacing : Address.Zero);
        }

        var fields = Fields(CreateReader(memory), _pawn);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fields, Has.Count.EqualTo(4096));
            Assert.That(fields[^1], Is.EqualTo(_chain + (4095 * BlockSpacing)));
            Assert.That(memory.ReadsAt(_chain + (4096 * BlockSpacing) + objectLayout.Outer), Is.Zero);
        }
    }

    // Calling ChildrenOf with no enumeration reads nothing, because the enumeration is lazy.
    [Test]
    public void ChildrenOf_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithChain();

        _ = CreateReader(memory).ChildrenOf(_pawn);

        Assert.That(memory.Reads, Is.Zero);
    }

    // Taking only the 1st field reads neither its next pointer nor the 2nd field, because the enumeration is lazy.
    [Test]
    public void ChildrenOf_FirstOnly_ReadsNoFurtherField()
    {
        var memory = MemoryWithChain();

        var first = CreateReader(memory).ChildrenOf(_pawn).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Unwrap(), Is.EqualTo(_field1));
            Assert.That(memory.ReadsAt(_field1 + fieldNext), Is.Zero);
            Assert.That(memory.ReadsAt(_field2 + objectLayout.Outer), Is.Zero);
        }
    }

    // A 2nd enumeration of the same result reads the memory again, because the game can free a field and reuse its address.
    [Test]
    public void ChildrenOf_EnumeratedTwice_ReadsAgain()
    {
        var memory = MemoryWithChain();
        var children = CreateReader(memory).ChildrenOf(_pawn);

        var first = children.Select(static item => item.Unwrap()).ToList();
        memory.WritePointer(_field1 + fieldNext, Address.Zero);

        Address[] expectedFirst = [_field1, _field2, _field3];
        Address[] expectedSecond = [_field1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(expectedFirst));
            Assert.That(children.Select(static item => item.Unwrap()), Is.EqualTo(expectedSecond));
        }
    }

    // ChildrenOf reads only the children, outer and next offsets, so the reader works before the measurer has found the name array.
    [Test]
    public void ChildrenOf_OnlyChildrenOuterAndNextOffsetsKnown_ReturnsFields()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddObject(_pawn, objectLayout, PawnId);
        memory.WritePointer(_pawn + layout.Children, _field1);
        memory.AddObject(_field1, objectLayout, ObjectId, outer: _pawn);
        var reader = CreateReader(
            memory,
            objectLayout with { Index = UnknownOffset, Name = UnknownOffset, Class = UnknownOffset },
            layout with { SuperStruct = UnknownOffset, PropertySize = UnknownOffset });

        Address[] expected = [_field1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Fields(reader, _pawn), Is.EqualTo(expected));
            Assert.That(memory.ReadsAt(_pawn + UnknownOffset), Is.Zero);
            Assert.That(memory.ReadsAt(_field1 + UnknownOffset), Is.Zero);
            Assert.That(memory.ReadsAt(_nameArray), Is.Zero);
        }
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        var objects = CreateObjects(new FakeMemory(), objectLayout);

        Assert.That(
            () => new StructReader(null!, objects, layout, new FieldLayout(fieldNext)),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    // A null object reader throws with its parameter name.
    [Test]
    public void Constructor_NullObjects_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new StructReader(new FakeMemory(), null!, layout, new FieldLayout(fieldNext)),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("objects"));
    }

    // A null layout throws with its parameter name.
    [Test]
    public void Constructor_NullLayout_ThrowsArgumentNullException()
    {
        FakeMemory memory = new();
        var objects = CreateObjects(memory, objectLayout);

        Assert.That(
            () => new StructReader(memory, objects, null!, new FieldLayout(fieldNext)),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("layout"));
    }

    [Test]
    public void Constructor_NullFieldLayout_ThrowsArgumentNullException()
    {
        FakeMemory memory = new();
        var objects = CreateObjects(memory, objectLayout);

        Assert.That(
            () => new StructReader(memory, objects, layout, null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("fieldLayout"));
    }

    private static List<Address> Fields(StructReader reader, Address type)
    {
        return [.. reader.ChildrenOf(type).Select(static item => item.Unwrap())];
    }

    private FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(_nameArray, _entryLayout, "Object", "Actor", "Pawn");

        return memory;
    }

    // Lays out the super chain Pawn, Actor, Object.
    private FakeMemory MemoryWithTypes()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_pawn, objectLayout, PawnId);
        memory.WritePointer(_pawn + layout.SuperStruct, _actor);
        memory.AddObject(_actor, objectLayout, ActorId);
        memory.WritePointer(_actor + layout.SuperStruct, _object);
        memory.AddObject(_object, objectLayout, ObjectId);

        return memory;
    }

    // Gives Pawn a chain of 3 fields that ends in a null pointer.
    private FakeMemory MemoryWithChain()
    {
        var memory = MemoryWithTypes();
        memory.WritePointer(_pawn + layout.Children, _field1);
        memory.AddObject(_field1, objectLayout, ObjectId, outer: _pawn);
        memory.WritePointer(_field1 + fieldNext, _field2);
        memory.AddObject(_field2, objectLayout, ObjectId, outer: _pawn);
        memory.WritePointer(_field2 + fieldNext, _field3);
        memory.AddObject(_field3, objectLayout, ObjectId, outer: _pawn);

        return memory;
    }

    private ObjectReader CreateObjects(FakeMemory memory, ObjectLayout objects)
    {
        return new ObjectReader(memory, new FlatNameTable(memory, _nameArray, _entryLayout), objects);
    }

    private StructReader CreateReader(FakeMemory memory)
    {
        return CreateReader(memory, objectLayout, layout);
    }

    private StructReader CreateReader(FakeMemory memory, ObjectLayout objects, StructLayout structs)
    {
        return new StructReader(memory, CreateObjects(memory, objects), structs, new FieldLayout(fieldNext));
    }
}
