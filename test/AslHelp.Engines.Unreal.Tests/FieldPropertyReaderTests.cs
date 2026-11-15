using AslHelp.Memory;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class FieldPropertyReaderTests(
    MemoryLayout memoryLayout,
    Address start,
    FieldStructLayout structLayout,
    FieldPropertyLayout fieldLayout,
    FieldClassLayout classLayout,
    PropertyLayout layout)
{
    private const int BlockSpacing = 0x100;
    private const int ChildrenOffset = 0x10;
    private const int IntPropertyId = 1;
    private const int IntValueId = 2;
    private const int OtherValueId = 3;
    private const int PastBlock = 0x200;
    private const int TickId = 4;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);

    // Use the 4.25 x86, 4.25 x64 and 5.8 x64 profiles, with ArrayDim 4 bytes in front of ElementSize.
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(
            MemoryLayout.X86,
            new Address(0x8000_0000),
            new FieldStructLayout(0x30),
            new FieldPropertyLayout(0x04, 0x10, 0x14),
            new FieldClassLayout(0x00),
            new PropertyLayout(0x20, 0x24, 0x34)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            new FieldStructLayout(0x50),
            new FieldPropertyLayout(0x08, 0x20, 0x28),
            new FieldClassLayout(0x00),
            new PropertyLayout(0x38, 0x3C, 0x4C)),
        new(
            MemoryLayout.X64,
            new Address(0x7FF6_0000_0000),
            new FieldStructLayout(0x50),
            new FieldPropertyLayout(0x08, 0x18, 0x20),
            new FieldClassLayout(0x08),
            new PropertyLayout(0x30, 0x34, 0x44)),
    ];

    private readonly Address _actor = start + 0x5100;
    private readonly Address _block = start + 0x7000;
    private readonly Address _chain = start + 0x10_0000;
    private readonly Address _class = start + 0x6000;
    private readonly Address _function = start + 0x8000;
    private readonly Address _nameArray = start;
    private readonly Address _pawn = start + 0x5000;
    private readonly Address _pool = start + 0x6800;
    private readonly Address _property1 = start + 0x9000;
    private readonly Address _property2 = start + 0xA000;
    private readonly Address _property3 = start + 0xB000;
    private readonly Address _superProperty = start + 0xC000;
    private readonly Address _unreadable = start + 0xF000;

    // PropertiesOf follows the child properties pointer and the next pointers in chain order and doesn't check the class.
    [Test]
    public void PropertiesOf_ChainOfThree_ReturnsAllInOrder()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldLayout.Class, Address.Zero);

        Address[] expected = [_property1, _property2, _property3];

        Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
    }

    // A null child properties pointer enumerates nothing.
    [Test]
    public void PropertiesOf_NullChildProperties_ReturnsNothing()
    {
        Assert.That(CreateReader(MemoryWithStructs()).PropertiesOf(_pawn), Is.Empty);
    }

    // An unreadable struct yields 1 failure with the child properties offset.
    [Test]
    public void PropertiesOf_UnreadableType_YieldsOneFailure()
    {
        var items = CreateReader(MemoryWithNames()).PropertiesOf(_pawn).ToList();
        var address = _pawn + structLayout.ChildProperties;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(
                items[0].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the properties of the struct at {_pawn}: reading {address} failed. No block contains {address}. "
                    + $"Check FieldStructLayout.ChildProperties (0x{structLayout.ChildProperties:X})."));
        }
    }

    // The Children pointer lists functions and ChildProperties lists properties, so a reader with the wrong offset returns another list.
    [Test]
    public void PropertiesOf_WrongChildPropertiesOffset_ReadsTheFunctionChain()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_pawn + ChildrenOffset, _function);
        memory.AddField(_function, fieldLayout, TickId);

        var wrong = CreateReader(memory, new FieldStructLayout(ChildrenOffset));

        Address[] expected = [_property1, _property2, _property3];
        Address[] expectedWrong = [_function];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Properties(CreateReader(memory), _pawn), Is.EqualTo(expected));
            Assert.That(Properties(wrong, _pawn), Is.EqualTo(expectedWrong));
        }
    }

    // A wrong next offset fails after the property in front of it, with the property and the next offset.
    [Test]
    public void PropertiesOf_UnreadableNextPointer_YieldsPropertyThenFailure()
    {
        var reader = CreateReader(MemoryWithChain(), structLayout, fieldLayout with { Next = PastBlock });

        var items = reader.PropertiesOf(_pawn).ToList();
        var address = _property1 + PastBlock;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Unwrap(), Is.EqualTo(_property1));
            Assert.That(
                items[1].UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the next property after the property at {_property1}: reading {address} failed. No block contains {address}. "
                    + $"Check FieldPropertyLayout.Next (0x{PastBlock:X})."));
        }
    }

    // A next pointer into unreadable memory yields that address and then fails its own next read, because the list only reads the next pointers.
    [Test]
    public void PropertiesOf_NextPointerIntoUnreadableMemory_YieldsAddressThenFailure()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldLayout.Next, _unreadable);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[1].Unwrap(), Is.EqualTo(_unreadable));
            Assert.That(items[2].UnwrapErr().Message, Does.StartWith($"Can't read the next property after the property at {_unreadable}:"));
        }
    }

    // A property that is its own next property fails after 4096 items, because a damaged chain can loop.
    [Test]
    public void PropertiesOf_LoopingChain_FailsAfter4096()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldLayout.Next, _property1);

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(4097));
            Assert.That(
                items[^1].UnwrapErr().Message,
                Is.EqualTo(
                    $"The struct at {_pawn} has more than 4096 properties. "
                    + $"Check FieldPropertyLayout.Next (0x{fieldLayout.Next:X})."));
        }
    }

    // A chain of 4096 ends at its null pointer and a chain of 4097 fails and doesn't read the next pointer of the last property.
    [TestCase(4096, false)]
    [TestCase(4097, true)]
    public void PropertiesOf_ChainAroundLimit_ReturnsFirst4096AndFailsPastIt(int length, bool fails)
    {
        var memory = MemoryWithStructs();
        memory.Add(_chain, new byte[length * BlockSpacing]);
        memory.WritePointer(_pawn + structLayout.ChildProperties, _chain);

        for (int i = 0; i < length; i++)
        {
            var field = _chain + (i * BlockSpacing);
            memory.WritePointer(field + fieldLayout.Next, i + 1 < length ? field + BlockSpacing : Address.Zero);
        }

        var items = CreateReader(memory).PropertiesOf(_pawn).ToList();
        var properties = items.Where(static item => item.IsOk).Select(static item => item.Unwrap()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(properties, Has.Count.EqualTo(4096));
            Assert.That(properties[^1], Is.EqualTo(_chain + (4095 * BlockSpacing)));
            Assert.That(items[^1].IsErr, Is.EqualTo(fails));
            Assert.That(memory.ReadsAt(_chain + (4096 * BlockSpacing) + fieldLayout.Next), Is.Zero);
        }
    }

    // The list of Pawn ends at its own null pointer, so the list of Actor stays out.
    [Test]
    public void PropertiesOf_SuperProperties_NotIncluded()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_actor + structLayout.ChildProperties, _superProperty);
        memory.AddField(_superProperty, fieldLayout, OtherValueId);

        Address[] expected = [_property1, _property2, _property3];
        Address[] expectedActor = [_superProperty];
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Properties(reader, _pawn), Is.EqualTo(expected));
            Assert.That(Properties(reader, _actor), Is.EqualTo(expectedActor));
        }
    }

    // Calling PropertiesOf without enumerating it reads nothing, because the enumeration is lazy.
    [Test]
    public void PropertiesOf_NotEnumerated_ReadsNothing()
    {
        var memory = MemoryWithChain();

        _ = CreateReader(memory).PropertiesOf(_pawn);

        Assert.That(memory.Reads, Is.Zero);
    }

    // A 2nd enumeration of the same result reads the memory again, because the game can free a property and reuse its address.
    [Test]
    public void PropertiesOf_EnumeratedTwice_ReadsAgain()
    {
        var memory = MemoryWithChain();
        var properties = CreateReader(memory).PropertiesOf(_pawn);

        var first = properties.Select(static item => item.Unwrap()).ToList();
        memory.WritePointer(_property1 + fieldLayout.Next, Address.Zero);

        Address[] expectedFirst = [_property1, _property2, _property3];
        Address[] expectedSecond = [_property1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(expectedFirst));
            Assert.That(properties.Select(static item => item.Unwrap()), Is.EqualTo(expectedSecond));
        }
    }

    // A number of 0 returns the base name and a number above 0 adds _ and the number minus 1, as the engine prints the name.
    [TestCase(0, "IntValue")]
    [TestCase(1, "IntValue_0")]
    [TestCase(1000, "IntValue_999")]
    public void NameOf_PropertyWithNumber_ReturnsFullName(int number, string expected)
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + fieldLayout.Name + sizeof(int), number);

        Assert.That(CreateReader(memory).NameOf(_property1).Unwrap(), Is.EqualTo(expected));
    }

    // An id of a chunked pool is a block and an offset, which goes through the table as is.
    [Test]
    public void NameOf_ChunkedNameId_ReturnsText()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNamePool(_pool, Address.Zero, _block);
        memory.AddPoolEntry(_block + 0x20, wide: false, "IntValue");
        memory.AddField(_property1, fieldLayout, 0x0001_0010);
        FieldPropertyReader reader = new(memory, new ChunkedNameTable(memory, _pool), structLayout, fieldLayout, classLayout, layout);

        Assert.That(reader.NameOf(_property1).Unwrap(), Is.EqualTo("IntValue"));
    }

    // A failed read fails with the property, the address of the name id, the failed read and the name offset.
    [Test]
    public void NameOf_UnreadableProperty_FailsWithNameOffset()
    {
        var address = _property1 + fieldLayout.Name;

        Assert.That(
            CreateReader(MemoryWithNames()).NameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the property at {_property1} from {address}. No block contains {address}. "
                + $"Check FieldPropertyLayout.Name (0x{fieldLayout.Name:X})."));
    }

    // A block that ends right after the name id fails the read of the id and the number, so the cause has the address of the id.
    [Test]
    public void NameOf_NumberOutsideReadableMemory_FailsAtNameId()
    {
        var memory = MemoryWithNames();
        memory.Add(_property1, new byte[fieldLayout.Name + sizeof(int)]);
        memory.WriteInt32(_property1 + fieldLayout.Name, IntValueId);

        var address = _property1 + fieldLayout.Name;

        Assert.That(
            CreateReader(memory).NameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the property at {_property1} from {address}. No block contains {address}. "
                + $"Check FieldPropertyLayout.Name (0x{fieldLayout.Name:X})."));
    }

    // A negative number fails with the number and the name offset even though the name id is valid.
    [Test]
    public void NameOf_NegativeNumber_FailsWithNumber()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + fieldLayout.Name + sizeof(int), -1);

        var address = _property1 + fieldLayout.Name;

        Assert.That(
            CreateReader(memory).NameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the property at {_property1} from {address}. The name at {address} has the number -1, which is below 0. "
                + $"Check FieldPropertyLayout.Name (0x{fieldLayout.Name:X})."));
    }

    // A name id outside the table keeps the failure of the table in the message.
    [Test]
    public void NameOf_IdOutsideTable_WrapsTableError()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + fieldLayout.Name, 9);

        var address = _property1 + fieldLayout.Name;

        Assert.That(
            CreateReader(memory).NameOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the property at {_property1} from {address}. Name 9 is outside the array of 5 names. "
                + $"Check FieldPropertyLayout.Name (0x{fieldLayout.Name:X})."));
    }

    // The kind is the name of the field class, read at its own offset, which doesn't have an F in front.
    [Test]
    public void KindOf_IntProperty_ReturnsClassNameWithoutPrefix()
    {
        Assert.That(CreateReader(MemoryWithChain()).KindOf(_property1).Unwrap(), Is.EqualTo("IntProperty"));
    }

    // A failed read of the class pointer fails with the property and the class offset.
    [Test]
    public void KindOf_UnreadableClassPointer_FailsWithClassOffset()
    {
        var address = _property1 + fieldLayout.Class;

        Assert.That(
            CreateReader(MemoryWithNames()).KindOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the class of the property at {_property1}: reading {address} failed. No block contains {address}. "
                + $"Check FieldPropertyLayout.Class (0x{fieldLayout.Class:X})."));
    }

    // A class pointer of 0 fails with the class offset and doesn't read address 0.
    [Test]
    public void KindOf_ZeroClass_FailsWithClassNull()
    {
        var memory = MemoryWithChain();
        memory.WritePointer(_property1 + fieldLayout.Class, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CreateReader(memory).KindOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"The class pointer of the property at {_property1} is 0. "
                    + $"Check FieldPropertyLayout.Class (0x{fieldLayout.Class:X})."));
            Assert.That(memory.ReadsAt(Address.Zero + classLayout.Name), Is.Zero);
        }
    }

    // A wrong name offset of the field class fails with the class, the property and that offset.
    [Test]
    public void KindOf_WrongClassNameOffset_FailsWithFieldClassName()
    {
        var address = _class + PastBlock;
        var reader = CreateReader(MemoryWithChain(), structLayout, fieldLayout, new FieldClassLayout(PastBlock));

        Assert.That(
            reader.KindOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the class at {_class} of the property at {_property1} from {address}. No block contains {address}. "
                + $"Check FieldClassLayout.Name (0x{PastBlock:X})."));
    }

    // A negative number on the field class fails with the number and the name offset of the field class.
    [Test]
    public void KindOf_NegativeClassNameNumber_FailsWithNumber()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_class + classLayout.Name + sizeof(int), -1);

        var address = _class + classLayout.Name;

        Assert.That(
            CreateReader(memory).KindOf(_property1).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the class at {_class} of the property at {_property1} from {address}. "
                + $"The name at {address} has the number -1, which is below 0. "
                + $"Check FieldClassLayout.Name (0x{classLayout.Name:X})."));
    }

    // Each member comes from its own offset, so swapped offsets return another value.
    [Test]
    public void OffsetOf_ElementSizeOf_ArrayDimOf_ReadableProperty_ReturnValues()
    {
        var memory = MemoryWithChain();
        memory.WriteInt32(_property1 + layout.ArrayDim, 3);
        memory.WriteInt32(_property1 + layout.ElementSize, 4);
        memory.WriteInt32(_property1 + layout.Offset, 0x2A8);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.ArrayDimOf(_property1).Unwrap(), Is.EqualTo(3));
            Assert.That(reader.ElementSizeOf(_property1).Unwrap(), Is.EqualTo(4));
            Assert.That(reader.OffsetOf(_property1).Unwrap(), Is.EqualTo(0x2A8));
        }
    }

    // A failed read fails with the property, the address of the read, the failed read and the layout field to check.
    [Test]
    public void OffsetOf_ElementSizeOf_ArrayDimOf_UnreadableProperty_FailWithLayoutField()
    {
        var reader = CreateReader(MemoryWithNames());
        var arrayDim = _property1 + layout.ArrayDim;
        var elementSize = _property1 + layout.ElementSize;
        var offset = _property1 + layout.Offset;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                reader.ArrayDimOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the array size of the property at {_property1}: reading {arrayDim} failed. No block contains {arrayDim}. "
                    + $"Check PropertyLayout.ArrayDim (0x{layout.ArrayDim:X})."));
            Assert.That(
                reader.ElementSizeOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the element size of the property at {_property1}: reading {elementSize} failed. No block contains {elementSize}. "
                    + $"Check PropertyLayout.ElementSize (0x{layout.ElementSize:X})."));
            Assert.That(
                reader.OffsetOf(_property1).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the offset of the property at {_property1}: reading {offset} failed. No block contains {offset}. "
                    + $"Check PropertyLayout.Offset (0x{layout.Offset:X})."));
        }
    }

    // A null argument throws with its parameter name.
    [Test]
    public void Constructor_NullArguments_ThrowWithParamName()
    {
        FakeMemory memory = new();
        FlatNameTable names = new(memory, _nameArray, _entryLayout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => new FieldPropertyReader(null!, names, structLayout, fieldLayout, classLayout, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
            Assert.That(
                () => new FieldPropertyReader(memory, null!, structLayout, fieldLayout, classLayout, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("names"));
            Assert.That(
                () => new FieldPropertyReader(memory, names, null!, fieldLayout, classLayout, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("structLayout"));
            Assert.That(
                () => new FieldPropertyReader(memory, names, structLayout, null!, classLayout, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("fieldLayout"));
            Assert.That(
                () => new FieldPropertyReader(memory, names, structLayout, fieldLayout, null!, layout),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("classLayout"));
            Assert.That(
                () => new FieldPropertyReader(memory, names, structLayout, fieldLayout, classLayout, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("layout"));
        }
    }

    private static List<Address> Properties(FieldPropertyReader reader, Address type)
    {
        return [.. reader.PropertiesOf(type).Select(static item => item.Unwrap())];
    }

    private FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(_nameArray, _entryLayout, "None", "IntProperty", "IntValue", "OtherValue", "Tick");

        return memory;
    }

    // Lays out Pawn and Actor with empty lists and the field class IntProperty.
    private FakeMemory MemoryWithStructs()
    {
        var memory = MemoryWithNames();
        memory.Add(_pawn, new byte[BlockSpacing]);
        memory.Add(_actor, new byte[BlockSpacing]);
        memory.AddFieldClass(_class, classLayout, IntPropertyId);

        return memory;
    }

    // Gives Pawn a chain of 3 properties that ends in a null pointer.
    private FakeMemory MemoryWithChain()
    {
        var memory = MemoryWithStructs();
        memory.WritePointer(_pawn + structLayout.ChildProperties, _property1);
        memory.AddField(_property1, fieldLayout, IntValueId, _class, _property2);
        memory.AddField(_property2, fieldLayout, IntValueId, _class, _property3);
        memory.AddField(_property3, fieldLayout, IntValueId, _class);

        return memory;
    }

    private FieldPropertyReader CreateReader(FakeMemory memory)
    {
        return CreateReader(memory, structLayout);
    }

    private FieldPropertyReader CreateReader(FakeMemory memory, FieldStructLayout structs)
    {
        return CreateReader(memory, structs, fieldLayout);
    }

    private FieldPropertyReader CreateReader(FakeMemory memory, FieldStructLayout structs, FieldPropertyLayout field)
    {
        return CreateReader(memory, structs, field, classLayout);
    }

    private FieldPropertyReader CreateReader(FakeMemory memory, FieldStructLayout structs, FieldPropertyLayout field, FieldClassLayout klass)
    {
        return new FieldPropertyReader(memory, new FlatNameTable(memory, _nameArray, _entryLayout), structs, field, klass, layout);
    }
}
