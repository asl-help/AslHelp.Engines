using AslHelp.Engines.Unreal.Properties;
using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPropertyTests(
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

    private Address TopOfAddressRange => new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // Each value kind reaches VisitValue with the type a read gives and the visit doesn't read memory.
    [TestCase("Hp", "VisitValue<Int32>")]
    [TestCase("Speed", "VisitValue<Single>")]
    [TestCase("Int8Value", "VisitValue<SByte>")]
    [TestCase("UInt64Value", "VisitValue<UInt64>")]
    [TestCase("ByteValue", "VisitValue<Byte>")]
    [TestCase("BoolValue", "VisitValue<Boolean>")]
    [TestCase("StringValue", "VisitValue<String>")]
    [TestCase("NameValue", "VisitValue<String>")]
    [TestCase("Coord", "VisitStruct")]
    [TestCase("Kind", "VisitEnum")]
    [TestCase("TextValue", "VisitUnsupported")]
    public void Accept_DataProperty_VisitsTheMethodOfItsKind(string name, string expected)
    {
        var property = DataProperty(name);
        int reads = _fakeGame.Memory.Reads;

        var (kind, visited) = property.Accept(new KindVisitor());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kind, Is.EqualTo(expected));
            Assert.That(visited, Is.SameAs(property));
            Assert.That(_fakeGame.Memory.Reads, Is.EqualTo(reads));
        }
    }

    // A visitor that only overrides the abstract methods gets a struct and an enum as unsupported.
    [TestCase("Coord")]
    [TestCase("Kind")]
    public void Accept_VisitorWithoutShapeOverrides_GetsUnsupported(string name)
    {
        Assert.That(DataProperty(name).Accept(new ValueOnlyVisitor()), Is.EqualTo("unsupported"));
    }

    // Read<int> and Read<Address> read an int and the pointer of an object property.
    [Test]
    public void Read_IntAndObjectOnGame_ReadsTheValues()
    {
        var data = _game.Read<Address>(_fakeGame.GameMode, _game.ClassOf(_fakeGame.GameMode).Unwrap().FindProperty("Data").Unwrap());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(_game.Read<int>(_fakeGame.Data, DataProperty("Hp")).Unwrap(), Is.EqualTo(FakeGame.Hp));
        }
    }

    // A stored pointer of 0 is a value, because a game leaves an object property empty until it sets it.
    [Test]
    public void Read_NullPointer_ReturnsZero()
    {
        var dataProperty = _game.ClassOf(_fakeGame.GameMode).Unwrap().FindProperty("Data").Unwrap();
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(_game.Read<Address>(_fakeGame.GameMode, dataProperty).Unwrap(), Is.EqualTo(Address.Zero));
    }

    // An object of 0 fails without a read.
    [Test]
    public void Read_NullObject_FailsWithObjectNull()
    {
        var hp = DataProperty("Hp");
        int reads = _fakeGame.Memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.Read<int>(Address.Zero, hp).UnwrapErr().Message, Is.EqualTo("The object pointer is 0."));
            Assert.That(_fakeGame.Memory.Reads, Is.EqualTo(reads));
        }
    }

    // A float read as an int fails with the property, its class, the type it reads and the type of the read, before a read of the value.
    [Test]
    public void Read_ValueOfAnotherType_FailsWithPropertyClassAndBothTypes()
    {
        var speed = DataProperty("Speed");
        int reads = _fakeGame.Memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _game.Read<int>(_fakeGame.Data, speed).UnwrapErr().Message,
                Is.EqualTo("Speed is FloatProperty, which reads as Single, not Int32."));
            Assert.That(_fakeGame.Memory.Reads, Is.EqualTo(reads));
        }
    }

    // A kind the library doesn't read fails with the class the engine prints.
    [Test]
    public void Read_UnsupportedKind_FailsWithItsClassName()
    {
        Assert.That(
            _game.Read<int>(_fakeGame.Data, DataProperty("TextValue")).UnwrapErr().Message,
            Is.EqualTo("TextValue is TextProperty, which the library doesn't read."));
    }

    // A value whose last byte is past the object fails with the property, the object and the read without throwing.
    [Test]
    public void Read_ValuePastReadableMemory_FailsWithTheAddress()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.PropertyAddress("Int64Value") + FakeGame.PropertyOffsetOffset, 0xFC);
        var address = _fakeGame.Data + 0xFC;

        Assert.That(
            _game.Read<long>(_fakeGame.Data, DataProperty("Int64Value")).UnwrapErr().Message,
            Is.EqualTo($"Can't read Int64Value of the object at {_fakeGame.Data}: reading {address} failed. No block contains {address}."));
    }

    // A garbage object pointer at the top of the address range wraps past the end and fails the read of each kind without throwing.
    [Test]
    public void Read_ObjectAtTopOfAddressRange_FailsForEachKind()
    {
        var top = TopOfAddressRange;
        string objectText = $"of the object at {top}: ";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.Read<int>(top, DataProperty("Hp")).UnwrapErr().Message, Does.StartWith($"Can't read Hp {objectText}"));
            Assert.That(_game.Read<bool>(top, DataProperty("BoolValue")).UnwrapErr().Message, Does.StartWith($"Can't read BoolValue {objectText}"));
            Assert.That(_game.Read<string>(top, DataProperty("NameValue")).UnwrapErr().Message, Does.StartWith($"Can't read NameValue {objectText}"));
            Assert.That(
                _game.Read<string>(top, DataProperty("StringValue")).UnwrapErr().Message,
                Does.StartWith($"Can't read StringValue {objectText}"));
        }
    }

    private UnrealProperty DataProperty(string name)
    {
        return _game.ClassOf(_fakeGame.Data).Unwrap().FindProperty(name).Unwrap();
    }
}

file sealed class ValueOnlyVisitor : PropertyVisitor<string>
{
    public override string VisitUnsupported(UnrealProperty prop)
    {
        return "unsupported";
    }

    public override string VisitValue<T>(UnrealProperty prop)
    {
        return "value";
    }
}
