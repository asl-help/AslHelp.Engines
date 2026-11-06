using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ObjectReaderTests(
    MemoryLayout memoryLayout,
    Address start,
    ObjectLayout layout)
{
    private const int CoreId = 1;
    private const int EngineId = 2;
    private const int UnknownOffset = -1;

    private static readonly NameEntryLayout _entryLayout = new(0, 12);

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), new ObjectLayout(0x04, 0x10, 0x18, 0x24)),
        new(MemoryLayout.X86, new Address(0x8000_0000), new ObjectLayout(0x08, 0x2C, 0x34, 0x14)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), new ObjectLayout(0x0C, 0x10, 0x18, 0x28)),
    ];

    private readonly Address _class = start + 0x7000;
    private readonly Address _nameArray = start;
    private readonly Address _object = start + 0x5000;
    private readonly Address _otherObject = start + 0x5100;
    private readonly Address _outer = start + 0x6000;

    // A number of 0 returns the text of the name id.
    [Test]
    public void NameOf_ObjectWithNumberZero_ReturnsBaseName()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);

        Assert.That(CreateReader(memory).NameOf(_object).Unwrap(), Is.EqualTo("Core"));
    }

    // A positive number returns the base name with no suffix for the number.
    [Test]
    public void NameOf_ObjectWithNumber_ReturnsBaseNameWithoutSuffix()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, number: 5);

        Assert.That(CreateReader(memory).NameOf(_object).Unwrap(), Is.EqualTo("Core"));
    }

    // The largest number is still not negative, so the base name comes back.
    [Test]
    public void NameOf_LargestNumber_ReturnsBaseName()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, number: int.MaxValue);

        Assert.That(CreateReader(memory).NameOf(_object).Unwrap(), Is.EqualTo("Core"));
    }

    // A negative number fails with the number and the name offset even though the name id is valid.
    [Test]
    public void NameOf_NegativeNumber_FailsWithNumber()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, number: -1);

        Assert.That(
            CreateReader(memory).NameOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"The object at {_object} has the name number -1, which is below 0. "
                + $"Check ObjectLayout.Name (0x{layout.Name:X}) against this engine version."));
    }

    // The smallest number reads as signed, so it fails.
    [Test]
    public void NameOf_SmallestNumber_FailsWithNumber()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, number: int.MinValue);

        Assert.That(CreateReader(memory).NameOf(_object).UnwrapErr().Message, Does.Contain($"name number {int.MinValue}"));
    }

    // A number that turns negative fails even though the table has the name in its cache.
    [Test]
    public void NameOf_NumberTurnsNegativeAfterNameCached_Fails()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);
        var reader = CreateReader(memory);

        string first = reader.NameOf(_object).Unwrap();
        memory.WriteInt32(_object + layout.Name + sizeof(int), -1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("Core"));
            Assert.That(reader.NameOf(_object).IsErr, Is.True);
        }
    }

    // Address 0 fails with no memory read, even when address 0 has a valid object.
    [Test]
    public void NameOf_AddressZero_FailsWithNoRead()
    {
        var memory = MemoryWithNames();
        memory.AddObject(Address.Zero, layout, CoreId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CreateReader(memory).NameOf(Address.Zero).UnwrapErr().Message, Is.EqualTo("The object pointer is 0."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // An unreadable object fails with the object and the name offset with no read of the name array.
    [Test]
    public void NameOf_UnreadableObject_FailsWithNameOffset()
    {
        var memory = MemoryWithNames();
        var address = _object + layout.Name;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CreateReader(memory).NameOf(_object).UnwrapErr().Message,
                Is.EqualTo(
                    $"Can't read the name of the object at {_object}: reading {address} failed. No block contains {address}. "
                    + $"Check ObjectLayout.Name (0x{layout.Name:X}) against this engine version."));
            Assert.That(memory.ReadsAt(_nameArray), Is.Zero);
        }
    }

    // A block that ends right after the name id fails the number read, so the message has the address of the number.
    [Test]
    public void NameOf_NumberOutsideReadableMemory_FailsWithNameOffset()
    {
        var memory = MemoryWithNames();
        memory.Add(_object, new byte[layout.Name + sizeof(int)]);
        memory.WriteInt32(_object + layout.Name, CoreId);

        var address = _object + layout.Name + sizeof(int);

        Assert.That(
            CreateReader(memory).NameOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_object}: reading {address} failed. No block contains {address}. "
                + $"Check ObjectLayout.Name (0x{layout.Name:X}) against this engine version."));
    }

    // An object at the end of the address range wraps around to unreadable memory and does not throw, although the build turns on overflow checks.
    [Test]
    public void NameOf_ObjectAtEndOfAddressRange_Fails()
    {
        Assert.That(CreateReader(MemoryWithNames()).NameOf(new Address(ulong.MaxValue)).IsErr, Is.True);
    }

    // The 4 bytes to the number go on the address and not on the offset, so the largest offset does not overflow a 4-byte sum.
    [Test]
    public void NameOf_NameOffsetAtIntLimit_ReturnsBaseName()
    {
        var name = _object + int.MaxValue;
        var memory = MemoryWithNames();
        memory.Add(name, new byte[2 * sizeof(int)]);
        memory.WriteInt32(name, CoreId);
        var reader = CreateReader(memory, layout with { Name = int.MaxValue });

        Assert.That(reader.NameOf(_object).Unwrap(), Is.EqualTo("Core"));
    }

    // A name id outside the table fails with the object, the id and the failure of the table.
    [TestCase(-1, "Name -1 is below 0.")]
    [TestCase(3, "Name 3 is outside the array of 3 names.")]
    [TestCase(int.MaxValue, "Name 2147483647 is outside the array of 3 names.")]
    [TestCase(int.MinValue, "Name -2147483648 is below 0.")]
    public void NameOf_NameIdOutsideTable_WrapsTableError(int id, string tableError)
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, id);

        Assert.That(
            CreateReader(memory).NameOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the name of the object at {_object} with the name id {id}. {tableError} "
                + $"Check ObjectLayout.Name (0x{layout.Name:X}) against this engine version."));
    }

    // A changed name id returns the new name, because the game can reuse the address of a freed object.
    [Test]
    public void NameOf_SameObjectAfterMemoryChanged_ReturnsNewName()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);
        var reader = CreateReader(memory);

        string first = reader.NameOf(_object).Unwrap();
        memory.WriteInt32(_object + layout.Name, EngineId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo("Core"));
            Assert.That(reader.NameOf(_object).Unwrap(), Is.EqualTo("Engine"));
        }
    }

    // 2 objects with 1 name id read both objects and read the entry once, because only the table caches.
    [Test]
    public void NameOf_TwoObjectsWithSameId_ReadsEntryOnce()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);
        memory.AddObject(_otherObject, layout, CoreId);
        var reader = CreateReader(memory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.NameOf(_object).Unwrap(), Is.EqualTo("Core"));
            Assert.That(reader.NameOf(_otherObject).Unwrap(), Is.EqualTo("Core"));
            Assert.That(memory.ReadsAt(_otherObject + layout.Name), Is.EqualTo(1));
            Assert.That(memory.ReadsAt(_nameArray), Is.EqualTo(1));
        }
    }

    // NameOf reads only the name offset, so the reader works before the measurer has found the other offsets.
    [Test]
    public void NameOf_OnlyNameOffsetKnown_ReturnsBaseName()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);
        var reader = CreateReader(memory, layout with { Index = UnknownOffset, Outer = UnknownOffset, Class = UnknownOffset });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.NameOf(_object).Unwrap(), Is.EqualTo("Core"));
            Assert.That(memory.ReadsAt(_object + UnknownOffset), Is.Zero);
        }
    }

    // ClassOf returns the pointer at the class offset of the layout.
    [Test]
    public void ClassOf_ReadableObject_ReturnsPointer()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, outer: _outer, klass: _class);

        Assert.That(CreateReader(memory).ClassOf(_object).Unwrap(), Is.EqualTo(_class));
    }

    // A class pointer of 0 is a success, so it stays apart from memory that does not read.
    [Test]
    public void ClassOf_ZeroPointer_ReturnsOkZero()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);

        Assert.That(CreateReader(memory).ClassOf(_object), Is.EqualTo(Result.Ok(Address.Zero)));
    }

    // A failed read fails with the object, the address of the read, the class offset and the failed read.
    [Test]
    public void ClassOf_UnreadableObject_FailsWithClassOffset()
    {
        var address = _object + layout.Class;

        Assert.That(
            CreateReader(MemoryWithNames()).ClassOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the class of the object at {_object}: reading {address} failed. No block contains {address}. "
                + $"Check ObjectLayout.Class (0x{layout.Class:X}) against this engine version."));
    }

    // A changed class returns the new pointer, because the game can reuse the address of a freed object.
    [Test]
    public void ClassOf_SameObjectAfterMemoryChanged_ReturnsNewPointer()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, klass: _class);
        var reader = CreateReader(memory);

        var first = reader.ClassOf(_object).Unwrap();
        memory.WritePointer(_object + layout.Class, _outer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(_class));
            Assert.That(reader.ClassOf(_object).Unwrap(), Is.EqualTo(_outer));
        }
    }

    // ClassOf makes 1 read at its own offset, so the reader works before the measurer has found the name array.
    [Test]
    public void ClassOf_OnlyClassOffsetKnown_ReturnsPointerWithOneRead()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddObject(_object, layout, CoreId, klass: _class);
        var reader = CreateReader(memory, layout with { Index = UnknownOffset, Outer = UnknownOffset, Name = UnknownOffset });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.ClassOf(_object).Unwrap(), Is.EqualTo(_class));
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
    }

    // OuterOf returns the pointer at the outer offset of the layout.
    [Test]
    public void OuterOf_ReadableObject_ReturnsPointer()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, outer: _outer, klass: _class);

        Assert.That(CreateReader(memory).OuterOf(_object).Unwrap(), Is.EqualTo(_outer));
    }

    // An outer pointer of 0 is a success, because a package does not have an outer object.
    [Test]
    public void OuterOf_ZeroPointer_ReturnsOkZero()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId);

        Assert.That(CreateReader(memory).OuterOf(_object), Is.EqualTo(Result.Ok(Address.Zero)));
    }

    // A failed read fails with the object, the address of the read, the outer offset and the failed read.
    [Test]
    public void OuterOf_UnreadableObject_FailsWithOuterOffset()
    {
        var address = _object + layout.Outer;

        Assert.That(
            CreateReader(MemoryWithNames()).OuterOf(_object).UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read the outer object of the object at {_object}: reading {address} failed. No block contains {address}. "
                + $"Check ObjectLayout.Outer (0x{layout.Outer:X}) against this engine version."));
    }

    // A changed outer object returns the new pointer, because the game can reuse the address of a freed object.
    [Test]
    public void OuterOf_SameObjectAfterMemoryChanged_ReturnsNewPointer()
    {
        var memory = MemoryWithNames();
        memory.AddObject(_object, layout, CoreId, outer: _outer);
        var reader = CreateReader(memory);

        var first = reader.OuterOf(_object).Unwrap();
        memory.WritePointer(_object + layout.Outer, _class);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(_outer));
            Assert.That(reader.OuterOf(_object).Unwrap(), Is.EqualTo(_class));
        }
    }

    // OuterOf makes 1 read at its own offset, so the reader works before the measurer has found the name array.
    [Test]
    public void OuterOf_OnlyOuterOffsetKnown_ReturnsPointerWithOneRead()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddObject(_object, layout, CoreId, outer: _outer);
        var reader = CreateReader(memory, layout with { Index = UnknownOffset, Name = UnknownOffset, Class = UnknownOffset });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.OuterOf(_object).Unwrap(), Is.EqualTo(_outer));
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
    }

    // A null memory throws with its parameter name.
    [Test]
    public void Constructor_NullMemory_ThrowsArgumentNullException()
    {
        FlatNameTable names = new(new FakeMemory(), _nameArray, _entryLayout);

        Assert.That(
            () => new ObjectReader(null!, names, layout),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("memory"));
    }

    // A null table throws with its parameter name.
    [Test]
    public void Constructor_NullNames_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new ObjectReader(new FakeMemory(), null!, layout),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("names"));
    }

    // A null layout throws with its parameter name.
    [Test]
    public void Constructor_NullLayout_ThrowsArgumentNullException()
    {
        FlatNameTable names = new(new FakeMemory(), _nameArray, _entryLayout);

        Assert.That(
            () => new ObjectReader(new FakeMemory(), names, null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("layout"));
    }

    private FakeMemory MemoryWithNames()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.AddNames(_nameArray, _entryLayout, "None", "Core", "Engine");

        return memory;
    }

    private ObjectReader CreateReader(FakeMemory memory)
    {
        return CreateReader(memory, layout);
    }

    private ObjectReader CreateReader(FakeMemory memory, ObjectLayout objectLayout)
    {
        return new ObjectReader(memory, new FlatNameTable(memory, _nameArray, _entryLayout), objectLayout);
    }
}
