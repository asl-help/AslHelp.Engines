using AslHelp.Engines.Unreal;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Describes the objects that <see cref="UdkMembers.Measure"/> read.
/// </summary>
/// <param name="ObjectsByName">The objects of the object array by base name.</param>
/// <param name="ClassClass">The class called Class.</param>
/// <param name="Reader">The reader with the offsets found.</param>
internal sealed record UdkMeasuredObjects(
    ILookup<string?, Address> ObjectsByName,
    Address ClassClass,
    ObjectReader Reader);

/// <summary>
///     Lists the members to measure and finds their offsets through the classes of the Core package.
/// </summary>
/// <remarks>
///     Reads through <see cref="ObjectReader"/> and <see cref="StructReader"/> with the offsets found so far and -1 for the others.
///     Each search then adds its offset to the layout the next search uses.
/// </remarks>
internal sealed class UdkMembers
{
    /// <summary>
    ///     Represents the report key of the pointer to the 1st field of a struct.
    /// </summary>
    public const string Children = "UStruct.Children";

    /// <summary>
    ///     Represents the report key of the size of 1 element of a property.
    /// </summary>
    public const string ElementSize = "UProperty.ElementSize";

    /// <summary>
    ///     Represents the report key of the shifted id of a name entry.
    /// </summary>
    public const string EntryIndex = "FNameEntry.Index";

    /// <summary>
    ///     Represents the report key of the text of a name entry.
    /// </summary>
    public const string EntryName = "FNameEntry.Name";

    /// <summary>
    ///     Represents the report key of the names of an enum.
    /// </summary>
    public const string EnumNames = "UEnum.Names";

    /// <summary>
    ///     Represents the report key of the pointer to the next field.
    /// </summary>
    public const string Next = "UField.Next";

    /// <summary>
    ///     Represents the report key of the pointer to the class of an object.
    /// </summary>
    public const string ObjectClass = "UObject.Class";

    /// <summary>
    ///     Represents the report key of the position of an object in the object array.
    /// </summary>
    public const string ObjectInternalInteger = "UObject.ObjectInternalInteger";

    /// <summary>
    ///     Represents the report key of the name id of an object.
    /// </summary>
    public const string ObjectName = "UObject.Name";

    /// <summary>
    ///     Represents the report key of the pointer to the outer object of an object.
    /// </summary>
    public const string ObjectOuter = "UObject.Outer";

    /// <summary>
    ///     Represents the report key of the offset of a property in its struct.
    /// </summary>
    public const string Offset = "UProperty.Offset";

    /// <summary>
    ///     Represents the report key of the pointer to the class of an object property.
    /// </summary>
    public const string PropertyClass = "UObjectProperty.PropertyClass";

    /// <summary>
    ///     Represents the report key of the pointer to the next property of a struct.
    /// </summary>
    public const string PropertyLinkNext = "UProperty.PropertyLinkNext";

    /// <summary>
    ///     Represents the report key of the size in bytes of the properties of a struct.
    /// </summary>
    public const string PropertySize = "UStruct.PropertySize";

    /// <summary>
    ///     Represents the report key of the pointer to the struct that a struct derives from.
    /// </summary>
    public const string SuperField = "UStruct.SuperField";

    private const int LargestObjectSize = 0x200;
    private const int LargestOffset = 0x100;
    private const int LeastChildren = 5;
    private const int LeastNamedPercent = 95;
    private const int SampleLength = 512;
    private const int UnknownOffset = -1;

    private readonly IMemoryReader _memory;
    private readonly FlatNameTable _names;
    private readonly ILookup<string?, Address> _objectsByName;
    private readonly Dictionary<string, int> _offsets;
    private readonly int _pointerSize;
    private readonly Dictionary<string, string> _unmeasured;

    private ObjectLayout _layout;
    private ObjectReader _objects;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UdkMembers"/> class.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="names">The name array.</param>
    /// <param name="objects">The object array.</param>
    /// <param name="nameOffset">The offset of the name id in an object.</param>
    /// <param name="offsets">The offsets found so far.</param>
    /// <param name="unmeasured">The reasons for the members that were not found.</param>
    private UdkMembers(
        IMemoryReader memory,
        UdkNameArray names,
        UdkObjectArray objects,
        int nameOffset,
        Dictionary<string, int> offsets,
        Dictionary<string, string> unmeasured)
    {
        _memory = memory;
        _names = names.Table;
        _offsets = offsets;
        _pointerSize = memory.Layout.PointerSize;
        _unmeasured = unmeasured;
        _layout = new ObjectLayout(objects.IndexOffset, UnknownOffset, nameOffset, UnknownOffset);
        _objects = new ObjectReader(memory, _names, _layout);

        // Read every name once, because the readers do not cache and each class lookup would read all objects again.
        _objectsByName = objects.Pointers.ToLookup(_objects.NameOf);
    }

    /// <summary>
    ///     Finds the offset of every member and adds it to <paramref name="offsets"/> or adds the reason to <paramref name="unmeasured"/>.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="names">The name array.</param>
    /// <param name="objects">The object array.</param>
    /// <param name="offsets">The offset of each member, by member.</param>
    /// <param name="unmeasured">The reason for each member that was not found, by member.</param>
    /// <returns>
    ///     The objects of the object array by base name, the class called Class and the reader with the offsets found.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The name, class or outer offset was not found or a class of the super chain does not exist.
    /// </exception>
    public static UdkMeasuredObjects Measure(
        IMemoryReader memory,
        UdkNameArray names,
        UdkObjectArray objects,
        Dictionary<string, int> offsets,
        Dictionary<string, string> unmeasured)
    {
        offsets[EntryIndex] = names.Layout.Index;
        offsets[EntryName] = names.Layout.Name;
        offsets[ObjectInternalInteger] = objects.IndexOffset;

        UdkMembers members = new(memory, names, objects, FindNameOffset(memory, names, objects), offsets, unmeasured);
        var classClass = members.FindClassOffset();

        members.FindOuterAndStructMembers(classClass);

        return new UdkMeasuredObjects(members._objectsByName, classClass, members._objects);
    }

    private static IEnumerable<int> OffsetsBy(int step)
    {
        for (int at = step; at < LargestOffset; at += step)
        {
            yield return at;
        }
    }

    private static int FindNameOffset(IMemoryReader memory, UdkNameArray names, UdkObjectArray objects)
    {
        var sample = objects.Array.Read(SampleLength).Where(static obj => obj != Address.Zero).ToList();
        int best = UnknownOffset;
        int mostDistinct = 0;

        foreach (int at in OffsetsBy(sizeof(int)))
        {
            if (at == objects.IndexOffset)
            {
                continue;
            }

            ObjectReader reader = new(memory, names.Table, new ObjectLayout(objects.IndexOffset, UnknownOffset, at, UnknownOffset));
            HashSet<string> distinct = [with(StringComparer.Ordinal)];
            int named = 0;

            foreach (var obj in sample)
            {
                if (reader.NameOf(obj) is { } name)
                {
                    named++;
                    _ = distinct.Add(name);
                }
            }

            if (named * 100 >= sample.Count * LeastNamedPercent && distinct.Count > mostDistinct)
            {
                best = at;
                mostDistinct = distinct.Count;
            }
        }

        return best == UnknownOffset ? throw new InvalidOperationException(UdkSR.NameOffsetNotFound) : best;
    }

    /// <summary>
    ///     Finds the class offset at which the object called Class points at itself.
    /// </summary>
    /// <returns>
    ///     The class called Class.
    /// </returns>
    /// <exception cref="InvalidOperationException">The offset was not found.</exception>
    private Address FindClassOffset()
    {
        foreach (var obj in _objectsByName["Class"])
        {
            int found = UnknownOffset;

            foreach (int at in OffsetsBy(_pointerSize))
            {
                if (at != _layout.Name && new ObjectReader(_memory, _names, _layout with { Class = at }).ClassOf(obj) == obj)
                {
                    found = at;
                }
            }

            if (found != UnknownOffset)
            {
                UseLayout(_layout with { Class = found });

                return obj;
            }
        }

        throw new InvalidOperationException(UdkSR.ClassOffsetNotFound);
    }

    /// <summary>
    ///     Finds the outer offset and then every struct and property member.
    /// </summary>
    /// <param name="classClass">The class called Class.</param>
    /// <exception cref="InvalidOperationException">The outer offset was not found or a class of the super chain does not exist.</exception>
    private void FindOuterAndStructMembers(Address classClass)
    {
        var fieldClass = RequireClass(classClass, "Field");
        var objectClass = RequireClass(classClass, "Object");

        UseLayout(_layout with { Outer = FindOuterOffset(classClass, objectClass) });

        _offsets[ObjectClass] = _layout.Class;
        _offsets[ObjectName] = _layout.Name;
        _offsets[ObjectOuter] = _layout.Outer;

        Address[] chain = [classClass, RequireClass(classClass, "State"), RequireClass(classClass, "Struct"), fieldClass, objectClass];

        Record(SuperField, FindSuperOffset(chain), UdkSR.UnmeasuredSuperField);

        int sizeAt = Record(PropertySize, FindSizeOffset(chain), UdkSR.UnmeasuredPropertySize);

        var properties = FindChildren(objectClass);

        FindPropertyMembers(properties);
        FindMembersAtClassSizes(classClass, fieldClass, objectClass, sizeAt, properties);
    }

    private int FindOuterOffset(Address classClass, Address objectClass)
    {
        int found = UnknownOffset;

        foreach (int at in OffsetsBy(_pointerSize))
        {
            if (at != _layout.Class && at != _layout.Name && at != _layout.Index)
            {
                ObjectReader reader = new(_memory, _names, _layout with { Outer = at });

                if (string.Equals(_objects.NameOf(reader.OuterOf(classClass)), "Core", StringComparison.Ordinal)
                    && string.Equals(_objects.NameOf(reader.OuterOf(objectClass)), "Core", StringComparison.Ordinal))
                {
                    found = at;
                }
            }
        }

        return found == UnknownOffset ? throw new InvalidOperationException(UdkSR.OuterOffsetNotFound) : found;
    }

    private int FindSuperOffset(Address[] chain)
    {
        return OffsetsBy(_pointerSize).FirstOrDefault(
            at =>
            {
                var reader = Structs(new StructLayout(at, UnknownOffset, UnknownOffset), UnknownOffset);

                return reader.SuperOf(chain[^1]) == Address.Zero
                    && chain.Zip(chain.Skip(1)).All(pair => reader.SuperOf(pair.First) == pair.Second);
            },
            UnknownOffset);
    }

    private int FindSizeOffset(Address[] chain)
    {
        return OffsetsBy(sizeof(int)).FirstOrDefault(
            at =>
            {
                // Skip the index and the name id, because they can grow along the chain too and both sit in front of the size.
                if (at == _layout.Index || at == _layout.Name)
                {
                    return false;
                }

                // Compare along the chain, because each class adds members to the class it derives from.
                int smallest = ReadInt(chain[^1], at);

                return smallest > _layout.Outer
                    && smallest < LargestObjectSize
                    && chain.Zip(chain.Skip(1)).All(pair => ReadInt(pair.First, at) > ReadInt(pair.Second, at));
            },
            UnknownOffset);
    }

    /// <summary>
    ///     Finds the children and next offsets through the fields of the class Object.
    /// </summary>
    /// <param name="objectClass">The class called Object.</param>
    /// <returns>
    ///     The fields of the class Object or an empty list if the offsets were not found.
    /// </returns>
    private List<Address> FindChildren(Address objectClass)
    {
        foreach (int children in OffsetsBy(_pointerSize))
        {
            StructLayout layout = new(UnknownOffset, children, UnknownOffset);

            if (!Structs(layout, UnknownOffset).ChildrenOf(objectClass).Any())
            {
                continue;
            }

            foreach (int next in OffsetsBy(_pointerSize))
            {
                var fields = Structs(layout, next).ChildrenOf(objectClass).ToList();
                var names = fields.ConvertAll(_objects.NameOf);

                if (fields.Count >= LeastChildren && names.Contains("Outer") && names.Contains("Name") && names.Contains("Class"))
                {
                    _offsets[Children] = children;
                    _offsets[Next] = next;

                    return fields;
                }
            }
        }

        _unmeasured[Children] = UdkSR.UnmeasuredChildren;
        _unmeasured[Next] = UdkSR.UnmeasuredChildren;

        return [];
    }

    /// <summary>
    ///     Finds the offset, element size and link offsets through the properties Outer, Name and Class of the class Object.
    /// </summary>
    /// <param name="fields">The fields of the class Object.</param>
    private void FindPropertyMembers(List<Address> fields)
    {
        if (fields.Count == 0)
        {
            string needs = UdkSR.FormatUnmeasuredNeeds(Children);

            _unmeasured[ElementSize] = needs;
            _unmeasured[Offset] = needs;
            _unmeasured[PropertyLinkNext] = needs;

            return;
        }

        var outer = FieldCalled(fields, "Outer");
        var name = FieldCalled(fields, "Name");
        var klass = FieldCalled(fields, "Class");

        int offset = OffsetsBy(sizeof(int)).FirstOrDefault(
            at => ReadInt(outer, at) == _layout.Outer && ReadInt(name, at) == _layout.Name && ReadInt(klass, at) == _layout.Class,
            UnknownOffset);

        // Allow 4 or 8 on Name, because a name is a 4-byte id with or without a 4-byte number.
        int elementSize = OffsetsBy(sizeof(int)).FirstOrDefault(
            at => at != offset && ReadInt(outer, at) == _pointerSize && ReadInt(klass, at) == _pointerSize && ReadInt(name, at) is 4 or 8,
            UnknownOffset);

        _ = Record(Offset, offset, UdkSR.UnmeasuredOffset);
        _ = Record(ElementSize, elementSize, UdkSR.UnmeasuredElementSize);
        _ = Record(PropertyLinkNext, FindLinkOffset(fields), UdkSR.UnmeasuredPropertyLinkNext);
    }

    /// <summary>
    ///     Finds the pointer that connects the properties of a struct and skips its other fields.
    /// </summary>
    /// <param name="fields">The fields of the class Object.</param>
    /// <returns>
    ///     The offset or -1 if it was not found.
    /// </returns>
    private int FindLinkOffset(List<Address> fields)
    {
        HashSet<Address> properties =
        [
            .. fields.Where(field => _objects.NameOf(_objects.ClassOf(field))?.EndsWith("Property", StringComparison.Ordinal) == true),
        ];

        return OffsetsBy(_pointerSize).FirstOrDefault(
            at =>
            {
                // Skip Next, because it connects the properties too in a struct that has no other fields.
                if (at == _offsets[Next])
                {
                    return false;
                }

                var pointers = properties.Select(property => ReadPointer(property, at)).ToList();

                return pointers.TrueForAll(pointer => pointer == Address.Zero || properties.Contains(pointer))
                    && pointers.Exists(pointer => pointer != Address.Zero);
            },
            UnknownOffset);
    }

    /// <summary>
    ///     Finds the members whose offsets are the sizes of the classes Property and Field.
    /// </summary>
    /// <param name="classClass">The class called Class.</param>
    /// <param name="fieldClass">The class called Field.</param>
    /// <param name="objectClass">The class called Object.</param>
    /// <param name="sizeAt">The offset of the size of a class or -1 if it was not found.</param>
    /// <param name="fields">The fields of the class Object.</param>
    private void FindMembersAtClassSizes(Address classClass, Address fieldClass, Address objectClass, int sizeAt, List<Address> fields)
    {
        if (sizeAt == UnknownOffset)
        {
            string needs = UdkSR.FormatUnmeasuredNeeds(PropertySize);

            _unmeasured[EnumNames] = needs;
            _unmeasured[PropertyClass] = needs;

            return;
        }

        // Report UEnum.Names before the check of PropertyClass, because the growth along the chain already checks the size offset.
        _offsets[EnumNames] = ReadInt(fieldClass, sizeAt);

        if (fields.Count == 0)
        {
            _unmeasured[PropertyClass] = UdkSR.FormatUnmeasuredNeeds(Children);

            return;
        }

        var propertyClass = FindClass(classClass, "Property");
        int propertyClassOffset = propertyClass == Address.Zero ? UnknownOffset : ReadInt(propertyClass, sizeAt);

        if (propertyClassOffset != UnknownOffset && ReadPointer(FieldCalled(fields, "Outer"), propertyClassOffset) == objectClass)
        {
            _offsets[PropertyClass] = propertyClassOffset;
        }
        else
        {
            _unmeasured[PropertyClass] = UdkSR.UnmeasuredPropertyClass;
        }
    }

    private Address FieldCalled(List<Address> fields, string name)
    {
        return fields.Find(field => string.Equals(_objects.NameOf(field), name, StringComparison.Ordinal));
    }

    private Address FindClass(Address classClass, string name)
    {
        return _objectsByName[name].FirstOrDefault(obj => _objects.ClassOf(obj) == classClass);
    }

    private Address RequireClass(Address classClass, string name)
    {
        var found = FindClass(classClass, name);

        return found == Address.Zero ? throw new InvalidOperationException(UdkSR.FormatClassNotFound(name)) : found;
    }

    private int Record(string member, int offset, string reason)
    {
        if (offset == UnknownOffset)
        {
            _unmeasured[member] = reason;
        }
        else
        {
            _offsets[member] = offset;
        }

        return offset;
    }

    private int ReadInt(Address obj, int at)
    {
        return _memory.Read<int>(obj + at).UnwrapOr(UnknownOffset);
    }

    private Address ReadPointer(Address obj, int at)
    {
        return _memory.ReadPointer(obj + at).UnwrapOr(Address.Zero);
    }

    private StructReader Structs(StructLayout layout, int next)
    {
        return new StructReader(_memory, _objects, layout, new FieldLayout(next));
    }

    private void UseLayout(ObjectLayout layout)
    {
        _layout = layout;
        _objects = new ObjectReader(_memory, _names, layout);
    }
}
