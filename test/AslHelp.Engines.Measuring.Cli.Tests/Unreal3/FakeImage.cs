using AslHelp.Engines.Measuring.Cli.Unreal3;
using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

/// <summary>
///     Builds a small UDK object graph into a <see cref="FakeMemory"/>, with the offsets of a <see cref="FakeLayout"/>.
/// </summary>
internal sealed class FakeImage
{
    private const int ArrayHeaderSize = 3 * sizeof(int);
    private const uint ArraysRva = 0x3000;
    private const int BlockSize = 0x100;
    private const int DecoyArrayAt = 0x10;
    private const int DecoyCount = 32;
    private const uint DecoyRva = 0x1000;
    private const int ExtraHeaderAt = 0x180;
    private const int NameArrayAt = 0x100;
    private const int ObjectArrayAt = 0x200;
    private const int PageSize = 0x1000;
    private const int SlotsAt = 0x300;

    private static readonly Address _decoyPointers = new(0x0300_0000);
    private static readonly Address _duplicatePointers = new(0x0330_0000);
    private static readonly Address _entriesStart = new(0x0100_0000);
    private static readonly Address _grownPointers = new(0x0800_0000);
    private static readonly Address _namePointers = new(0x0310_0000);
    private static readonly Address _objectPointers = new(0x0320_0000);
    private static readonly Address _objectsStart = new(0x0200_0000);
    private static readonly Address _unreadable = new(0x0700_0000);

    private readonly byte[] _arrays = new byte[2 * PageSize];
    private readonly Dictionary<string, Address> _classes = [];
    private readonly List<Address> _entries = [];
    private readonly FakeLayout _layout;
    private readonly List<string> _names = [];
    private readonly List<Address> _objects = [];
    private readonly Dictionary<string, Address> _properties = [];

    private int _nextSlot = SlotsAt;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeImage"/> class and builds the graph.
    /// </summary>
    /// <param name="layout">The offsets of the fake build.</param>
    /// <param name="firstNames">The names at the start of the name array.</param>
    /// <param name="fillers">The number of objects to add before the classes.</param>
    public FakeImage(FakeLayout layout, IReadOnlyList<string> firstNames, int fillers = 0)
    {
        _layout = layout;

        foreach (string name in firstNames)
        {
            _ = NameId(name);
        }

        var core = AddObject("Core", Address.Zero);

        // Leave 1 position empty, because a real object array has empty positions.
        _objects.Add(Address.Zero);

        // Add the properties before the classes, because the search must skip the property called Class to find the class.
        AddProperties();

        for (int filler = 0; filler < fillers; filler++)
        {
            _ = AddObject("Filler", core);
        }

        AddClasses(core);
        LinkProperties();

        var engine = AddObjectOfClass("GameEngine_0", "GameEngine", core);
        var world = AddObjectOfClass("TheWorld", "World", core);
        var otherWorld = AddObjectOfClass("OtherWorld", "World", core);

        Core = core;
        Engine = engine;
        EngineSlotRva = AddSlot(engine);
        WorldSlotRva = AddSlot(world);
        _ = AddSlot(otherWorld);

        // Add a slot at the class Engine, because a real static can point at a class and the class of that object is Class.
        _ = AddSlot(_classes["Engine"]);

        AddNameArray();
        AddObjectArray();

        byte[] decoy = new byte[PageSize];
        Memory.Add(_decoyPointers, new byte[DecoyCount * sizeof(uint)]);
        Memory.Add(Base + DecoyRva, decoy);
        Memory.WritePointer(Base + DecoyRva + DecoyArrayAt, _decoyPointers);
        Memory.WriteInt32(Base + DecoyRva + DecoyArrayAt + sizeof(uint), DecoyCount);
        Memory.WriteInt32(Base + DecoyRva + DecoyArrayAt + (2 * sizeof(uint)), DecoyCount);
        Memory.Add(Base + ArraysRva, _arrays);

        // Keep 1 pointer in the decoy, because a real array of small ints reads as pointers into nothing.
        Memory.WritePointer(_decoyPointers, new Address(1));

        Sections = [new UdkSection(DecoyRva, decoy), new UdkSection(ArraysRva, _arrays)];
    }

    /// <summary>
    ///     Gets the base of the fake executable.
    /// </summary>
    public static Address Base { get; } = new(0x0040_0000);

    /// <summary>
    ///     Gets the RVA of the name array.
    /// </summary>
    public static uint NameArrayRva => ArraysRva + NameArrayAt;

    /// <summary>
    ///     Gets the RVA of the object array.
    /// </summary>
    public static uint ObjectArrayRva => ArraysRva + ObjectArrayAt;

    /// <summary>
    ///     Gets the RVA of the extra array header that <see cref="AliasNameArray"/> and <see cref="DuplicateNameArray"/> add.
    /// </summary>
    public static uint ExtraHeaderRva => ArraysRva + ExtraHeaderAt;

    /// <summary>
    ///     Gets the package called <c>Core</c>.
    /// </summary>
    public Address Core { get; }

    /// <summary>
    ///     Gets the object of the class <c>GameEngine</c>.
    /// </summary>
    public Address Engine { get; }

    /// <summary>
    ///     Gets the RVA of the static that points at <see cref="Engine"/>.
    /// </summary>
    public uint EngineSlotRva { get; }

    /// <summary>
    ///     Gets the memory with the graph.
    /// </summary>
    public FakeMemory Memory { get; } = new();

    /// <summary>
    ///     Gets the sections without code, whose bytes are the blocks of <see cref="Memory"/>.
    /// </summary>
    public IReadOnlyList<UdkSection> Sections { get; }

    /// <summary>
    ///     Gets the RVA of the static that points at the object called <c>TheWorld</c>.
    /// </summary>
    public uint WorldSlotRva { get; }

    /// <summary>
    ///     Finds the pointer at a position of the object array.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>
    ///     The address of the pointer.
    /// </returns>
    public static Address ObjectPointerAt(int position)
    {
        return _objectPointers + (position * sizeof(uint));
    }

    /// <summary>
    ///     Adds a static that points at <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>
    ///     The RVA of the static.
    /// </returns>
    public uint AddSlot(Address value)
    {
        int at = _nextSlot;
        _nextSlot += sizeof(uint);

        WriteStaticPointer(at, value);

        return ArraysRva + (uint)at;
    }

    /// <summary>
    ///     Adds a 2nd header with the data pointer of the object array.
    /// </summary>
    public void AliasObjectArray()
    {
        WriteHeader(ExtraHeaderAt, _objectPointers, _objects.Count);
    }

    /// <summary>
    ///     Finds the class called <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the class.</param>
    /// <returns>
    ///     The address of the class.
    /// </returns>
    public Address ClassCalled(string name)
    {
        return _classes[name];
    }

    /// <summary>
    ///     Zeroes the pointer at the entry of the name with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    public void ClearEntryPointer(int id)
    {
        Memory.WritePointer(_namePointers + (id * sizeof(uint)), Address.Zero);
    }

    /// <summary>
    ///     Adds a 2nd header whose data pointer points at a copy of the pointers of the object array.
    /// </summary>
    /// <param name="shorterBy">The number of objects the copy leaves out at its end.</param>
    public void DuplicateObjectArray(int shorterBy = 0)
    {
        byte[] pointers = new byte[_objects.Count * sizeof(uint)];
        Memory.Add(_duplicatePointers, pointers);

        for (int position = 0; position < _objects.Count; position++)
        {
            Memory.WritePointer(_duplicatePointers + (position * sizeof(uint)), _objects[position]);
        }

        WriteHeader(ExtraHeaderAt, _duplicatePointers, _objects.Count - shorterBy);
    }

    /// <summary>
    ///     Finds the entry of the name with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <returns>
    ///     The address of the entry.
    /// </returns>
    public Address EntryAt(int id)
    {
        return _entries[id];
    }

    /// <summary>
    ///     Moves the object array to memory with room for <paramref name="count"/> pointers and sets its header to that count.
    /// </summary>
    /// <param name="count">The new count, which is above the number of objects.</param>
    public void GrowObjectArray(int count)
    {
        Memory.Add(_grownPointers, new byte[count * sizeof(uint)]);

        for (int position = 0; position < _objects.Count; position++)
        {
            Memory.WritePointer(_grownPointers + (position * sizeof(uint)), _objects[position]);
        }

        WriteHeader(ObjectArrayAt, _grownPointers, count);
    }

    /// <summary>
    ///     Zeroes the pointer of the object array at every position from <paramref name="length"/> on.
    /// </summary>
    /// <param name="length">The number of positions to keep.</param>
    public void KeepObjectPositions(int length)
    {
        for (int position = length; position < _objects.Count; position++)
        {
            Memory.WritePointer(_objectPointers + (position * sizeof(uint)), Address.Zero);
        }
    }

    /// <summary>
    ///     Finds the property of the class <c>Object</c> called <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    ///     The address of the property.
    /// </returns>
    public Address PropertyCalled(string name)
    {
        return _properties[name];
    }

    /// <summary>
    ///     Swaps the positions of the classes of the super chain, so their indexes grow from <c>Object</c> up to <c>Class</c>.
    /// </summary>
    public void ReverseChainPositions()
    {
        string[] chain = ["Class", "State", "Struct", "Field", "Object"];
        int first = _objects.IndexOf(_classes[chain[0]]);

        for (int step = 0; step < chain.Length; step++)
        {
            var klass = _classes[chain[chain.Length - 1 - step]];

            Memory.WriteInt32(klass + _layout.Index, first + step);
            Memory.WritePointer(_objectPointers + ((first + step) * sizeof(uint)), klass);
        }
    }

    /// <summary>
    ///     Sets the count and the capacity in the header of the name array.
    /// </summary>
    /// <param name="count">The count.</param>
    public void SetNameArrayCount(int count)
    {
        WriteHeader(NameArrayAt, _namePointers, count);
    }

    /// <summary>
    ///     Sets the count and the capacity in the header of the object array.
    /// </summary>
    /// <param name="count">The count.</param>
    public void SetObjectArrayCount(int count)
    {
        WriteHeader(ObjectArrayAt, _objectPointers, count);
    }

    /// <summary>
    ///     Adds a 2nd header with the data pointer of the name array.
    /// </summary>
    public void AliasNameArray()
    {
        WriteHeader(ExtraHeaderAt, _namePointers, _names.Count);
    }

    /// <summary>
    ///     Removes the Children pointer of the class <c>Object</c>.
    /// </summary>
    public void ClearChildren()
    {
        Memory.WritePointer(_classes["Object"] + _layout.Children, Address.Zero);
    }

    /// <summary>
    ///     Zeroes the header of the name array.
    /// </summary>
    public void ClearNameArray()
    {
        _arrays.AsSpan(NameArrayAt, ArrayHeaderSize).Clear();
    }

    /// <summary>
    ///     Zeroes the header of the object array.
    /// </summary>
    public void ClearObjectArray()
    {
        _arrays.AsSpan(ObjectArrayAt, ArrayHeaderSize).Clear();
    }

    /// <summary>
    ///     Removes the class pointer of the property <c>Outer</c>.
    /// </summary>
    public void ClearPropertyClass()
    {
        Memory.WritePointer(_properties["Outer"] + _layout.PropertySize, Address.Zero);
    }

    /// <summary>
    ///     Zeroes every static that points at an object.
    /// </summary>
    public void ClearSlots()
    {
        _arrays.AsSpan(SlotsAt, _nextSlot - SlotsAt).Clear();
    }

    /// <summary>
    ///     Removes the super pointer of the class <c>Struct</c>.
    /// </summary>
    public void ClearSuperOfStruct()
    {
        Memory.WritePointer(_classes["Struct"] + _layout.Super, Address.Zero);
    }

    /// <summary>
    ///     Adds a 2nd header whose data pointer points at a copy of the entry pointers of the name array.
    /// </summary>
    public void DuplicateNameArray()
    {
        byte[] pointers = new byte[_entries.Count * sizeof(uint)];
        Memory.Add(_duplicatePointers, pointers);

        for (int id = 0; id < _entries.Count; id++)
        {
            Memory.WritePointer(_duplicatePointers + (id * sizeof(uint)), _entries[id]);
        }

        WriteHeader(ExtraHeaderAt, _duplicatePointers, _names.Count);
    }

    /// <summary>
    ///     Points the object array at memory that does not read.
    /// </summary>
    public void MakeObjectArrayDataUnreadable()
    {
        WriteStaticPointer(ObjectArrayAt, _unreadable);
    }

    /// <summary>
    ///     Writes the name with <paramref name="id"/> as UTF-16 with the wide bit set in its index.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    public void MakeNameWide(int id)
    {
        Memory.WriteInt32(_entries[id] + _layout.EntryIndex, (id << 1) | 1);
        Memory.WriteBytes(_entries[id] + _layout.EntryName, Encoding.Unicode.GetBytes($"{_names[id]}\0"));
    }

    /// <summary>
    ///     Writes <paramref name="text"/> over the name with <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="text">The new text.</param>
    public void RenameEntry(int id, string text)
    {
        Memory.WriteBytes(_entries[id] + _layout.EntryName, Encoding.ASCII.GetBytes($"{text}\0"));
    }

    /// <summary>
    ///     Writes -1 over every int of every object except its index, so no object reads as a name at any offset.
    /// </summary>
    public void ScrambleObjects()
    {
        foreach (var value in _objects)
        {
            if (value == Address.Zero)
            {
                continue;
            }

            for (int at = 0; at < BlockSize; at += sizeof(int))
            {
                if (at != _layout.Index)
                {
                    Memory.WriteInt32(value + at, -1);
                }
            }
        }
    }

    /// <summary>
    ///     Adds a static on the 2nd page of the section that points at <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The object.</param>
    /// <returns>
    ///     The RVA of the static.
    /// </returns>
    public uint AddSlotOnSecondPage(Address value)
    {
        WriteStaticPointer(PageSize, value);

        return ArraysRva + PageSize;
    }

    /// <summary>
    ///     Zeroes the 2nd page of the section, as the read of a page that fails does.
    /// </summary>
    public void ClearSecondPage()
    {
        _arrays.AsSpan(PageSize).Clear();
    }

    private void AddClasses(Address core)
    {
        (string Name, string? Super, int Size)[] classes =
        [
            ("Class", "State", _layout.ClassSize),
            ("State", "Struct", _layout.StateSize),
            ("Struct", "Field", _layout.StructSize),
            ("Field", "Object", _layout.FieldSize),
            ("Object", null, _layout.ObjectSize),
            ("Package", "Object", _layout.ObjectSize),
            ("Property", "Field", _layout.PropertySize),
            ("ObjectProperty", "Property", _layout.PropertySize + sizeof(uint)),
            ("NameProperty", "Property", _layout.PropertySize),
            ("Function", "Struct", _layout.StructSize + sizeof(uint)),
            ("Engine", "Object", _layout.ObjectSize),
            ("GameEngine", "Engine", _layout.ObjectSize + sizeof(uint)),
            ("World", "Object", _layout.ObjectSize),
        ];

        foreach ((string name, _, int size) in classes)
        {
            var klass = AddObject(name, core);
            Memory.WriteInt32(klass + _layout.Size, size);
            _classes[name] = klass;
        }

        foreach ((string name, string? super, _) in classes)
        {
            Memory.WritePointer(_classes[name] + _layout.Class, _classes["Class"]);
            Memory.WritePointer(_classes[name] + _layout.Super, super is null ? Address.Zero : _classes[super]);
        }

        Memory.WritePointer(core + _layout.Class, _classes["Package"]);
    }

    private void AddNameArray()
    {
        Memory.Add(_namePointers, new byte[_names.Count * sizeof(uint)]);

        for (int id = 0; id < _names.Count; id++)
        {
            var entry = _entriesStart + (id * BlockSize);
            Memory.Add(entry, new byte[BlockSize]);
            Memory.WriteInt32(entry + _layout.EntryIndex, id << 1);
            Memory.WriteBytes(entry + _layout.EntryName, Encoding.ASCII.GetBytes($"{_names[id]}\0"));
            Memory.WritePointer(_namePointers + (id * sizeof(uint)), entry);
            _entries.Add(entry);
        }

        WriteHeader(NameArrayAt, _namePointers, _names.Count);
    }

    private Address AddObject(string name, Address outer)
    {
        var value = _objectsStart + (_objects.Count * BlockSize);
        Memory.Add(value, new byte[BlockSize]);

        Memory.WriteInt32(value + _layout.Index, _objects.Count);
        Memory.WriteInt32(value + _layout.Name, NameId(name));
        Memory.WritePointer(value + _layout.Outer, outer);

        _objects.Add(value);

        return value;
    }

    private void AddObjectArray()
    {
        Memory.Add(_objectPointers, new byte[_objects.Count * sizeof(uint)]);

        for (int position = 0; position < _objects.Count; position++)
        {
            Memory.WritePointer(_objectPointers + (position * sizeof(uint)), _objects[position]);
        }

        WriteHeader(ObjectArrayAt, _objectPointers, _objects.Count);
    }

    private Address AddObjectOfClass(string name, string klass, Address outer)
    {
        var value = AddObject(name, outer);
        Memory.WritePointer(value + _layout.Class, _classes[klass]);

        return value;
    }

    private void AddProperties()
    {
        foreach (string name in (string[])["Outer", "Name", "IsA", "Class", "Linker"])
        {
            _properties[name] = AddObject(name, Address.Zero);
        }
    }

    private void LinkProperties()
    {
        var objectClass = _classes["Object"];

        (string Name, string Class, int Offset, int ElementSize)[] children =
        [
            ("Outer", "ObjectProperty", _layout.Outer, sizeof(uint)),
            ("Name", "NameProperty", _layout.Name, 2 * sizeof(uint)),
            ("IsA", "Function", 0, 0),
            ("Class", "ObjectProperty", _layout.Class, sizeof(uint)),
            ("Linker", "ObjectProperty", _layout.Class + sizeof(uint), sizeof(uint)),
        ];

        var previousField = Address.Zero;
        var previousProperty = Address.Zero;

        foreach ((string name, string klass, int offset, int elementSize) in children)
        {
            var child = _properties[name];

            Memory.WritePointer(child + _layout.Outer, objectClass);
            Memory.WritePointer(child + _layout.Class, _classes[klass]);
            Memory.WritePointer(previousField == Address.Zero ? objectClass + _layout.Children : previousField + _layout.Next, child);

            previousField = child;

            if (klass.EndsWith("Property", StringComparison.Ordinal))
            {
                Memory.WriteInt32(child + _layout.Offset, offset);
                Memory.WriteInt32(child + _layout.ElementSize, elementSize);

                if (previousProperty != Address.Zero)
                {
                    Memory.WritePointer(previousProperty + _layout.LinkNext, child);
                }

                previousProperty = child;
            }
        }

        Memory.WritePointer(_properties["Outer"] + _layout.PropertySize, objectClass);
        Memory.WritePointer(_properties["Class"] + _layout.PropertySize, _classes["Class"]);
    }

    private int NameId(string name)
    {
        int id = _names.IndexOf(name);

        if (id >= 0)
        {
            return id;
        }

        _names.Add(name);

        return _names.Count - 1;
    }

    private void WriteHeader(int at, Address data, int count)
    {
        WriteStaticPointer(at, data);
        BinaryPrimitives.WriteInt32LittleEndian(_arrays.AsSpan(at + sizeof(uint)), count);
        BinaryPrimitives.WriteInt32LittleEndian(_arrays.AsSpan(at + (2 * sizeof(uint))), count);
    }

    private void WriteStaticPointer(int at, Address value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_arrays.AsSpan(at), (uint)value.Value);
    }
}
