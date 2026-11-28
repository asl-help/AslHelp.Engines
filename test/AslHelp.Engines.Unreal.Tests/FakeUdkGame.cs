using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Builds and attaches to an x86 game in the storage shapes of UDK, whose world points at a game with a data object that has <c>IntValue</c>
///     and 1 property of each value kind that Unreal Engine 3 stores in its own way.
/// </summary>
/// <remarks>
///     Uses the offsets of <see cref="Document"/>.
///     <c>World</c> gets a <c>Game</c> property that a real UDK world doesn't have. It lets the tests read a path from <c>GWorld</c>.
///     <c>FixtureData</c> is a property of <c>FixtureGame</c>, the class of the game,
///     and <c>GoalScore</c> is a property of its super class <c>GameInfo</c>.
///     <c>bFirst</c>, <c>bSecond</c> and <c>bThird</c> of the data object share 1 4-byte word with the masks 2, 4 and 8.
///     <c>Coord</c> is a struct of the type <c>FixtureCoord</c> with <c>X</c> and <c>Y</c>.
///     The game and the data object have the name number 1. The object array has an empty slot in front of the class with a UTF-16 name.
///     The outer object of every class is 1 of the packages <c>Core</c>, <c>Engine</c> or <c>Fixture</c>.
///     <c>Core</c> is in front of the class <c>Package</c> in the array and the group <c>Effects</c> inside <c>Engine</c> is a package object
///     with an outer object. <c>GEngine</c> points at the engine, an object of <c>GameEngine</c>, whose base <c>Engine</c> has <c>Game</c>.
///     A 2nd data object called <c>FixtureData_1</c> and <c>Default__FixtureData</c> are objects of <c>FixtureData</c> too.
/// </remarks>
internal sealed class FakeUdkGame
{
    /// <summary>
    ///     The 2010-09 profile of the UDK player as the measure tool wrote it, with 1 line per type.
    /// </summary>
    public const string Document = $$"""
        {
          "engine": "unreal3",
          "versions": ["2010-09"],
          "offsets": {
            "FName": { "Index": "0x0", "Number": "0x4" },
            "FNameEntry": { "Index": "0x8", "Name": "0x10" },
            "FScriptArray": { "Data": "0x0", "ArrayNum": "0x4" },
            "UBoolProperty": { "BitMask": "0x80" },
            "UEnum": { "Names": "0x40" },
            "UField": { "Next": "0x3C" },
            "UObject": { "ObjectInternalInteger": "0x4", "Outer": "0x28", "Name": "0x2C", "Class": "0x34" },
            "UObjectProperty": { "PropertyClass": "0x80" },
            "UProperty": { "ArrayDim": "0x40", "ElementSize": "0x44", "Offset": "0x60", "PropertyLinkNext": "0x64" },
            "UStruct": { "SuperField": "0x48", "Children": "0x4C", "PropertySize": "0x50" },
            "UStructProperty": { "Struct": "0x80" }
          },
          "globals": {
            "GNames": { "2010-09": [{ "forward": "A3 ???????? 89 33", "offset": 1 }, { "read": "global" }] },
            "GObjObjects": { "2010-09": {{GObjObjectsRoute}} },
            "GEngine": { "2010-09": [{ "forward": "2B F0 A1 ????????", "offset": 3 }, { "read": "global" }] },
            "GWorld": { "2010-09": [{ "forward": "89 07 A1 ????????", "offset": 3 }, { "read": "global" }] }
          }
        }
        """;

    /// <summary>
    ///     The value of <c>Coord.X</c> in the data object.
    /// </summary>
    public const int CoordX = 7;

    /// <summary>
    ///     The value of <c>Coord.Y</c> in the data object.
    /// </summary>
    public const int CoordY = 9;

    /// <summary>
    ///     The value of <c>GoalScore</c> in the game.
    /// </summary>
    public const int GoalScore = 25;

    /// <summary>
    ///     The offset of <c>GoalScore</c> in the game.
    /// </summary>
    public const int GoalScoreOffset = 0x84;

    /// <summary>
    ///     The route of <c>GObjObjects</c> in <see cref="Document"/>.
    /// </summary>
    public const string GObjObjectsRoute = """[{ "forward": "3B F0 8B 0D ????????", "offset": 4 }, { "read": "global" }]""";

    /// <summary>
    ///     The value of <c>IntValue</c> in the data object.
    /// </summary>
    public const int IntValue = 1337;

    /// <summary>
    ///     The offset of <c>IntValue</c> in the data object.
    /// </summary>
    public const int IntValueOffset = 0x88;

    /// <summary>
    ///     The name of the module.
    /// </summary>
    public const string ModuleName = "UDK.exe";

    /// <summary>
    ///     The text of <c>NameValue</c> in the data object.
    /// </summary>
    public const string NameText = "fixture-name";

    /// <summary>
    ///     The number of objects in the object array, with the empty slot.
    /// </summary>
    public const int ObjectCount = 24;

    /// <summary>
    ///     The text of <c>StringValue</c> in the data object.
    /// </summary>
    public const string StringText = "hello-fixture";

    /// <summary>
    ///     The name of the class whose name entry is UTF-16.
    /// </summary>
    public const string WideClassName = "フィクスチャ";

    private const int ArrayDimOffset = 0x40;
    private const int BitMaskOffset = 0x80;
    private const int BitsOffset = 0x8C;
    private const int ChildrenOffset = 0x4C;
    private const int ClassId = 1;
    private const int CoordOffset = 0xA4;
    private const int DataOffset = 0x90;
    private const int ElementSizeOffset = 0x44;
    private const int EngineGameOffset = 0x84;
    private const int FixtureDataId = 9;
    private const int FixtureGameId = 8;
    private const int GameId = 11;
    private const int GameInfoId = 7;
    private const int GameOffset = 0x80;
    private const int GoalScoreId = 12;
    private const int IntPropertyId = 4;
    private const int IntValueId = 13;
    private const int ModuleSize = 0x1000;
    private const int NameValueOffset = 0x9C;
    private const int NextOffset = 0x3C;
    private const int ObjectId = 2;
    private const int ObjectPropertyId = 5;
    private const int PropertyId = 3;
    private const int PropertyOffsetOffset = 0x60;
    private const int StringValueOffset = 0x90;
    private const int StructTypeOffset = 0x80;
    private const int SuperFieldOffset = 0x48;
    private const int TheWorldId = 10;
    private const int WideClassId = 14;
    private const int WorldId = 6;

    private static readonly NameEntryLayout _entryLayout = new(0x8, 0x10);
    private static readonly ObjectLayout _objectLayout = new(0x28, 0x2C, 0x34);

    // List the texts of the lookup objects and the value properties after the texts with an id constant, so their ids follow WideClassId.
    private static readonly string[] _valueNames =
    [
        "Package",
        "Core",
        "Engine",
        "Fixture",
        "Effects",
        "GameEngine",
        "Default__FixtureData",
        "BoolProperty",
        "StrProperty",
        "NameProperty",
        "StructProperty",
        "bFirst",
        "bSecond",
        "bThird",
        "StringValue",
        "NameValue",
        "Coord",
        "FixtureCoord",
        "X",
        "Y",
        NameText,
    ];

    private readonly Address _classClass;
    private readonly Address _coordType;
    private readonly Address _corePackage;
    private readonly Address _dataClass;
    private readonly Address _defaultData;
    private readonly Address _effectsGroup;
    private readonly Address _engineClass;
    private readonly Address _enginePackage;
    private readonly Address _fixtureGameClass;
    private readonly Address _fixturePackage;
    private readonly Address _gameEngineClass;
    private readonly Address _gameInfoClass;
    private readonly Address _intPropertyClass;
    private readonly Address _objectClass;
    private readonly Address _objectPropertyClass;
    private readonly Address _packageClass;
    private readonly Address _propertyClass;
    private readonly Address _stringData;
    private readonly Address _wideClass;
    private readonly Address _worldClass;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeUdkGame"/> class and builds the memory of the game at <c>0x80000000</c>,
    ///     so an operand read as signed misses.
    /// </summary>
    public FakeUdkGame()
    {
        Address start = new(0x8000_0000);

        Module = new MemoryModule(ModuleName, null, new MemoryRange(start, ModuleSize));

        _classClass = start + 0x20_0000;
        _objectClass = start + 0x20_1000;
        _propertyClass = start + 0x20_2000;
        _intPropertyClass = start + 0x20_3000;
        _objectPropertyClass = start + 0x20_4000;
        _worldClass = start + 0x20_5000;
        _gameInfoClass = start + 0x20_6000;
        _fixtureGameClass = start + 0x20_7000;
        _dataClass = start + 0x20_8000;
        _wideClass = start + 0x20_9000;
        _coordType = start + 0x20_A000;
        _packageClass = start + 0x20_B000;
        _engineClass = start + 0x20_C000;
        _gameEngineClass = start + 0x20_D000;
        _corePackage = start + 0x21_0000;
        _enginePackage = start + 0x21_1000;
        _fixturePackage = start + 0x21_2000;
        _effectsGroup = start + 0x21_3000;
        _stringData = start + 0x40_3000;
        _defaultData = start + 0x40_6000;

        NameTableGlobal = start + 0x10_0000;
        GWorldSlot = start + 0x12_0000;
        GEngineSlot = start + 0x12_1000;
        World = start + 0x40_0000;
        Game = start + 0x40_1000;
        Data = start + 0x40_2000;
        Engine = start + 0x40_4000;
        SecondData = start + 0x40_5000;

        Memory = new FakeMemory { Layout = MemoryLayout.X86 };
        AddModule(NameTableGlobal, start + 0x11_0000);
        AddNames(NameTableGlobal, start + 0x10_1000, start + 0x30_0000);
        AddObjectArray(start + 0x11_0000, start + 0x11_1000);
        AddClasses();
        AddProperties(start + 0x50_0000);
        AddValueProperties(start + 0x51_0000, start + 0x60_0000);
        AddObjects();
    }

    /// <summary>
    ///     Gets the address of the data object that the game points at, called <c>FixtureData_0</c>.
    /// </summary>
    public Address Data { get; }

    /// <summary>
    ///     Gets the address of the engine that <c>GEngine</c> points at, an object of <c>GameEngine</c> whose <c>Game</c> points at the game.
    /// </summary>
    public Address Engine { get; }

    /// <summary>
    ///     Gets the address of the game that the world points at, an object of <c>FixtureGame</c> called <c>FixtureGame_0</c>.
    /// </summary>
    public Address Game { get; }

    /// <summary>
    ///     Gets the address of the <c>GEngine</c> slot.
    /// </summary>
    public Address GEngineSlot { get; }

    /// <summary>
    ///     Gets the address of the <c>GWorld</c> slot.
    /// </summary>
    public Address GWorldSlot { get; }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public FakeMemory Memory { get; }

    /// <summary>
    ///     Gets the module that the routes of <see cref="Document"/> find the globals in.
    /// </summary>
    public MemoryModule Module { get; }

    /// <summary>
    ///     Gets the address of <c>GNames</c>, the array of name entry pointers.
    /// </summary>
    public Address NameTableGlobal { get; }

    /// <summary>
    ///     Gets the address of the 2nd data object, called <c>FixtureData_1</c>, whose <c>IntValue</c> is 7.
    /// </summary>
    public Address SecondData { get; }

    /// <summary>
    ///     Gets the address of the world, called <c>TheWorld</c>.
    /// </summary>
    public Address World { get; }

    /// <summary>
    ///     Attaches to the game with <see cref="Document"/>.
    /// </summary>
    /// <returns>
    ///     The game.
    /// </returns>
    public UnrealGame Attach()
    {
        return UnrealGame.Attach(Memory, Module, UnrealProfile.Parse(Document).Unwrap()).Unwrap();
    }

    // Adds the module with the instruction of each global that attach locates and the slots of GWorld and GEngine.
    private void AddModule(Address gNames, Address gObjObjects)
    {
        var module = Module.Range.Start;

        Memory.Add(module, new byte[ModuleSize]);
        Memory.WriteGlobalReference(module + 0x100, [0xA3], gNames);
        Memory.WriteGlobalReference(module + 0x200, [0x3B, 0xF0, 0x8B, 0x0D], gObjObjects);
        Memory.WriteGlobalReference(module + 0x300, [0x89, 0x07, 0xA1], GWorldSlot);
        Memory.WriteGlobalReference(module + 0x400, [0x2B, 0xF0, 0xA1], GEngineSlot);

        // Write 89 33 after the operand of GNames, because its signature continues past the operand.
        Memory.WriteInt32(module + 0x105, 0x3389);
        Memory.AddPointers(GWorldSlot, World);
        Memory.AddPointers(GEngineSlot, Engine);
    }

    // Adds the name array with an entry per name, so a name's id is its position in the list. Sets the low index bit of the wide entry.
    private void AddNames(Address gNames, Address data, Address entries)
    {
        string[] texts =
        [
            "None",
            "Class",
            "Object",
            "Property",
            "IntProperty",
            "ObjectProperty",
            "World",
            "GameInfo",
            "FixtureGame",
            "FixtureData",
            "TheWorld",
            "Game",
            "GoalScore",
            "IntValue",
            WideClassName,
            .. _valueNames,
        ];
        var entryAddresses = new Address[texts.Length];

        for (int id = 0; id < texts.Length; id++)
        {
            entryAddresses[id] = entries + (id * 0x100);
            Memory.AddNameEntry(entryAddresses[id], _entryLayout, (id << 1) | (id == WideClassId ? 1 : 0), texts[id]);
        }

        Memory.AddNameArray(gNames, data, texts.Length, entryAddresses);
    }

    // Adds GObjObjects as a data pointer and a count at the global itself, with an empty slot in front of the wide class.
    private void AddObjectArray(Address gObjObjects, Address data)
    {
        Address[] objects =
        [
            _classClass,
            _corePackage,
            _objectClass,
            _propertyClass,
            _intPropertyClass,
            _objectPropertyClass,
            _worldClass,
            _gameInfoClass,
            _fixtureGameClass,
            _dataClass,
            Address.Zero,
            _wideClass,
            _packageClass,
            _enginePackage,
            _fixturePackage,
            _effectsGroup,
            _engineClass,
            _gameEngineClass,
            Engine,
            SecondData,
            _defaultData,
            World,
            Game,
            Data,
        ];

        Memory.AddNameArray(gObjObjects, data, objects.Length, objects);
    }

    // Adds the packages and the classes, each an object of Class, with their super classes and packages.
    private void AddClasses()
    {
        Memory.AddObject(_corePackage, _objectLayout, ValueNameId("Core"), klass: _packageClass);
        Memory.AddObject(_enginePackage, _objectLayout, ValueNameId("Engine"), klass: _packageClass);
        Memory.AddObject(_fixturePackage, _objectLayout, ValueNameId("Fixture"), klass: _packageClass);
        Memory.AddObject(_effectsGroup, _objectLayout, ValueNameId("Effects"), outer: _enginePackage, klass: _packageClass);
        AddClass(_classClass, ClassId, Address.Zero, _corePackage);
        AddClass(_objectClass, ObjectId, Address.Zero, _corePackage);
        AddClass(_propertyClass, PropertyId, _objectClass, _corePackage);
        AddClass(_intPropertyClass, IntPropertyId, _propertyClass, _corePackage);
        AddClass(_objectPropertyClass, ObjectPropertyId, _propertyClass, _corePackage);
        AddClass(_packageClass, ValueNameId("Package"), _objectClass, _corePackage);
        AddClass(_worldClass, WorldId, _objectClass, _enginePackage);
        AddClass(_gameInfoClass, GameInfoId, _objectClass, _enginePackage);
        AddClass(_engineClass, ValueNameId("Engine"), _objectClass, _enginePackage);
        AddClass(_gameEngineClass, ValueNameId("GameEngine"), _engineClass, _enginePackage);
        AddClass(_fixtureGameClass, FixtureGameId, _gameInfoClass, _fixturePackage);
        AddClass(_dataClass, FixtureDataId, _objectClass, _fixturePackage);
        AddClass(_wideClass, WideClassId, _objectClass, _fixturePackage);
    }

    // Adds 1 property per class that declares one, each the 1st field of its class.
    private void AddProperties(Address properties)
    {
        int pointerSize = Memory.Layout.PointerSize;

        AddProperty(_worldClass, properties, GameId, _objectPropertyClass, GameOffset, pointerSize);
        AddProperty(_engineClass, properties + 0x4000, GameId, _objectPropertyClass, EngineGameOffset, pointerSize);
        AddProperty(_gameInfoClass, properties + 0x1000, GoalScoreId, _intPropertyClass, GoalScoreOffset, sizeof(int));
        AddProperty(_fixtureGameClass, properties + 0x2000, FixtureDataId, _objectPropertyClass, DataOffset, pointerSize);
        AddProperty(_dataClass, properties + 0x3000, IntValueId, _intPropertyClass, IntValueOffset, sizeof(int));
    }

    // Adds the world, the game and the data object with the pointers between them.
    private void AddObjects()
    {
        Memory.AddObject(World, _objectLayout, TheWorldId, klass: _worldClass);
        Memory.AddObject(Game, _objectLayout, FixtureGameId, number: 1, klass: _fixtureGameClass);
        Memory.AddObject(Data, _objectLayout, FixtureDataId, number: 1, klass: _dataClass);
        Memory.AddObject(SecondData, _objectLayout, FixtureDataId, number: 2, klass: _dataClass);
        Memory.AddObject(_defaultData, _objectLayout, ValueNameId("Default__FixtureData"), klass: _dataClass);
        Memory.AddObject(Engine, _objectLayout, ValueNameId("GameEngine"), number: 1, klass: _gameEngineClass);
        Memory.WritePointer(Engine + EngineGameOffset, Game);
        Memory.WriteInt32(SecondData + IntValueOffset, 7);

        Memory.WritePointer(World + GameOffset, Game);
        Memory.WritePointer(Game + DataOffset, Data);
        Memory.WriteInt32(Game + GoalScoreOffset, GoalScore);
        Memory.WriteInt32(Data + IntValueOffset, IntValue);

        // Set bFirst and bThird and leave bSecond in the shared word.
        Memory.WriteInt32(Data + BitsOffset, 0b1010);
        Memory.Add(_stringData, new byte[0x100]);
        Memory.WriteUtf16(_stringData, StringText + '\0');
        Memory.WritePointer(Data + StringValueOffset, _stringData);
        Memory.WriteInt32(Data + StringValueOffset + 4, StringText.Length + 1);
        Memory.WriteInt32(Data + NameValueOffset, ValueNameId(NameText));
        Memory.WriteInt32(Data + CoordOffset, CoordX);
        Memory.WriteInt32(Data + CoordOffset + 4, CoordY);
    }

    private static int ValueNameId(string name)
    {
        return WideClassId + 1 + Array.IndexOf(_valueNames, name);
    }

    // Adds the classes of the value kinds, the struct type and the value properties of the data object, chained behind IntValue.
    private void AddValueProperties(Address kinds, Address properties)
    {
        var boolClass = kinds;
        var stringClass = kinds + 0x1000;
        var nameClass = kinds + 0x2000;
        var structClass = kinds + 0x3000;

        AddClass(boolClass, ValueNameId("BoolProperty"), _propertyClass, _corePackage);
        AddClass(stringClass, ValueNameId("StrProperty"), _propertyClass, _corePackage);
        AddClass(nameClass, ValueNameId("NameProperty"), _propertyClass, _corePackage);
        AddClass(structClass, ValueNameId("StructProperty"), _propertyClass, _corePackage);
        Memory.AddObject(_coordType, _objectLayout, ValueNameId("FixtureCoord"));

        var y = AddField(_coordType, properties, ValueNameId("Y"), _intPropertyClass, 0x4, sizeof(int), Address.Zero);
        var x = AddField(_coordType, properties + 0x1000, ValueNameId("X"), _intPropertyClass, 0x0, sizeof(int), y);
        Memory.WritePointer(_coordType + ChildrenOffset, x);

        var coord = AddField(_dataClass, properties + 0x2000, ValueNameId("Coord"), structClass, CoordOffset, 0x8, Address.Zero);
        Memory.WritePointer(coord + StructTypeOffset, _coordType);

        var nameValue = AddField(_dataClass, properties + 0x3000, ValueNameId("NameValue"), nameClass, NameValueOffset, 0x8, coord);
        var next = AddField(_dataClass, properties + 0x4000, ValueNameId("StringValue"), stringClass, StringValueOffset, 0xC, nameValue);

        (string Name, int BitMask, int At)[] bools = [("bThird", 8, 0x5000), ("bSecond", 4, 0x6000), ("bFirst", 2, 0x7000)];

        foreach ((string name, int bitMask, int at) in bools)
        {
            next = AddField(_dataClass, properties + at, ValueNameId(name), boolClass, BitsOffset, sizeof(uint), next);
            Memory.WriteInt32(next + BitMaskOffset, bitMask);
        }

        // Put the value properties behind IntValue, which AddProperties made the only field of the data class.
        Memory.WritePointer(Memory.ReadPointer(_dataClass + ChildrenOffset).Unwrap() + NextOffset, next);
    }

    private void AddClass(Address type, int nameId, Address super, Address package)
    {
        Memory.AddObject(type, _objectLayout, nameId, outer: package, klass: _classClass);
        Memory.WritePointer(type + SuperFieldOffset, super);
    }

    // Adds a property as an object of its kind with 1 element and makes it the 1st field of its owner.
    private void AddProperty(Address owner, Address property, int nameId, Address kind, int offset, int elementSize)
    {
        Memory.WritePointer(owner + ChildrenOffset, AddField(owner, property, nameId, kind, offset, elementSize, Address.Zero));
    }

    // Adds a property as an object of its kind with 1 element that points at the next field.
    private Address AddField(Address owner, Address property, int nameId, Address kind, int offset, int elementSize, Address next)
    {
        Memory.AddObject(property, _objectLayout, nameId, outer: owner, klass: kind);
        Memory.WritePointer(property + NextOffset, next);
        Memory.WriteInt32(property + ArrayDimOffset, 1);
        Memory.WriteInt32(property + ElementSizeOffset, elementSize);
        Memory.WriteInt32(property + PropertyOffsetOffset, offset);

        return property;
    }
}
