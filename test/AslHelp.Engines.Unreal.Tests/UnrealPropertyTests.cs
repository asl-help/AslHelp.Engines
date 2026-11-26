using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPropertyTests(
    MemoryLayout memoryLayout,
    Address start)
{
    private const int ObjectSize = 0x100;
    private const int ValueOffset = 0x2C;

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private readonly Address _obj = start + 0x1000;
    private readonly Address _target = start + 0x2000;

    // IntProperty reaches VisitInt32 with the same property.
    [Test]
    public void Accept_IntProperty_VisitsInt32()
    {
        var property = Property("Hp", "IntProperty");

        var (kind, visited) = property.Accept(new KindVisitor());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kind, Is.EqualTo(nameof(IPropertyVisitor<>.VisitInt32)));
            Assert.That(visited, Is.SameAs(property));
        }
    }

    // A ClassProperty points at a class object and 5.0 to 5.3 print TObjectPtr properties with Ptr in the name,
    // so all 4 read as object pointers.
    [TestCase("ClassProperty")]
    [TestCase("ClassPtrProperty")]
    [TestCase("ObjectProperty")]
    [TestCase("ObjectPtrProperty")]
    public void Accept_ObjectPropertyNames_VisitObject(string className)
    {
        var property = Property("FixtureData", className);

        var (kind, visited) = property.Accept(new KindVisitor());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kind, Is.EqualTo(nameof(IPropertyVisitor<>.VisitObject)));
            Assert.That(visited, Is.SameAs(property));
        }
    }

    // A kind the library doesn't read goes to VisitUnsupported with the class as the engine prints it. Accept compares ordinally.
    [TestCase("FloatProperty")]
    [TestCase("Int64Property")]
    [TestCase("WeakObjectProperty")]
    [TestCase("intproperty")]
    public void Accept_OtherClassNames_VisitUnsupported(string className)
    {
        var (kind, visited) = Property("Speed", className).Accept(new KindVisitor());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kind, Is.EqualTo(nameof(IPropertyVisitor<>.VisitUnsupported)));
            Assert.That(visited.ClassName, Is.EqualTo(className));
        }
    }

    // The value is 4 signed bytes at the object plus the offset.
    [Test]
    public void ReadInt32_IntProperty_ReturnsTheSignedValue()
    {
        var memory = MemoryWithObject();
        memory.WriteInt32(_obj + ValueOffset, -5);

        Assert.That(Property("Hp", "IntProperty").ReadInt32(memory, _obj).Unwrap(), Is.EqualTo(-5));
    }

    // The pointer has the size of the memory's pointers, so an address above 4 GB on x64 and above the signed range on x86 comes back whole.
    [Test]
    public void ReadObject_ObjectProperty_ReturnsThePointer()
    {
        var memory = MemoryWithObject();
        memory.WritePointer(_obj + ValueOffset, _target);

        Assert.That(Property("FixtureData", "ObjectProperty").ReadObject(memory, _obj).Unwrap(), Is.EqualTo(_target));
    }

    // A stored pointer of 0 is a value, because a game leaves an object property empty until it sets it.
    [Test]
    public void ReadObject_NullPointer_ReturnsZero()
    {
        var memory = MemoryWithObject();

        var target = Property("FixtureData", "ObjectProperty").ReadObject(memory, _obj);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Unwrap(), Is.EqualTo(Address.Zero));
            Assert.That(memory.ReadsAt(_obj + ValueOffset), Is.EqualTo(1));
        }
    }

    // An object of 0 fails without a read.
    [Test]
    public void ReadInt32_NullObject_FailsWithObjectNull()
    {
        var memory = MemoryWithObject();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Property("Hp", "IntProperty").ReadInt32(memory, Address.Zero).UnwrapErr().Message, Is.EqualTo("The object pointer is 0."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // An object of 0 fails without a read.
    [Test]
    public void ReadObject_NullObject_FailsWithObjectNull()
    {
        var memory = MemoryWithObject();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Property("FixtureData", "ObjectProperty").ReadObject(memory, Address.Zero).UnwrapErr().Message,
                Is.EqualTo("The object pointer is 0."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A value whose last byte is past the object fails with the property, the object and the read without throwing.
    [Test]
    public void ReadInt32_ValuePastReadableMemory_FailsWithTheAddress()
    {
        var address = _obj + (ObjectSize - 2);

        Assert.That(
            new UnrealProperty("Hp", "IntProperty", ObjectSize - 2, sizeof(int)).ReadInt32(MemoryWithObject(), _obj).UnwrapErr().Message,
            Is.EqualTo($"Can't read Hp of the object at {_obj}: reading {address} failed. No block contains {address}."));
    }

    // An object in unreadable memory fails the pointer read with the same values.
    [Test]
    public void ReadObject_UnreadableObject_FailsWithTheAddress()
    {
        var address = _target + ValueOffset;

        Assert.That(
            Property("FixtureData", "ObjectProperty").ReadObject(MemoryWithObject(), _target).UnwrapErr().Message,
            Is.EqualTo($"Can't read FixtureData of the object at {_target}: reading {address} failed. No block contains {address}."));
    }

    // An object property read as an int fails before the read.
    [Test]
    public void ReadInt32_ObjectProperty_FailsWithWrongKind()
    {
        var memory = MemoryWithObject();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Property("FixtureData", "ObjectProperty").ReadInt32(memory, _obj).UnwrapErr().Message,
                Is.EqualTo("FixtureData is ObjectProperty, not IntProperty."));
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // An int property read as a pointer fails with both kinds.
    [Test]
    public void ReadObject_IntProperty_FailsWithWrongKind()
    {
        Assert.That(
            Property("Hp", "IntProperty").ReadObject(MemoryWithObject(), _obj).UnwrapErr().Message,
            Is.EqualTo("Hp is IntProperty, not ObjectProperty."));
    }

    // A kind the library doesn't read fails with the class the engine prints.
    [Test]
    public void ReadInt32_UnsupportedProperty_FailsWithItsClassName()
    {
        Assert.That(
            Property("Speed", "FloatProperty").ReadInt32(MemoryWithObject(), _obj).UnwrapErr().Message,
            Is.EqualTo("Speed is FloatProperty, not IntProperty."));
    }

    private static UnrealProperty Property(string name, string className)
    {
        return new UnrealProperty(name, className, ValueOffset, sizeof(int));
    }

    private FakeMemory MemoryWithObject()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_obj, new byte[ObjectSize]);

        return memory;
    }
}
