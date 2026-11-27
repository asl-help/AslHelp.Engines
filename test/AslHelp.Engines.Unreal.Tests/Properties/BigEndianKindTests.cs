using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace AslHelp.Engines.Unreal.Tests.Properties;

public sealed class BigEndianKindTests
{
    private const int BitMaskOffset = 0x10;
    private const int ValueOffset = 0x20;

    private static readonly Address _data = new(0x2000);
    private static readonly Address _object = new(0x1000);
    private static readonly Address _property = new(0x3000);

    private FakeMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _memory = new FakeMemory { Layout = MemoryLayout.BigEndian32 };
        _memory.Add(_object, new byte[0x100]);
        _memory.Add(_data, new byte[0x100]);
        _memory.Add(_property, new byte[0x100]);
    }

    // A big-endian game stores each UTF-16 unit high byte first, so the text reads in that order.
    [Test]
    public void Read_BigEndianString_ReadsUtf16InGameOrder()
    {
        string text = "ü-日本";
        WriteBigEndian(_object + ValueOffset, (uint)_data.Value);
        WriteBigEndian(_object + ValueOffset + 4, (uint)(text.Length + 1));
        WriteBytes(_data, Encoding.BigEndianUnicode.GetBytes(text));

        Assert.That(Property("StrProperty").Read<string>(_memory, _object).Unwrap(), Is.EqualTo(text));
    }

    // The id and the number of a name read high byte first, so the number 3 prints as _2.
    [Test]
    public void Read_BigEndianName_ReadsIdAndNumberInGameOrder()
    {
        WriteBigEndian(_object + ValueOffset, 7);
        WriteBigEndian(_object + ValueOffset + 4, 3);

        Assert.That(Property("NameProperty").Read<string>(_memory, _object).Unwrap(), Is.EqualTo("name-7_2"));
    }

    // The 4-byte mask and bits read high byte first, so bit 2 of the value is set and bit 0 isn't.
    [TestCase(0x4u, true)]
    [TestCase(0x1u, false)]
    public void Read_BigEndianBitMask_TestsTheMaskOnTheValue(uint bitMask, bool expected)
    {
        WriteBigEndian(_property + BitMaskOffset, bitMask);
        WriteBigEndian(_object + ValueOffset, 0b110);

        Assert.That(Property("BoolProperty").Read<bool>(_memory, _object).Unwrap(), Is.EqualTo(expected));
    }

    // 3 ints right after each other read in 1 read, with each one high byte first.
    [Test]
    public void ReadMany_BigEndianInts_SwapsEachElement()
    {
        int[] expected = [100, 200, 300];
        int[] values = new int[3];
        WriteBigEndian(_data, 100);
        WriteBigEndian(_data + 4, 200);
        WriteBigEndian(_data + 8, 300);
        var access = (ValueAccess<int>)Property("IntProperty", 0x0).Bind().Unwrap();
        int reads = _memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(access.ReadMany(_memory, _data, sizeof(int), values, []).IsOk, Is.True);
            Assert.That(values, Is.EqualTo(expected));
            Assert.That(_memory.Reads - reads, Is.EqualTo(1));
        }
    }

    // The words of the allocation bits read high byte first, so slots 0, 31 and 33 count and slots 1 and 32 don't.
    [Test]
    public void ReadSlots_BigEndianBits_SwapsEachWord()
    {
        var sparse = _object + ValueOffset;
        WriteBigEndian(sparse, (uint)_data.Value);
        WriteBigEndian(sparse + 0x4, 40);
        WriteBigEndian(sparse + 0x10, 0x8000_0001);
        WriteBigEndian(sparse + 0x14, 0b10);
        SparseArray sparseArray = new(Property("SetProperty"), new SparseArrayLayout(0x0, 0x4, 0x8, 0x10, 0x30), 0);

        var slots = sparseArray.ReadSlots(_memory, _object).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(slots.Data, Is.EqualTo(_data));
            Assert.That(slots.SlotCount, Is.EqualTo(40));
            Assert.That(sparseArray.IsUsed(0), Is.True);
            Assert.That(sparseArray.IsUsed(1), Is.False);
            Assert.That(sparseArray.IsUsed(31), Is.True);
            Assert.That(sparseArray.IsUsed(32), Is.False);
            Assert.That(sparseArray.IsUsed(33), Is.True);
        }
    }

    private UnrealProperty Property(string className, int offset = ValueOffset)
    {
        Dictionary<string, int> offsets = new(StringComparer.Ordinal)
        {
            ["UBoolProperty.BitMask"] = BitMaskOffset,
            ["FName.Index"] = 0x0,
            ["FName.Number"] = 0x4,
            ["FScriptArray.Data"] = 0x0,
            ["FScriptArray.ArrayNum"] = 0x4,
        };

        KindProfile profile = new(
            "U",
            "Index",
            bitMaskBools: true,
            (type, member) => offsets.TryGetValue($"{type}.{member}", out int offset) ? offset : null);
        var kindLayouts = KindLayouts.Read(profile);

        // Pass only the memory, the name table and the layouts, because a value read of these kinds doesn't use the other readers.
        ClassReader classReader = new(_memory, new NumberedNames(), null!, null!, null!, null!, kindLayouts);

        return new UnrealProperty(classReader, _property, "Value", className, offset, 0x8);
    }

    private void WriteBigEndian(Address address, uint value)
    {
        byte[] bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        WriteBytes(address, bytes);
    }

    private void WriteBytes(Address address, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            _memory.Write(address + i, bytes[i]);
        }
    }
}

file sealed class NumberedNames : INameTable
{
    public Result<string> Read(int id)
    {
        return $"name-{id}";
    }
}
