using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Finds the offsets that the value kinds read, through the fields of the fixture class and its spawned object.
/// </summary>
/// <remarks>
///     Runs after <see cref="UdkFixtureCheck"/>, because each search reads the fields through Children, Next and Offset
///     and checks the values the fixture game sets.
/// </remarks>
internal sealed class UdkValueMembers
{
    /// <summary>
    ///     Represents the report key of the number of elements of a property.
    /// </summary>
    public const string ArrayDim = "UProperty.ArrayDim";

    /// <summary>
    ///     Represents the report key of the count of a dynamic array.
    /// </summary>
    public const string ArrayNum = "FScriptArray.ArrayNum";

    /// <summary>
    ///     Represents the report key of the mask of a bool property.
    /// </summary>
    public const string BitMask = "UBoolProperty.BitMask";

    /// <summary>
    ///     Represents the report key of the pointer to the data of a dynamic array.
    /// </summary>
    public const string Data = "FScriptArray.Data";

    /// <summary>
    ///     Represents the report key of the pointer to the property of the elements of an array property.
    /// </summary>
    public const string Inner = "UArrayProperty.Inner";

    /// <summary>
    ///     Represents the report key of the name id in a name.
    /// </summary>
    public const string NameIndex = "FName.Index";

    /// <summary>
    ///     Represents the report key of the number in a name.
    /// </summary>
    public const string NameNumber = "FName.Number";

    /// <summary>
    ///     Represents the report key of the pointer to the struct type of a struct property.
    /// </summary>
    public const string Struct = "UStructProperty.Struct";

    private const string CoordType = "FixtureCoord";
    private const string DefaultObjectPrefix = "Default__";
    private const int LargestArrayDim = 0x1000;
    private const int LargestOffset = 0x100;
    private const string NameText = "fixture-name";
    private const string PropertyClass = "Property";
    private const string StringText = "hello-fixture";

    // List the bools of the fixture with the values PostBeginPlay sets.
    private static readonly (string Name, bool Value)[] _bools = [("BoolValue", true), ("bFirst", true), ("bSecond", false), ("bThird", true)];

    private readonly Dictionary<string, Address> _fields;
    private readonly UdkFixture _fixture;
    private readonly UdkMeasuredObjects _measured;
    private readonly IMemoryReader _memory;
    private readonly UdkNameArray _names;
    private readonly Dictionary<string, int> _offsets;
    private readonly int _pointerSize;
    private readonly Dictionary<string, string> _unmeasured;

    private UdkValueMembers(
        IMemoryReader memory,
        UdkNameArray names,
        UdkMeasuredObjects measured,
        UdkFixture fixture,
        Dictionary<string, int> offsets,
        Dictionary<string, string> unmeasured)
    {
        _memory = memory;
        _names = names;
        _measured = measured;
        _fixture = fixture;
        _offsets = offsets;
        _unmeasured = unmeasured;
        _pointerSize = memory.Layout.PointerSize;
        _fields = ReadFields();
    }

    /// <summary>
    ///     Finds the offset of every value member and adds it to <paramref name="offsets"/> or adds the reason to <paramref name="unmeasured"/>.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="names">The name array.</param>
    /// <param name="measured">The objects found and the reader with the offsets found.</param>
    /// <param name="fixture">The fixture class and its spawned object.</param>
    /// <param name="offsets">The offset of each member, by member, with Children, Next, Offset and ElementSize.</param>
    /// <param name="unmeasured">The reason for each member that was not found, by member.</param>
    public static void Measure(
        IMemoryReader memory,
        UdkNameArray names,
        UdkMeasuredObjects measured,
        UdkFixture fixture,
        Dictionary<string, int> offsets,
        Dictionary<string, string> unmeasured)
    {
        UdkValueMembers members = new(memory, names, measured, fixture, offsets, unmeasured);

        members.Record(BitMask, members.FindBitMask(), UdkSR.UnmeasuredBitMask);
        members.Record(Struct, members.FindStruct(), UdkSR.UnmeasuredStruct);
        members.Record(Inner, members.FindInner(), UdkSR.UnmeasuredInner);

        if (!offsets.ContainsKey(UdkMembers.ElementSize))
        {
            string needs = UdkSR.FormatUnmeasuredNeeds(UdkMembers.ElementSize);

            foreach (string member in (string[])[Data, ArrayNum, NameIndex, NameNumber, ArrayDim])
            {
                unmeasured[member] = needs;
            }

            return;
        }

        members.FindArrayMembers();
        members.FindNameMembers();

        // Report ArrayDim as needing the size or the super chain, because it checks each property against the size of its struct
        // and finds the properties by their super chain.
        foreach (string member in (string[])[UdkMembers.PropertySize, UdkMembers.SuperField])
        {
            if (!offsets.ContainsKey(member))
            {
                unmeasured[ArrayDim] = UdkSR.FormatUnmeasuredNeeds(member);

                return;
            }
        }

        members.FindArrayDim();
    }

    private Dictionary<string, Address> ReadFields()
    {
        StructReader structReader = new(
            _memory,
            _measured.Reader,
            new StructLayout(-1, _offsets[UdkMembers.Children]),
            new ChildLayout(_offsets[UdkMembers.Next]));
        Dictionary<string, Address> fields = [with(StringComparer.Ordinal)];

        foreach (var child in structReader.ChildrenOf(_fixture.Class))
        {
            if (!child.TryUnwrap(out var field))
            {
                break;
            }

            if (_measured.Reader.NameOf(field).TryUnwrap(out string? name))
            {
                fields[name] = field;
            }
        }

        return fields;
    }

    // Find the 4-byte mask that is 1 bit in each bool and reads the value of each bool in the spawned object.
    private int FindBitMask()
    {
        if (!_bools.All(entry => _fields.ContainsKey(entry.Name)))
        {
            return -1;
        }

        return OffsetsBy(sizeof(uint)).FirstOrDefault(
            at => _bools.All(
                entry =>
                {
                    var field = _fields[entry.Name];
                    uint mask = (uint)ReadInt(field, at);
                    uint bits = (uint)ReadInt(_fixture.Instance, ValueOffsetOf(field));

                    return mask != 0 && (mask & (mask - 1)) == 0 && (bits & mask) != 0 == entry.Value;
                }),
            -1);
    }

    // Find the pointer of the struct property Coord that points at the struct type FixtureCoord.
    private int FindStruct()
    {
        if (!_fields.TryGetValue("Coord", out var coord))
        {
            return -1;
        }

        return OffsetsBy(_pointerSize).FirstOrDefault(
            at => string.Equals(_measured.Reader.NameOf(ReadPointer(coord, at)).UnwrapOrDefault(), CoordType, StringComparison.Ordinal),
            -1);
    }

    // Find the pointer of the array properties IntArray and StringArray that points at a property of the class IntProperty and StrProperty.
    private int FindInner()
    {
        if (!_fields.TryGetValue("IntArray", out var intArray) || !_fields.TryGetValue("StringArray", out var stringArray))
        {
            return -1;
        }

        return OffsetsBy(_pointerSize).FirstOrDefault(
            at => ClassNameIs(ReadPointer(intArray, at), "IntProperty") && ClassNameIs(ReadPointer(stringArray, at), "StrProperty"),
            -1);
    }

    private bool ClassNameIs(Address obj, string name)
    {
        return obj != Address.Zero
            && _measured.Reader.ClassOf(obj).TryUnwrap(out var klass)
            && string.Equals(_measured.Reader.NameOf(klass).UnwrapOrDefault(), name, StringComparison.Ordinal);
    }

    // Find the data pointer and the count of StringValue in the spawned object, the count with the terminator.
    private void FindArrayMembers()
    {
        int data = -1;
        int count = -1;

        if (_fields.TryGetValue("StringValue", out var stringValue))
        {
            var value = _fixture.Instance + ValueOffsetOf(stringValue);
            int elementSize = ReadInt(stringValue, _offsets[UdkMembers.ElementSize]);
            byte[] text = Encoding.Unicode.GetBytes(StringText + '\0');
            byte[] read = new byte[text.Length];

            data = Within(elementSize, _pointerSize).FirstOrDefault(
                at => _memory.Read(ReadPointer(value, at), read).IsOk && read.AsSpan().SequenceEqual(text),
                -1);
            count = Within(elementSize, sizeof(int)).FirstOrDefault(at => at != data && ReadInt(value, at) == StringText.Length + 1, -1);
        }

        Record(Data, data, UdkSR.UnmeasuredString);
        Record(ArrayNum, data == -1 ? -1 : count, UdkSR.UnmeasuredString);
    }

    // Find the id of NameValue and the number, which is 0 in NameValue and above 0 in the name of the spawned object.
    private void FindNameMembers()
    {
        int index = -1;
        int number = -1;

        if (_fields.TryGetValue("NameValue", out var nameValue))
        {
            var value = _fixture.Instance + ValueOffsetOf(nameValue);
            var objectName = _fixture.Instance + _offsets[UdkMembers.ObjectName];
            int elementSize = ReadInt(nameValue, _offsets[UdkMembers.ElementSize]);

            index = Within(elementSize, sizeof(int)).FirstOrDefault(
                at => string.Equals(_names.Table.Read(ReadInt(value, at)).UnwrapOrDefault(), NameText, StringComparison.Ordinal),
                -1);
            number = Within(elementSize, sizeof(int)).FirstOrDefault(
                at => at != index && ReadInt(value, at) == 0 && ReadInt(objectName, at) > 0,
                -1);
        }

        Record(NameIndex, index, UdkSR.UnmeasuredName);
        Record(NameNumber, index == -1 ? -1 : number, UdkSR.UnmeasuredName);
    }

    // Find the int that is at least 1 in every property, above 1 in at least 1 and keeps each property inside the size of its struct.
    // Skip the offsets of the object and property members found, because the index and the name number can pass that check too.
    private void FindArrayDim()
    {
        int nameNumber = _offsets[UdkMembers.ObjectName] + sizeof(int);
        HashSet<int> known =
        [
            nameNumber,
            .. _offsets.Where(static pair => pair.Key.StartsWith("UObject.", StringComparison.Ordinal)
                    || pair.Key.StartsWith("UProperty.", StringComparison.Ordinal))
                .Select(static pair => pair.Value),
        ];

        var properties = ReadProperties();
        Dictionary<Address, int> structSizes = [];
        var found = OffsetsBy(sizeof(int)).Where(at => !known.Contains(at) && FitsAsArrayDim(properties, structSizes, at)).Take(2).ToList();

        if (found.Count == 1)
        {
            _offsets[ArrayDim] = found[0];
        }
        else
        {
            _unmeasured[ArrayDim] = found.Count == 0
                ? UdkSR.UnmeasuredArrayDim
                : UdkSR.FormatUnmeasuredArrayDimAmbiguous($"0x{found[0]:X}", $"0x{found[1]:X}");
        }
    }

    private bool FitsAsArrayDim(List<Address> properties, Dictionary<Address, int> structSizes, int at)
    {
        bool anyArray = false;

        foreach (var property in properties)
        {
            int arrayDim = ReadInt(property, at);

            if (arrayDim is < 1 or > LargestArrayDim)
            {
                return false;
            }

            if (arrayDim == 1)
            {
                continue;
            }

            var owner = _measured.Reader.OuterOf(property).UnwrapOr(Address.Zero);

            if (!structSizes.TryGetValue(owner, out int structSize))
            {
                structSize = ReadInt(owner, _offsets[UdkMembers.PropertySize]);
                structSizes[owner] = structSize;
            }

            if (ValueOffsetOf(property) + ((long)arrayDim * ReadInt(property, _offsets[UdkMembers.ElementSize])) > structSize)
            {
                return false;
            }

            anyArray = true;
        }

        return anyArray;
    }

    // List every object whose class derives from Property and read the super chain of each class once.
    // Skip the class default objects because their element count is 0.
    // Skip classes like AnimNodeBlendByProperty because only their name ends with Property.
    private List<Address> ReadProperties()
    {
        StructReader structReader = new(
            _memory,
            _measured.Reader,
            new StructLayout(_offsets[UdkMembers.SuperField], -1),
            new ChildLayout(-1));
        Dictionary<Address, bool> propertyClasses = [];
        List<Address> properties = [];

        foreach (var group in _measured.ObjectsByBaseName)
        {
            if (group.Key is null || group.Key.StartsWith(DefaultObjectPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var obj in group)
            {
                var klass = _measured.Reader.ClassOf(obj).UnwrapOr(Address.Zero);

                if (!propertyClasses.TryGetValue(klass, out bool isProperty))
                {
                    isProperty = structReader.DerivesFrom(klass, PropertyClass).UnwrapOr(false);
                    propertyClasses[klass] = isProperty;
                }

                if (isProperty)
                {
                    properties.Add(obj);
                }
            }
        }

        return properties;
    }

    private static IEnumerable<int> OffsetsBy(int step)
    {
        for (int at = step; at < LargestOffset; at += step)
        {
            yield return at;
        }
    }

    private static IEnumerable<int> Within(int size, int step)
    {
        for (int at = 0; at + step <= size; at += step)
        {
            yield return at;
        }
    }

    private int ValueOffsetOf(Address field)
    {
        return ReadInt(field, _offsets[UdkMembers.Offset]);
    }

    private int ReadInt(Address obj, int at)
    {
        return _memory.Read<int>(obj + at).UnwrapOr(-1);
    }

    private Address ReadPointer(Address obj, int at)
    {
        return _memory.ReadPointer(obj + at).UnwrapOr(Address.Zero);
    }

    private void Record(string member, int offset, string reason)
    {
        if (offset == -1)
        {
            _unmeasured[member] = reason;
        }
        else
        {
            _offsets[member] = offset;
        }
    }
}
