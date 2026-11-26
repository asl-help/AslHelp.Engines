using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPropertyEdgeTests(
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

    private Address TopOfAddressRange => new(memoryLayout.PointerSize == sizeof(ulong) ? ulong.MaxValue : uint.MaxValue);

    // ObjectPtrProperty is an object pointer on 5.0 to 5.3, so ReadObject reads it.
    [Test]
    public void ReadObject_ObjectPtrProperty_ReturnsThePointer()
    {
        var memory = MemoryWithObject();
        memory.WritePointer(_obj + ValueOffset, _target);

        Assert.That(Property("FixtureData", "ObjectPtrProperty").ReadObject(memory, _obj).Unwrap(), Is.EqualTo(_target));
    }

    // An ObjectPtrProperty read as an int fails with the class the engine prints and not ObjectProperty.
    [Test]
    public void ReadInt32_ObjectPtrProperty_FailsWithItsClassName()
    {
        Assert.That(
            Property("FixtureData", "ObjectPtrProperty").ReadInt32(MemoryWithObject(), _obj).UnwrapErr().Message,
            Is.EqualTo("FixtureData is ObjectPtrProperty, not IntProperty."));
    }

    // A garbage object pointer at the top of the address range wraps past the end and fails the read without throwing.
    [Test]
    public void ReadInt32_ObjectAtTopOfAddressRange_Fails()
    {
        var top = TopOfAddressRange;

        Assert.That(
            Property("Hp", "IntProperty").ReadInt32(MemoryWithObject(), top).UnwrapErr().Message,
            Does.StartWith($"Can't read Hp of the object at {top}: "));
    }

    // A garbage object pointer at the top of the address range wraps past the end and fails the read without throwing.
    [Test]
    public void ReadObject_ObjectAtTopOfAddressRange_Fails()
    {
        var top = TopOfAddressRange;

        Assert.That(
            Property("FixtureData", "ObjectProperty").ReadObject(MemoryWithObject(), top).UnwrapErr().Message,
            Does.StartWith($"Can't read FixtureData of the object at {top}: "));
    }

    // A memory without a pointer size refuses the pointer read, which fails with the property and doesn't throw.
    [Test]
    public void ReadObject_MemoryWithoutPointerSize_Fails()
    {
        FakeMemory memory = new() { Layout = default };
        memory.Add(_obj, new byte[ObjectSize]);

        Assert.That(
            Property("FixtureData", "ObjectProperty").ReadObject(memory, _obj).UnwrapErr().Message,
            Does.StartWith($"Can't read FixtureData of the object at {_obj}: ").And.Contain("pointer size of 0 bytes"));
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
