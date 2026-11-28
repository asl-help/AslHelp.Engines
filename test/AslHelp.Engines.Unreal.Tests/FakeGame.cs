using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Builds and attaches to a game whose world points at a game mode with a data object that has 1 property of every value kind.
/// </summary>
/// <remarks>
///     <para>
///         <c>GameMode</c> is a property of <c>World</c>. <c>Data</c> is a property of <c>FixtureGameMode</c>, the class of the game mode,
///         and of <c>OtherGameMode</c> at another offset. <c>GameModeBase</c>, the super class of both, doesn't have it.
///     </para>
///     <para>
///         The outer object of every class is 1 of the packages <c>/Script/CoreUObject</c>, <c>/Script/Engine</c> or <c>/Script/Fixture</c>.
///         After the world class and the world, the object array lists <c>/Script/CoreUObject</c> in front of the class <c>Package</c>,
///         then the classes, the game modes, the data objects, <c>Default__FixtureData</c> and the engine. <c>GEngine</c> points at the engine, an object of <c>GameEngine</c>,
///         whose base <c>Engine</c> has <c>Data</c>. The subsystem isn't in the array until a test adds it.
///     </para>
///     <para>
///         <c>FixtureData</c>, the class of the data object, has <c>Hp</c>, <c>Speed</c> and a property per value kind, each listed with its
///         offset below. <c>Coord</c> is a struct of the type <c>FixtureCoord</c> with <c>X</c> and <c>Y</c>.
///         <c>FloatFixtureData</c> has a <c>Hp</c> that is a float, so a test can swap the data object for one with another kind.
///     </para>
/// </remarks>
internal sealed class FakeGame
{
    /// <summary>
    ///     The offset of the 3 bitfields <c>bFirst</c>, <c>bSecond</c> and <c>bThird</c> in the data object, with the masks 1, 2 and 4.
    /// </summary>
    public const int BitsOffset = 0x6A;

    /// <summary>
    ///     The value of <c>ByteValue</c>.
    /// </summary>
    public const byte ByteValue = 200;

    /// <summary>
    ///     The value of <c>Coord.X</c>.
    /// </summary>
    public const int CoordX = 7;

    /// <summary>
    ///     The value of <c>Coord.Y</c>.
    /// </summary>
    public const int CoordY = 9;

    /// <summary>
    ///     The offset of <c>Data</c> in an object of <c>FixtureGameMode</c> and in the engine.
    /// </summary>
    public const int DataOffset = 0x90;

    /// <summary>
    ///     The value of <c>DoubleValue</c>.
    /// </summary>
    public const double DoubleValue = 2.718281828;

    /// <summary>
    ///     The offset of <c>IntArray</c> in the data object, with the data pointer at 0 and the count at 8.
    /// </summary>
    public const int IntArrayOffset = 0x100;

    /// <summary>
    ///     The offset of <c>IntSet</c> in the data object, a sparse array with the slots <c>11</c>, <c>22</c>, <c>33</c> and a free slot
    ///     whose bytes still are <c>44</c>.
    /// </summary>
    public const int IntSetOffset = 0x140;

    /// <summary>
    ///     The offset of <c>Items</c> in the data object, an array of the float data object and the other data object.
    /// </summary>
    public const int ItemsOffset = 0x130;

    /// <summary>
    ///     The value of <c>Hp</c> in the data object of <c>FloatFixtureData</c>.
    /// </summary>
    public const float FloatHp = 12.5f;

    /// <summary>
    ///     The offset of <c>GameMode</c> in the world.
    /// </summary>
    public const int GameModeOffset = 0x80;

    /// <summary>
    ///     The route of <c>GUObjectArray</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const string GUObjectArrayRoute = """[{ "forward": "B1 B2 B3 ????????", "offset": 3 }, { "read": "global" }]""";

    /// <summary>
    ///     The value of <c>Hp</c> in the data object.
    /// </summary>
    public const int Hp = 995;

    /// <summary>
    ///     The offset of <c>Hp</c> in the data object.
    /// </summary>
    public const int HpOffset = 0x88;

    /// <summary>
    ///     The value of <c>Int16Value</c>.
    /// </summary>
    public const short Int16Value = -300;

    /// <summary>
    ///     The value of <c>Int64Value</c>.
    /// </summary>
    public const long Int64Value = 9_000_000_000;

    /// <summary>
    ///     The value of <c>Int8Value</c>.
    /// </summary>
    public const sbyte Int8Value = -5;

    /// <summary>
    ///     The value of <c>Kind</c>, an enum with a byte as its underlying property.
    /// </summary>
    public const byte Kind = 1;

    /// <summary>
    ///     The value of <c>LongKind</c>, an enum with an 8-byte integer as its underlying property.
    /// </summary>
    public const long LongKind = 5_000_000_000;

    /// <summary>
    ///     The text of <c>NameValue</c> with the name number 0.
    /// </summary>
    public const string NameText = "fixture-name";

    /// <summary>
    ///     The offset of <c>NameValue</c> in the data object, with the name id at 0 and the name number at 4.
    /// </summary>
    public const int NameValueOffset = 0xA0;

    /// <summary>
    ///     The offset of <c>Position</c> in the data object, a struct of the 3 floats 1, 2 and 3.
    /// </summary>
    public const int PositionOffset = 0x290;

    /// <summary>
    ///     The offset of <c>ObjectSet</c> in the data object, a set of objects with the slots of the data object,
    ///     a free slot that still points at the float data object and the other data object.
    /// </summary>
    public const int ObjectSetOffset = 0x2C0;

    /// <summary>
    ///     The offset of the number of elements in a property, <c>ArrayDim</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int PropertyArrayDimOffset = 0x30;

    /// <summary>
    ///     The offset of the size of 1 element in a property, <c>ElementSize</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int PropertyElementSizeOffset = 0x34;

    /// <summary>
    ///     The offset of the declared class in an object property, <c>PropertyClass</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int PropertyClassOffset = 0x70;

    /// <summary>
    ///     The offset of the value offset in a property, <c>Offset_Internal</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int PropertyOffsetOffset = 0x44;

    /// <summary>
    ///     The offset of the slot size in a set property, the size in the set layout of the property.
    /// </summary>
    public const int SetSlotSizeOffset = 0x78 + 0xC;

    /// <summary>
    ///     The value of <c>SubsystemValue</c> in the subsystem.
    /// </summary>
    public const int SubsystemValue = 4242;

    /// <summary>
    ///     The offset of <c>SubsystemValue</c> in an object of <c>FixtureSubsystem</c>.
    /// </summary>
    public const int SubsystemValueOffset = 0x80;

    /// <summary>
    ///     The offset of <c>GameInstance</c> in the engine object, which <c>GameEngine</c> declares like in the real engine.
    /// </summary>
    public const int GameInstanceOffset = 0x98;

    /// <summary>
    ///     The offset of <c>OwningGameInstance</c> in the world.
    /// </summary>
    public const int OwningGameInstanceOffset = 0x88;

    // The offset of the subsystem map in the game instance, the sum of the 2 offsets of SubsystemDocument.
    private const int SubsystemMapOffset = 0x48;

    /// <summary>
    ///     The value of <c>Speed</c>.
    /// </summary>
    public const float Speed = 2.5f;

    /// <summary>
    ///     The offset of the 4-byte count of <c>StringValue</c> in the data object.
    /// </summary>
    public const int StringCountOffset = StringValueOffset + 0x8;

    /// <summary>
    ///     The text of <c>StringValue</c>.
    /// </summary>
    public const string StringText = "hello-fixture";

    /// <summary>
    ///     The offset of <c>StringValue</c> in the data object, with the data pointer at 0 and the count at 8.
    /// </summary>
    public const int StringValueOffset = 0x90;

    /// <summary>
    ///     The offset of the pointer to the struct type in a struct property and to the underlying property in an enum property.
    /// </summary>
    public const int StructTypeOffset = 0x70;

    /// <summary>
    ///     The offset of the super class in a class, <c>SuperStruct</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int SuperStructOffset = 0x30;

    /// <summary>
    ///     The value of <c>UInt16Value</c>.
    /// </summary>
    public const ushort UInt16Value = 60_000;

    /// <summary>
    ///     The value of <c>UInt32Value</c>.
    /// </summary>
    public const uint UInt32Value = 4_000_000_000;

    /// <summary>
    ///     The value of <c>UInt64Value</c>.
    /// </summary>
    public const ulong UInt64Value = 18_000_000_000_000_000_000;

    /// <summary>
    ///     The offset of <c>WeakItem</c> in the data object, a weak pointer to the world with the index 1 and the serial number 77.
    /// </summary>
    public const int WeakItemOffset = 0x280;

    /// <summary>
    ///     The value of <c>WideKind</c>, an enum with a 4-byte integer as its underlying property.
    /// </summary>
    public const int WideKind = 70_000;

    /// <summary>
    ///     The code bytes in front of the operand of the <c>GEngine</c> instruction that the documents of <see cref="Document"/> search for.
    /// </summary>
    public static readonly byte[] GEngineCode = [0xE1, 0xE2, 0xE3];

    /// <summary>
    ///     The code bytes in front of the operand of the <c>GWorld</c> instruction that the documents of <see cref="Document"/> search for.
    /// </summary>
    public static readonly byte[] GWorldCode = [0xC1, 0xC2, 0xC3];

    private const int BoolByteOffsetOffset = 0x71;
    private const int BoolFieldMaskOffset = 0x73;
    private const int FieldChildPropertiesOffset = 0x50;
    private const string FieldShape = "ffield";
    private const string GEngineLocator = """{ "4.0": [{ "forward": "E1 E2 E3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const string GUObjectArrayLocator = $$"""{ "4.0": {{GUObjectArrayRoute}} }""";
    private const string GWorldLocator = """{ "4.0": [{ "forward": "C1 C2 C3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const int InnerPropertyOffset = 0x70;
    private const int MapSlotSizeOffset = 0x80 + 0x8 + 0xC;
    private const string ModuleName = "game.exe";
    private const int ModuleSize = 0x1000;
    private const string NamesLocator = """{ "4.0": [{ "forward": "A1 A2 A3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const int ObjectChildrenOffset = 0x38;
    private const int ObjObjects = 0x10;
    private const int OtherDataOffset = 0xA0;

    // Leave room for objects that a test adds behind the last item of the array.
    private const int SpareItems = 8;

    private const int ValuePropertyOffset = 0x78;

    private static readonly ChildLayout _childLayout = new(0x28);
    private static readonly NameEntryLayout _entryLayout = new(0x0, 0x10);
    private static readonly FieldClassLayout _fieldClassLayout = new(0x8);
    private static readonly FieldPropertyLayout _fieldLayout = new(0x8, 0x18, 0x20);
    private static readonly ObjectItemLayout _item = new(0x8, 0x18);
    private static readonly ItemObjectArrayLayout _itemLayout = new(0x0, 0xC);

    // List every name of the game, so a name's id is its position in the list.
    private static readonly string[] _names =
    [
        "None",
        "World",
        "GameModeBase",
        "FixtureGameMode",
        "OtherGameMode",
        "GameMode",
        "Data",
        "FixtureData",
        "Hp",
        "Speed",
        "Property",
        "IntProperty",
        "ObjectProperty",
        "FloatProperty",
        "Int8Property",
        "Int16Property",
        "Int64Property",
        "UInt16Property",
        "UInt32Property",
        "UInt64Property",
        "DoubleProperty",
        "ByteProperty",
        "BoolProperty",
        "EnumProperty",
        "NameProperty",
        "StrProperty",
        "StructProperty",
        "TextProperty",
        "Int8Value",
        "Int16Value",
        "Int64Value",
        "UInt16Value",
        "UInt32Value",
        "UInt64Value",
        "DoubleValue",
        "ByteValue",
        "BoolValue",
        "bFirst",
        "bSecond",
        "bThird",
        "bFourth",
        "Kind",
        "WideKind",
        "LongKind",
        "StringValue",
        "NameValue",
        "Coord",
        "TextValue",
        "FixtureCoord",
        "X",
        "Y",
        "UnderlyingType",
        "FloatFixtureData",
        NameText,
        "ArrayProperty",
        "SetProperty",
        "MapProperty",
        "WeakObjectProperty",
        "IntArray",
        "StringArray",
        "CoordArray",
        "Items",
        "IntSet",
        "Dict",
        "IntDict",
        "NameDict",
        "MixedDict",
        "WeakItem",
        "Position",
        "DoublePosition",
        "Mixed",
        "Vector",
        "Rotator",
        "FixtureMixed",
        "Z",
        "A",
        "B",
        "Class",
        "Package",
        "/Script/CoreUObject",
        "/Script/Engine",
        "/Script/Fixture",
        "Engine",
        "GameEngine",
        "FixtureSubsystem",
        "SubsystemValue",
        "Default__FixtureData",
        "Default__FixtureSubsystem",
        "GameInstance",
        "OwningGameInstance",
        "/Script/Late",
        "ObjectSet",
        "ObjectDict",
        "ObjectKeyDict",
        "FIXTUREDATA",
        "HP",
        "Pitch",
        "Yaw",
        "Roll",
    ];

    private readonly Address _subsystemPairs;
    private readonly Address _coordType;
    private readonly Address _coreUObjectPackage;
    private readonly Address _defaultData;
    private readonly Address _doubleVectorType;
    private readonly Address _enginePackage;
    private readonly Address _fixturePackage;
    private readonly Address _floatDataClass;
    private readonly Address _gameModeProperty;
    private readonly Address _itemArray;
    private readonly Address _items;
    private readonly Address _metaclass;
    private readonly Address _mixedType;
    private readonly Address _otherDataProperty;
    private readonly Address _otherGameModeClass;
    private readonly Dictionary<string, Address> _properties = [];
    private readonly Dictionary<string, Address> _propertyClasses = [];
    private readonly string _shape;
    private readonly Address _start;
    private readonly Address _vectorType;
    private readonly Address _worldClass;

    private int _itemCount;
    private Address _nextObject;
    private Address _nextProperty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeGame"/> class and builds the memory of the game.
    /// </summary>
    /// <param name="memoryLayout">The pointer size of the game.</param>
    /// <param name="start">The address of the module. Every other address of the game sits above it.</param>
    /// <param name="shape">The property shape, <c>uproperty</c> or <c>ffield</c>.</param>
    public FakeGame(MemoryLayout memoryLayout, Address start, string shape)
    {
        _shape = shape;
        _start = start;
        Module = new MemoryModule(ModuleName, null, new MemoryRange(start, ModuleSize));

        _worldClass = start + 0x20_1000;
        GameModeBaseClass = start + 0x22_1000;
        FixtureGameModeClass = start + 0x22_2000;
        _otherGameModeClass = start + 0x23_1000;
        FixtureDataClass = start + 0x24_1000;
        _floatDataClass = start + 0x24_2000;
        _coordType = start + 0x26_1000;
        _vectorType = start + 0x26_2000;
        _doubleVectorType = start + 0x26_3000;
        _mixedType = start + 0x26_4000;
        _items = start + 0x40_0000;
        _gameModeProperty = start + 0x60_0000;
        _otherDataProperty = start + 0x60_2000;
        _nextProperty = start + 0x62_0000;
        _metaclass = start + 0x2A_0000;
        PackageClass = start + 0x2A_1000;
        _coreUObjectPackage = start + 0x2A_2000;
        _enginePackage = start + 0x2A_3000;
        _fixturePackage = start + 0x2A_4000;
        EngineClass = start + 0x2A_5000;
        GameEngineClass = start + 0x2A_6000;
        SubsystemClass = start + 0x2A_7000;
        _defaultData = start + 0x2B_2000;
        _itemArray = start + 0x11_0000 + ObjObjects;
        _nextObject = start + 0x2C_0000;

        NameTableGlobal = start + 0x10_0000;
        GWorldSlot = start + 0x12_0000;
        GEngineSlot = start + 0x12_1000;
        Engine = start + 0x2B_0000;
        Subsystem = start + 0x2B_1000;
        GameInstance = start + 0x2B_3000;
        GameInstanceClass = start + 0x2A_8000;
        _subsystemPairs = start + 0x2B_4000;
        World = start + 0x20_0000;
        GameMode = start + 0x22_0000;
        BaseGameMode = start + 0x22_3000;
        OtherGameMode = start + 0x23_0000;
        Data = start + 0x24_0000;
        FloatData = start + 0x24_3000;
        OtherData = start + 0x25_0000;
        StringData = start + 0x27_0000;
        CollectionData = start + 0x28_0000;
        UnreadableAddress = start + 0xF0_0000;

        Memory = new FakeMemory { Layout = memoryLayout };
        AddModule(NameTableGlobal, start + 0x11_0000);
        AddNames(NameTableGlobal, start + 0x30_0000, start + 0x30_1000, start + 0x31_0000);
        AddItems();
        AddClasses(start + 0x61_2000);
        AddObjects();
    }

    /// <summary>
    ///     Gets the address of an object of <c>GameModeBase</c>, which doesn't have <c>Data</c>.
    /// </summary>
    public Address BaseGameMode { get; }

    /// <summary>
    ///     Gets the address of the elements of <c>IntArray</c>, in a block of 0x4000 bytes, with the elements of the other collections behind it.
    /// </summary>
    public Address CollectionData { get; }

    /// <summary>
    ///     Gets the address of the data object that the game mode points at.
    /// </summary>
    public Address Data { get; }

    /// <summary>
    ///     Gets the address of the engine that <c>GEngine</c> points at, an object of <c>GameEngine</c> called <c>GameEngine_0</c>.
    /// </summary>
    public Address Engine { get; }

    /// <summary>
    ///     Gets the address of <c>Engine</c>, the base of <c>GameEngine</c>.
    /// </summary>
    public Address EngineClass { get; }

    /// <summary>
    ///     Gets the address of <c>FixtureData</c>.
    /// </summary>
    public Address FixtureDataClass { get; }

    /// <summary>
    ///     Gets the address of <c>FixtureGameMode</c>.
    /// </summary>
    public Address FixtureGameModeClass { get; }

    /// <summary>
    ///     Gets the address of an object of <c>FloatFixtureData</c>, whose <c>Hp</c> is <see cref="FloatHp"/>.
    /// </summary>
    public Address FloatData { get; }

    /// <summary>
    ///     Gets the address of the game mode that the world points at, an object of <c>FixtureGameMode</c>.
    /// </summary>
    public Address GameMode { get; }

    /// <summary>
    ///     Gets the address of <c>GameEngine</c>.
    /// </summary>
    public Address GameEngineClass { get; }

    /// <summary>
    ///     Gets the address of <c>GameModeBase</c>.
    /// </summary>
    public Address GameModeBaseClass { get; }

    /// <summary>
    ///     Gets the address of the <c>GEngine</c> slot.
    /// </summary>
    public Address GEngineSlot { get; }

    /// <summary>
    ///     Gets the address of the <c>GWorld</c> slot.
    /// </summary>
    public Address GWorldSlot { get; }

    /// <summary>
    ///     Gets the address of the property <c>Hp</c>.
    /// </summary>
    public Address HpProperty => _properties["Hp"];

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public FakeMemory Memory { get; }

    /// <summary>
    ///     Gets the module that the routes of <see cref="Document"/> find the globals in.
    /// </summary>
    public MemoryModule Module { get; }

    /// <summary>
    ///     Gets the address of the name table global, which points at the name table.
    /// </summary>
    public Address NameTableGlobal { get; }

    /// <summary>
    ///     Gets the offsets inside an object.
    /// </summary>
    public static ObjectLayout ObjectLayout { get; } = new(0x20, 0x18, 0x10);

    /// <summary>
    ///     Gets the address of the data object that the other game mode points at, with <c>Hp</c> of 500.
    /// </summary>
    public Address OtherData { get; }

    /// <summary>
    ///     Gets the address of an object of <c>OtherGameMode</c>.
    /// </summary>
    public Address OtherGameMode { get; }

    /// <summary>
    ///     Gets the address of <c>Package</c>.
    /// </summary>
    public Address PackageClass { get; }

    /// <summary>
    ///     Gets the offset of the pointer to the allocation bits on the heap in a sparse array, 1 pointer before <c>NumBits</c> at 0x10 + 0x18.
    /// </summary>
    public int SparseSecondaryBitsOffset => 0x28 - Memory.Layout.PointerSize;

    /// <summary>
    ///     Gets the address of the characters of <c>StringValue</c>, in a block of 2048 characters.
    /// </summary>
    public Address StringData { get; }

    /// <summary>
    ///     Gets the address of the subsystem, an object of <c>FixtureSubsystem</c> called <c>FixtureSubsystem_0</c>
    ///     that isn't in the object array until a test adds it.
    /// </summary>
    public Address Subsystem { get; }

    /// <summary>
    ///     Gets the address of the game instance, which <c>GEngine.GameInstance</c> points at. Its subsystem map is empty until a test
    ///     writes it with <see cref="WriteSubsystemMap"/>.
    /// </summary>
    public Address GameInstance { get; }

    /// <summary>
    ///     Gets the address of <c>GameInstance</c>.
    /// </summary>
    public Address GameInstanceClass { get; }

    /// <summary>
    ///     Gets the address of <c>FixtureSubsystem</c>.
    /// </summary>
    public Address SubsystemClass { get; }

    /// <summary>
    ///     Gets an address outside every block.
    /// </summary>
    public Address UnreadableAddress { get; }

    /// <summary>
    ///     Gets the address of the world.
    /// </summary>
    public Address World { get; }

    /// <summary>
    ///     Writes a profile document with every offset that any shape and any value kind reads, so a test only picks the shapes.
    /// </summary>
    /// <param name="field">The property shape.</param>
    /// <param name="names">The name table shape.</param>
    /// <param name="array">The object array shape.</param>
    /// <returns>
    ///     The document.
    /// </returns>
    public static string Document(string field, string names, string array)
    {
        string namesGlobal = names == "chunked" ? "NamePoolData" : "NameTable";

        return $$"""
            {
              "engine": "unreal4",
              "versions": ["4.0"],
              "shapes": { "FField": "{{field}}", "FNameEntry": "{{names}}", "FUObjectArray": "{{array}}" },
              "offsets": {
                "FBoolProperty": { "ByteOffset": "0x71", "FieldMask": "0x73" },
                "FArrayProperty": { "Inner": "0x70" },
                "FChunkedFixedUObjectArray": { "Objects": "0x0", "NumElements": "0x14", "NumChunks": "0x1C", "NumElementsPerChunk": "0x10000" },
                "FEnumProperty": { "UnderlyingProp": "0x70" },
                "FField": { "ClassPrivate": "0x8", "Next": "0x18", "NamePrivate": "0x20" },
                "FFieldClass": { "Name": "0x8" },
                "FFixedUObjectArray": { "Objects": "0x0", "NumElements": "0xC" },
                "FMapProperty": { "KeyProp": "0x70", "ValueProp": "0x78", "MapLayout": "0x80" },
                "FName": { "ComparisonIndex": "0x0", "Number": "0x4" },
                "FNameEntry": { "Index": "0x0", "AnsiName": "0x10" },
                "FObjectProperty": { "PropertyClass": "0x70" },
                "FProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44" },
                "FScriptArray": { "Data": "0x0", "ArrayNum": "0x8" },
                "FScriptBitArray": { "AllocatorInstance": "0x0", "NumBits": "0x18" },
                "FScriptMap": { "Pairs": "0x0" },
                "FScriptMapLayout": { "ValueOffset": "0x4", "SetLayout": "0x8" },
                "FScriptSet": { "Elements": "0x0" },
                "FScriptSetLayout": { "Size": "0xC" },
                "FScriptSparseArray": { "Data": "0x0", "AllocationFlags": "0x10", "FirstFreeIndex": "0x30", "NumFreeIndices": "0x34" },
                "FSetProperty": { "ElementProp": "0x70", "SetLayout": "0x78" },
                "FString": { "Data": "0x0" },
                "FStructProperty": { "Struct": "0x70" },
                "FUObjectArray": { "ObjObjects": "0x10" },
                "FUObjectItem": { "Object": "0x8", "SerialNumber": "0x10", "sizeof": "0x18" },
                "FWeakObjectPtr": { "ObjectIndex": "0x0", "ObjectSerialNumber": "0x4" },
                "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>": { "Chunks": "0x0", "NumElements": "0x1000", "NumChunks": "0x1004" },
                "UArrayProperty": { "Inner": "0x70" },
                "UBoolProperty": { "ByteOffset": "0x71", "FieldMask": "0x73" },
                "UEnumProperty": { "UnderlyingProp": "0x70" },
                "UField": { "Next": "0x28" },
                "UMapProperty": { "KeyProp": "0x70", "ValueProp": "0x78", "MapLayout": "0x80" },
                "UObjectBase": { "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
                "UObjectProperty": { "PropertyClass": "0x70" },
                "UProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44" },
                "USetProperty": { "ElementProp": "0x70", "SetLayout": "0x78" },
                "UStruct": { "SuperStruct": "0x30", "Children": "0x38", "ChildProperties": "0x50" },
                "UStructProperty": { "Struct": "0x70" }
              },
              "globals": {
                "{{namesGlobal}}": {{NamesLocator}},
                "GEngine": {{GEngineLocator}},
                "GUObjectArray": {{GUObjectArrayLocator}},
                "GWorld": {{GWorldLocator}}
              }
            }
            """;
    }

    /// <summary>
    ///     Writes the instruction of each global of <see cref="Document"/> into a module at <paramref name="module"/>.
    /// </summary>
    /// <param name="memory">The memory with a block for the module.</param>
    /// <param name="module">The address of the module.</param>
    /// <param name="names">The address of the name table global.</param>
    /// <param name="gUObjectArray">The address of <c>GUObjectArray</c>.</param>
    /// <param name="gWorld">The address of the <c>GWorld</c> slot.</param>
    /// <param name="gEngine">The address of the <c>GEngine</c> slot.</param>
    public static void WriteGlobalReferences(FakeMemory memory, Address module, Address names, Address gUObjectArray, Address gWorld, Address gEngine)
    {
        memory.WriteGlobalReference(module + 0x100, [0xA1, 0xA2, 0xA3], names);
        memory.WriteGlobalReference(module + 0x200, [0xB1, 0xB2, 0xB3], gUObjectArray);
        memory.WriteGlobalReference(module + 0x300, GWorldCode, gWorld);
        memory.WriteGlobalReference(module + 0x400, GEngineCode, gEngine);
    }

    /// <summary>
    ///     Makes the object pointer of the item at <paramref name="slot"/> unreadable, like a page of the array the game freed.
    /// </summary>
    /// <param name="slot">The position of the item.</param>
    public void FailObjectSlot(int slot)
    {
        Memory.Fail(_items + ((long)slot * _item.Size) + _item.ObjectPointer);
    }

    /// <summary>
    ///     Makes the object pointer of the item that has <paramref name="obj"/> unreadable.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    public void FailObjectSlotOf(Address obj)
    {
        for (int slot = 0; slot < _itemCount; slot++)
        {
            if (Memory.ReadPointer(_items + ((long)slot * _item.Size) + _item.ObjectPointer).Unwrap() == obj)
            {
                FailObjectSlot(slot);

                return;
            }
        }
    }

    /// <summary>
    ///     Makes the pairs of the subsystem map unreadable, like when the game moved them after the map was read.
    /// </summary>
    public void FailSubsystemPairs()
    {
        Memory.Fail(_subsystemPairs);
    }

    /// <summary>
    ///     Adds <paramref name="obj"/> after the last item of the object array and raises the count, like when the game creates an object.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    public void AddObject(Address obj)
    {
        Memory.WritePointer(_items + (_itemCount * _item.Size) + _item.ObjectPointer, obj);
        WriteObjectCount(_itemCount + 1);
    }

    /// <summary>
    ///     Writes <paramref name="obj"/> into the last item of the object array without changing the count,
    ///     like when the game creates an object in the slot of a freed object.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    public void ReplaceLastObject(Address obj)
    {
        Memory.WritePointer(_items + ((_itemCount - 1) * _item.Size) + _item.ObjectPointer, obj);
    }

    /// <summary>
    ///     Creates an object of <paramref name="klass"/> without an outer object at the next free address and adds it to the object array.
    /// </summary>
    /// <param name="name">The name of the object, 1 of the names of the game.</param>
    /// <param name="klass">The class of the object.</param>
    /// <returns>
    ///     The address of the object.
    /// </returns>
    public Address CreateObject(string name, Address klass)
    {
        var obj = _nextObject;
        _nextObject += 0x1000;

        Memory.AddObject(obj, ObjectLayout, Id(name), klass: klass);
        AddObject(obj);

        return obj;
    }

    /// <summary>
    ///     Creates a class of the package <c>/Script/Fixture</c> with 4-byte int properties at 0x80, 0x84 and so on
    ///     and adds it to the object array, like when the game loads a class.
    /// </summary>
    /// <param name="name">The name of the class, 1 of the names of the game.</param>
    /// <param name="super">The super class or <see cref="Address.Zero"/>.</param>
    /// <param name="properties">The names of the properties, each 1 of the names of the game.</param>
    /// <returns>
    ///     The address of the class.
    /// </returns>
    public Address CreateClass(string name, Address super, params string[] properties)
    {
        var klass = _nextObject;
        _nextObject += 0x1000;

        AddClass(klass, name, super, _fixturePackage);
        AddObject(klass);

        if (properties.Length > 0)
        {
            var addresses = properties.Select((property, i) =>
                AddProperty(klass, name + property, property, "IntProperty", 0x80 + (i * 4), sizeof(int)));
            LinkProperties(klass, [.. addresses]);
        }

        return klass;
    }

    /// <summary>
    ///     Writes the class pointer of <paramref name="obj"/>, like when the game frees the object and creates another one at its address.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="klass">The class.</param>
    public void WriteClass(Address obj, Address klass)
    {
        Memory.WritePointer(obj + ObjectLayout.Class, klass);
    }

    /// <summary>
    ///     Writes the name of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="name">The name, 1 of the names of the game.</param>
    public void WriteName(Address obj, string name)
    {
        Memory.WriteInt32(obj + ObjectLayout.Name, Id(name));
    }

    /// <summary>
    ///     Writes the count of the object array, which can be past its items, so their read fails.
    /// </summary>
    /// <param name="count">The count.</param>
    public void WriteObjectCount(int count)
    {
        _itemCount = count;
        Memory.WriteInt32(_itemArray + _itemLayout.NumElements, count);
    }

    /// <summary>
    ///     Attaches to the game with a profile of the property shape, indirect names and a flat object array.
    /// </summary>
    /// <returns>
    ///     The game.
    /// </returns>
    public UnrealGame Attach()
    {
        return Attach(Document(_shape, "indirect", "flat"));
    }

    /// <summary>
    ///     Attaches to the game with <paramref name="document"/>, like a document of <see cref="Document"/> with a change.
    /// </summary>
    /// <param name="document">The profile document.</param>
    /// <returns>
    ///     The game.
    /// </returns>
    public UnrealGame Attach(string document)
    {
        return UnrealGame.Attach(Memory, Module, UnrealProfile.Parse(document).Unwrap()).Unwrap();
    }

    /// <summary>
    ///     Writes the subsystem map of the game instance with 1 used slot per pair, in order.
    /// </summary>
    /// <param name="pairs">The class and the subsystem of each slot.</param>
    public void WriteSubsystemMap(params (Address Class, Address Subsystem)[] pairs)
    {
        int stride = (2 * Memory.Layout.PointerSize) + (2 * sizeof(int));

        for (int i = 0; i < pairs.Length; i++)
        {
            Memory.WritePointer(_subsystemPairs + (i * stride), pairs[i].Class);
            Memory.WritePointer(_subsystemPairs + (i * stride) + Memory.Layout.PointerSize, pairs[i].Subsystem);
        }

        WriteSparse(GameInstance + SubsystemMapOffset, _subsystemPairs, pairs.Length, 0, (1u << pairs.Length) - 1);
    }

    /// <summary>
    ///     Writes the document of <paramref name="field"/> with the subsystem collection of 4.22 and later.
    /// </summary>
    /// <param name="field">The property shape.</param>
    /// <returns>
    ///     The document.
    /// </returns>
    public static string SubsystemDocument(string field)
    {
        string document = JsonEdits.Set(Document(field, "indirect", "flat"), """{ "SubsystemCollection": "0x40" }""", "offsets", "UGameInstance");

        return JsonEdits.Set(document, """{ "SubsystemMap": "0x8" }""", "offsets", "FSubsystemCollectionBase");
    }

    /// <summary>
    ///     Attaches to the game with the document of the shape and <paramref name="bounds"/>.
    /// </summary>
    /// <param name="bounds">The largest string and collection that a read takes.</param>
    /// <returns>
    ///     The game.
    /// </returns>
    public UnrealGame Attach(ValueBounds bounds)
    {
        return UnrealGame.Attach(Memory, Module, UnrealProfile.Parse(Document(_shape, "indirect", "flat")).Unwrap(), bounds).Unwrap();
    }

    /// <summary>
    ///     Gets the address of the property <paramref name="name"/> of <c>FixtureData</c>, of <c>FixtureGameMode</c> or of <c>World</c>.
    /// </summary>
    /// <param name="name">The name of the property, like <c>Coord</c>, <c>Data</c> or <c>GameMode</c>.</param>
    /// <returns>
    ///     The address.
    /// </returns>
    public Address PropertyAddress(string name)
    {
        return _properties[name];
    }

    /// <summary>
    ///     Renames <c>FixtureGameMode</c> to <c>OtherGameMode</c> and moves its <c>Data</c> to the offset of <c>OtherGameMode</c>,
    ///     like when the game frees a class and loads another one at its address.
    /// </summary>
    /// <remarks>
    ///     Points <c>Data</c> of the game mode at the new offset to <see cref="OtherData"/>.
    /// </remarks>
    public void ReplaceFixtureGameModeClass()
    {
        Memory.WriteInt32(FixtureGameModeClass + ObjectLayout.Name, Id("OtherGameMode"));
        Memory.WriteInt32(_properties["Data"] + PropertyOffsetOffset, OtherDataOffset);
        Memory.WritePointer(GameMode + OtherDataOffset, OtherData);
    }

    /// <summary>
    ///     Writes the serial number of the item at <paramref name="index"/> of the object array.
    /// </summary>
    /// <param name="index">The position of the item, 0 for the world class or 1 for the world.</param>
    /// <param name="serialNumber">The serial number.</param>
    public void WriteItemSerial(int index, int serialNumber)
    {
        Memory.WriteInt32(_items + (index * _item.Size) + 0x10, serialNumber);
    }

    /// <summary>
    ///     Writes <paramref name="text"/> into the characters of <c>StringValue</c> and sets its count to the length plus the terminator.
    /// </summary>
    /// <param name="text">The text, at most 2047 characters.</param>
    public void WriteString(string text)
    {
        Memory.WriteUtf16(StringData, text + '\0');
        Memory.WriteInt32(Data + StringCountOffset, text.Length + 1);
    }

    private static int Id(string name)
    {
        return Array.IndexOf(_names, name);
    }

    // Adds the module and the slots of GWorld and GEngine.
    private void AddModule(Address names, Address gUObjectArray)
    {
        Memory.Add(Module.Range.Start, new byte[ModuleSize]);
        WriteGlobalReferences(Memory, Module.Range.Start, names, gUObjectArray, GWorldSlot, GEngineSlot);
        Memory.AddPointers(GWorldSlot, World);
        Memory.AddPointers(GEngineSlot, Engine);
    }

    // Adds a flat array with the world class and the world 1st, so a weak pointer to index 1 reaches the world,
    // then /Script/CoreUObject in front of the class Package, the other classes and the objects.
    private void AddItems()
    {
        Address[] objects =
        [
            _worldClass,
            World,
            _coreUObjectPackage,
            _metaclass,
            PackageClass,
            _enginePackage,
            _fixturePackage,
            GameModeBaseClass,
            FixtureGameModeClass,
            _otherGameModeClass,
            FixtureDataClass,
            _floatDataClass,
            EngineClass,
            GameEngineClass,
            SubsystemClass,
            GameMode,
            BaseGameMode,
            OtherGameMode,
            Data,
            FloatData,
            OtherData,
            _defaultData,
            Engine,
            GameInstanceClass,
            GameInstance,
        ];

        _itemCount = objects.Length;
        Memory.AddItemObjectArray(_itemArray, _itemLayout, _items, _itemCount);
        Memory.AddObjectItems(_items, _item, [.. objects, .. new Address[SpareItems]]);
    }

    // Adds an indirect name table, so a name's id is its position in the list.
    private void AddNames(Address names, Address nameTable, Address nameChunk, Address entries)
    {
        var entryAddresses = Enumerable.Range(0, _names.Length).Select(id => entries + (id * 0x100)).ToArray();

        Memory.AddPointers(names, nameTable);
        Memory.AddPointers(nameTable, nameChunk);
        Memory.AddPointers(nameChunk, entryAddresses);

        for (int id = 0; id < _names.Length; id++)
        {
            Memory.AddNameEntry(entryAddresses[id], _entryLayout, id << 1, _names[id]);
        }
    }

    // Adds the classes and the struct type with their properties in the property shape.
    private void AddClasses(Address propertyClass)
    {
        Memory.AddObject(_coreUObjectPackage, ObjectLayout, Id("/Script/CoreUObject"), klass: PackageClass);
        Memory.AddObject(_enginePackage, ObjectLayout, Id("/Script/Engine"), klass: PackageClass);
        Memory.AddObject(_fixturePackage, ObjectLayout, Id("/Script/Fixture"), klass: PackageClass);
        AddClass(_metaclass, "Class", Address.Zero, _coreUObjectPackage);
        AddClass(PackageClass, "Package", Address.Zero, _coreUObjectPackage);
        AddClass(_worldClass, "World", Address.Zero, _enginePackage);
        AddClass(GameModeBaseClass, "GameModeBase", Address.Zero, _enginePackage);
        AddClass(FixtureGameModeClass, "FixtureGameMode", GameModeBaseClass, _fixturePackage);
        AddClass(_otherGameModeClass, "OtherGameMode", GameModeBaseClass, _fixturePackage);
        AddClass(FixtureDataClass, "FixtureData", Address.Zero, _fixturePackage);
        AddClass(_floatDataClass, "FloatFixtureData", Address.Zero, _fixturePackage);
        AddClass(EngineClass, "Engine", Address.Zero, _enginePackage);
        AddClass(GameEngineClass, "GameEngine", EngineClass, _enginePackage);
        AddClass(SubsystemClass, "FixtureSubsystem", Address.Zero, _fixturePackage);
        AddClass(GameInstanceClass, "GameInstance", Address.Zero, _enginePackage);
        AddStruct(_coordType, "FixtureCoord");
        AddStruct(_vectorType, "Vector");
        AddStruct(_doubleVectorType, "Rotator");
        AddStruct(_mixedType, "FixtureMixed");
        AddPropertyClasses(propertyClass);

        int pointerSize = Memory.Layout.PointerSize;

        LinkProperties(EngineClass, AddProperty(EngineClass, "EngineData", "Data", "ObjectProperty", DataOffset, pointerSize));
        LinkProperties(GameEngineClass, AddProperty(GameEngineClass, "GameInstance", "ObjectProperty", GameInstanceOffset, pointerSize));
        LinkProperties(SubsystemClass, AddProperty(SubsystemClass, "SubsystemValue", "IntProperty", SubsystemValueOffset, sizeof(int)));
        AddProperty(_worldClass, _gameModeProperty, "GameMode", "ObjectProperty", GameModeOffset, pointerSize);
        AddProperty(_otherGameModeClass, _otherDataProperty, "Data", "ObjectProperty", OtherDataOffset, pointerSize);
        LinkProperties(_worldClass, _gameModeProperty, AddProperty(_worldClass, "OwningGameInstance", "ObjectProperty", OwningGameInstanceOffset, pointerSize));
        LinkProperties(_otherGameModeClass, _otherDataProperty);
        LinkProperties(FixtureGameModeClass, AddProperty(FixtureGameModeClass, "Data", "ObjectProperty", DataOffset, pointerSize));
        _properties["GameMode"] = _gameModeProperty;
        LinkProperties(_floatDataClass, AddProperty(_floatDataClass, "FloatHp", "Hp", "FloatProperty", HpOffset, sizeof(float)));
        LinkProperties(
            _coordType,
            AddProperty(_coordType, "X", "IntProperty", 0x0, sizeof(int)),
            AddProperty(_coordType, "Y", "IntProperty", 0x4, sizeof(int)));
        LinkProperties(
            _vectorType,
            AddProperty(_vectorType, "VectorX", "X", "FloatProperty", 0x0, sizeof(float)),
            AddProperty(_vectorType, "VectorY", "Y", "FloatProperty", 0x4, sizeof(float)),
            AddProperty(_vectorType, "VectorZ", "Z", "FloatProperty", 0x8, sizeof(float)));
        LinkProperties(
            _doubleVectorType,
            AddProperty(_doubleVectorType, "Pitch", "DoubleProperty", 0x0, sizeof(double)),
            AddProperty(_doubleVectorType, "Yaw", "DoubleProperty", 0x8, sizeof(double)),
            AddProperty(_doubleVectorType, "Roll", "DoubleProperty", 0x10, sizeof(double)));
        LinkProperties(
            _mixedType,
            AddProperty(_mixedType, "A", "IntProperty", 0x0, sizeof(int)),
            AddProperty(_mixedType, "B", "FloatProperty", 0x4, sizeof(float)));
        AddDataProperties();
    }

    // Adds the classes of the property kinds as field classes or as classes that derive from Property.
    private void AddPropertyClasses(Address propertyClass)
    {
        if (_shape != FieldShape)
        {
            AddStruct(propertyClass, "Property");
        }

        var klass = _start + 0x70_0000;

        foreach (string className in _names.Where(static name => name.EndsWith("Property", StringComparison.Ordinal) && name != "Property"))
        {
            if (_shape == FieldShape)
            {
                Memory.AddFieldClass(klass, _fieldClassLayout, Id(className));
            }
            else
            {
                AddStruct(klass, className);
                Memory.WritePointer(klass + SuperStructOffset, propertyClass);
            }

            _propertyClasses[className] = klass;
            klass += 0x1000;
        }
    }

    // Adds the properties of FixtureData in chain order, with the members that the kinds read.
    private void AddDataProperties()
    {
        Address[] properties =
        [
            AddProperty(FixtureDataClass, "Hp", "IntProperty", HpOffset, sizeof(int)),
            AddProperty(FixtureDataClass, "Speed", "FloatProperty", 0x8C, sizeof(float)),
            AddProperty(FixtureDataClass, "Int8Value", "Int8Property", 0x40, sizeof(sbyte)),
            AddProperty(FixtureDataClass, "Int16Value", "Int16Property", 0x42, sizeof(short)),
            AddProperty(FixtureDataClass, "UInt16Value", "UInt16Property", 0x44, sizeof(ushort)),
            AddProperty(FixtureDataClass, "UInt32Value", "UInt32Property", 0x48, sizeof(uint)),
            AddProperty(FixtureDataClass, "Int64Value", "Int64Property", 0x50, sizeof(long)),
            AddProperty(FixtureDataClass, "UInt64Value", "UInt64Property", 0x58, sizeof(ulong)),
            AddProperty(FixtureDataClass, "DoubleValue", "DoubleProperty", 0x60, sizeof(double)),
            AddProperty(FixtureDataClass, "ByteValue", "ByteProperty", 0x68, sizeof(byte)),
            AddBool("BoolValue", 0x69, 0, 0xFF),
            AddBool("bFirst", BitsOffset, 0, 0x1),
            AddBool("bSecond", BitsOffset, 0, 0x2),
            AddBool("bThird", BitsOffset, 0, 0x4),
            AddBool("bFourth", BitsOffset, 1, 0x10),
            AddEnum("Kind", 0x6C, "ByteProperty", sizeof(byte)),
            AddEnum("WideKind", 0x70, "IntProperty", sizeof(int)),
            AddEnum("LongKind", 0x78, "Int64Property", sizeof(long)),
            AddProperty(FixtureDataClass, "StringValue", "StrProperty", StringValueOffset, 0x10),
            AddProperty(FixtureDataClass, "NameValue", "NameProperty", NameValueOffset, 0x8),
            AddProperty(FixtureDataClass, "Coord", "StructProperty", 0xB0, 0x8),
            AddProperty(FixtureDataClass, "TextValue", "TextProperty", 0xB8, 0x18),
            AddArray("IntArray", IntArrayOffset, "IntProperty", sizeof(int)),
            AddArray("StringArray", 0x110, "StrProperty", 0x10),
            AddArray("CoordArray", 0x120, "StructProperty", 0x8),
            AddArray("Items", ItemsOffset, "ObjectProperty", Memory.Layout.PointerSize),
            AddSet("IntSet", IntSetOffset, "IntProperty", sizeof(int), 0xC),
            AddMap("Dict", 0x180, ("StrProperty", 0x10), ("IntProperty", 0x10, sizeof(int)), 0x20),
            AddMap("IntDict", 0x1C0, ("IntProperty", sizeof(int)), ("IntProperty", 0x4, sizeof(int)), 0x10),
            AddMap("NameDict", 0x200, ("NameProperty", 0x8), ("IntProperty", 0x8, sizeof(int)), 0x10),
            AddMap("MixedDict", 0x240, ("IntProperty", sizeof(int)), ("StructProperty", 0x4, 0x8), 0x14),
            AddProperty(FixtureDataClass, "WeakItem", "WeakObjectProperty", WeakItemOffset, 0x8),
            AddProperty(FixtureDataClass, "Position", "StructProperty", PositionOffset, 0xC),
            AddProperty(FixtureDataClass, "DoublePosition", "StructProperty", 0x2A0, 0x18),
            AddProperty(FixtureDataClass, "Mixed", "StructProperty", 0x2B8, 0x8),
            AddSet("ObjectSet", ObjectSetOffset, "ObjectProperty", Memory.Layout.PointerSize, 0x10),
            AddMap("ObjectDict", 0x300, ("StrProperty", 0x10), ("ObjectProperty", 0x10, Memory.Layout.PointerSize), 0x20),
            AddMap("ObjectKeyDict", 0x340, ("ObjectProperty", Memory.Layout.PointerSize), ("IntProperty", 0x8, sizeof(int)), 0x10),
        ];

        Memory.WritePointer(_properties["Coord"] + StructTypeOffset, _coordType);
        Memory.WritePointer(_properties["CoordArrayInner"] + StructTypeOffset, _coordType);
        Memory.WritePointer(_properties["MixedDictValue"] + StructTypeOffset, _mixedType);
        Memory.WritePointer(_properties["Position"] + StructTypeOffset, _vectorType);
        Memory.WritePointer(_properties["DoublePosition"] + StructTypeOffset, _doubleVectorType);
        Memory.WritePointer(_properties["Mixed"] + StructTypeOffset, _mixedType);
        LinkProperties(FixtureDataClass, properties);

        // Declare the object properties like the engine, with the base GameModeBase for GameMode, so only the derived class has Data.
        Memory.WritePointer(_gameModeProperty + PropertyClassOffset, GameModeBaseClass);

        foreach (string key in (string[])["Data", "EngineData", "ItemsInner", "ObjectSetElement", "ObjectDictValue", "ObjectKeyDictKey"])
        {
            Memory.WritePointer(_properties[key] + PropertyClassOffset, FixtureDataClass);
        }
    }

    // Adds an array property and its inner property, which has the name of the array like in the engine.
    private Address AddArray(string name, int offset, string innerClass, int elementSize)
    {
        var property = AddProperty(FixtureDataClass, name, "ArrayProperty", offset, 0x10);
        Memory.WritePointer(property + InnerPropertyOffset, AddProperty(property, name + "Inner", name, innerClass, 0x0, elementSize));

        return property;
    }

    // Adds a set property, its element property and the slot size in its set layout.
    private Address AddSet(string name, int offset, string elementClass, int elementSize, int slotSize)
    {
        var property = AddProperty(FixtureDataClass, name, "SetProperty", offset, 0x38);
        Memory.WritePointer(property + InnerPropertyOffset, AddProperty(property, name + "Element", name, elementClass, 0x0, elementSize));
        Memory.WriteInt32(property + SetSlotSizeOffset, slotSize);

        return property;
    }

    // Adds a map property, its key property at offset 0, its value property at the offset of the value in a pair and the slot size.
    private Address AddMap(string name, int offset, (string Class, int Size) key, (string Class, int Offset, int Size) value, int slotSize)
    {
        var property = AddProperty(FixtureDataClass, name, "MapProperty", offset, 0x38);
        Memory.WritePointer(property + InnerPropertyOffset, AddProperty(property, name + "Key", name, key.Class, 0x0, key.Size));
        Memory.WritePointer(property + ValuePropertyOffset, AddProperty(property, name + "Value", name, value.Class, value.Offset, value.Size));
        Memory.WriteInt32(property + MapSlotSizeOffset, slotSize);

        return property;
    }

    // Adds a bool property with its byte offset and field mask.
    private Address AddBool(string name, int offset, byte byteOffset, byte fieldMask)
    {
        var property = AddProperty(FixtureDataClass, name, "BoolProperty", offset, sizeof(bool));
        Memory.Write(property + BoolByteOffsetOffset, byteOffset);
        Memory.Write(property + BoolFieldMaskOffset, fieldMask);

        return property;
    }

    // Adds an enum property and its underlying property, which doesn't sit on any chain.
    private Address AddEnum(string name, int offset, string underlyingClass, int elementSize)
    {
        var property = AddProperty(FixtureDataClass, name, "EnumProperty", offset, elementSize);
        var underlying = AddProperty(property, name + "Underlying", "UnderlyingType", underlyingClass, 0x0, elementSize);
        Memory.WritePointer(property + StructTypeOffset, underlying);

        return property;
    }

    // Adds the world, the game modes and the data objects with the pointers between them and the values of every kind.
    private void AddObjects()
    {
        Memory.AddObject(World, ObjectLayout, Id("World"), klass: _worldClass);
        Memory.AddObject(GameMode, ObjectLayout, Id("GameMode"), klass: FixtureGameModeClass);
        Memory.AddObject(BaseGameMode, ObjectLayout, Id("GameMode"), klass: GameModeBaseClass);
        Memory.AddObject(OtherGameMode, ObjectLayout, Id("GameMode"), klass: _otherGameModeClass);
        Memory.AddObject(Data, ObjectLayout, Id("Data"), klass: FixtureDataClass);
        Memory.AddObject(FloatData, ObjectLayout, Id("Data"), klass: _floatDataClass);
        Memory.AddObject(OtherData, ObjectLayout, Id("Data"), klass: FixtureDataClass);
        Memory.AddObject(_defaultData, ObjectLayout, Id("Default__FixtureData"), klass: FixtureDataClass);
        Memory.AddObject(Engine, ObjectLayout, Id("GameEngine"), number: 1, klass: GameEngineClass);
        Memory.AddObject(Subsystem, ObjectLayout, Id("FixtureSubsystem"), number: 1, klass: SubsystemClass);
        Memory.AddObject(GameInstance, ObjectLayout, Id("GameInstance"), klass: GameInstanceClass);
        Memory.Add(_subsystemPairs, new byte[0x1000]);
        Memory.Add(StringData, new byte[0x1000]);

        Memory.WritePointer(Engine + DataOffset, Data);
        Memory.WritePointer(Engine + GameInstanceOffset, GameInstance);
        WriteSubsystemMap();
        Memory.WriteInt32(Subsystem + SubsystemValueOffset, SubsystemValue);
        Memory.WritePointer(World + GameModeOffset, GameMode);
        Memory.WritePointer(GameMode + DataOffset, Data);
        Memory.WritePointer(OtherGameMode + OtherDataOffset, OtherData);
        Memory.WriteInt32(OtherData + HpOffset, 500);
        Memory.Write(FloatData + HpOffset, FloatHp);

        Memory.Write(Data + 0x40, Int8Value);
        Memory.Write(Data + 0x42, Int16Value);
        Memory.Write(Data + 0x44, UInt16Value);
        Memory.Write(Data + 0x48, UInt32Value);
        Memory.Write(Data + 0x50, Int64Value);
        Memory.Write(Data + 0x58, UInt64Value);
        Memory.Write(Data + 0x60, DoubleValue);
        Memory.Write(Data + 0x68, ByteValue);
        Memory.Write<byte>(Data + 0x69, 1);

        // Set bFirst and bThird and leave bSecond, then set bFourth in the next byte and leave its mask clear in the 1st byte.
        Memory.Write<byte>(Data + BitsOffset, 0b101);
        Memory.Write<byte>(Data + BitsOffset + 1, 0x10);
        Memory.Write(Data + 0x6C, Kind);
        Memory.Write(Data + 0x70, WideKind);
        Memory.Write(Data + 0x78, LongKind);
        Memory.WriteInt32(Data + HpOffset, Hp);
        Memory.Write(Data + 0x8C, Speed);
        Memory.WritePointer(Data + StringValueOffset, StringData);
        WriteString(StringText);
        Memory.WriteInt32(Data + NameValueOffset, Id(NameText));
        Memory.WriteInt32(Data + 0xB0, CoordX);
        Memory.WriteInt32(Data + 0xB4, CoordY);
        AddCollections();
    }

    // Adds the members of the data object past its 1st 0x100 bytes and the elements of the collections behind CollectionData.
    private void AddCollections()
    {
        Memory.Add(Data + 0x100, new byte[0x300]);
        Memory.Add(CollectionData, new byte[0x8000]);

        WriteArray(IntArrayOffset, CollectionData, 3);
        WriteInts(CollectionData, 100, 200, 300);

        var strings = CollectionData + 0x4000;
        WriteArray(0x110, strings, 3);

        for (int i = 0; i < 3; i++)
        {
            var chars = strings + 0x100 + (i * 0x10);
            Memory.WriteUtf16(chars, $"{(char)('a' + i)}\0");
            Memory.WritePointer(strings + (i * 0x10), chars);
            Memory.WriteInt32(strings + (i * 0x10) + 0x8, 2);
        }

        WriteArray(0x120, CollectionData + 0x4200, 2);
        WriteInts(CollectionData + 0x4200, 1, 2, 3, 4);

        WriteArray(ItemsOffset, CollectionData + 0x4300, 2);
        Memory.WritePointer(CollectionData + 0x4300, FloatData);
        Memory.WritePointer(CollectionData + 0x4300 + Memory.Layout.PointerSize, OtherData);

        // Leave the 4th slot free with its old bytes, so a lookup has to skip it by its bit.
        var setSlots = CollectionData + 0x4400;
        WriteSparse(IntSetOffset, setSlots, 4, 1, 0b0111);

        for (int i = 0; i < 4; i++)
        {
            Memory.WriteInt32(setSlots + (i * 0xC), (i + 1) * 11);
        }

        var dictPairs = CollectionData + 0x4500;
        WriteSparse(0x180, dictPairs, 2, 0, 0b11);
        WriteStringKey(dictPairs, CollectionData + 0x4600, "one", 1);
        WriteStringKey(dictPairs + 0x20, CollectionData + 0x4610, "two", 2);

        var intDictPairs = CollectionData + 0x4700;
        WriteSparse(0x1C0, intDictPairs, 3, 0, 0b111);
        WriteInts(intDictPairs, 4, 40, 0, 0, 5, 50, 0, 0, 6, 60);

        var nameDictPairs = CollectionData + 0x4800;
        WriteSparse(0x200, nameDictPairs, 2, 0, 0b11);
        WriteInts(nameDictPairs, Id(NameText), 0, 7, 0, Id("Hp"), 5, 8);

        var mixedDictPairs = CollectionData + 0x4900;
        WriteSparse(0x240, mixedDictPairs, 1, 0, 0b1);
        WriteInts(mixedDictPairs, 5, 5);
        Memory.Write(mixedDictPairs + 0x8, 6f);

        // Leave the 2nd slot free with a pointer to the float data object, so a position has to skip it by its bit.
        var objectSetSlots = CollectionData + 0x4A00;
        WriteSparse(ObjectSetOffset, objectSetSlots, 3, 1, 0b101);
        Memory.WritePointer(objectSetSlots, Data);
        Memory.WritePointer(objectSetSlots + 0x10, FloatData);
        Memory.WritePointer(objectSetSlots + 0x20, OtherData);

        var objectDictPairs = CollectionData + 0x4B00;
        WriteSparse(0x300, objectDictPairs, 2, 0, 0b11);
        WriteStringKey(objectDictPairs, CollectionData + 0x4C00, "data", 0);
        WriteStringKey(objectDictPairs + 0x20, CollectionData + 0x4C10, "other", 0);
        Memory.WritePointer(objectDictPairs + 0x10, Data);
        Memory.WritePointer(objectDictPairs + 0x30, OtherData);

        var objectKeyDictPairs = CollectionData + 0x4D00;
        WriteSparse(0x340, objectKeyDictPairs, 3, 1, 0b101);
        Memory.WritePointer(objectKeyDictPairs, Data);
        Memory.WriteInt32(objectKeyDictPairs + 0x8, 1);
        Memory.WritePointer(objectKeyDictPairs + 0x10, FloatData);
        Memory.WriteInt32(objectKeyDictPairs + 0x18, 9);
        Memory.WritePointer(objectKeyDictPairs + 0x20, OtherData);
        Memory.WriteInt32(objectKeyDictPairs + 0x28, 2);

        Memory.WriteInt32(Data + WeakItemOffset, 1);
        Memory.WriteInt32(Data + WeakItemOffset + 4, 77);
        WriteItemSerial(0, 76);
        WriteItemSerial(1, 77);

        Memory.Write(Data + PositionOffset, 1f);
        Memory.Write(Data + PositionOffset + 4, 2f);
        Memory.Write(Data + PositionOffset + 8, 3f);
        Memory.Write(Data + 0x2A0, 1.5);
        Memory.Write(Data + 0x2A8, 2.5);
        Memory.Write(Data + 0x2B0, 3.5);
        Memory.WriteInt32(Data + 0x2B8, 1);
        Memory.Write(Data + 0x2BC, 2f);
    }

    // Writes the data pointer and the count of an array member of the data object.
    private void WriteArray(int offset, Address data, int count)
    {
        Memory.WritePointer(Data + offset, data);
        Memory.WriteInt32(Data + offset + 0x8, count);
    }

    private void WriteInts(Address start, params int[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            Memory.WriteInt32(start + (i * sizeof(int)), values[i]);
        }
    }

    // Writes the header of a sparse array member of the data object with its allocation bits inside it.
    private void WriteSparse(int offset, Address slots, int slotCount, int freeCount, uint bits)
    {
        WriteSparse(Data + offset, slots, slotCount, freeCount, bits);
    }

    // Writes the header of a sparse array with its allocation bits inside it.
    private void WriteSparse(Address sparse, Address slots, int slotCount, int freeCount, uint bits)
    {
        Memory.WritePointer(sparse, slots);
        Memory.WriteInt32(sparse + 0x8, slotCount);
        Memory.Write(sparse + 0x10, bits);
        Memory.WritePointer(sparse + SparseSecondaryBitsOffset, Address.Zero);
        Memory.WriteInt32(sparse + 0x34, freeCount);
    }

    // Writes a pair of a string key and a 4-byte value at offset 0x10.
    private void WriteStringKey(Address pair, Address chars, string key, int value)
    {
        Memory.WriteUtf16(chars, key + '\0');
        Memory.WritePointer(pair, chars);
        Memory.WriteInt32(pair + 0x8, key.Length + 1);
        Memory.WriteInt32(pair + 0x10, value);
    }

    // Adds a class as an object of Class with its package as the outer object.
    private void AddClass(Address type, string name, Address super, Address package)
    {
        Memory.AddObject(type, ObjectLayout, Id(name), outer: package, klass: _metaclass);
        Memory.WritePointer(type + SuperStructOffset, super);
    }

    // Adds a struct type or a property class, which isn't in the object array.
    private void AddStruct(Address type, string name)
    {
        Memory.AddObject(type, ObjectLayout, Id(name));
    }

    // Adds a property of a class at the next free address and keeps it by name.
    private Address AddProperty(Address owner, string name, string className, int offset, int elementSize)
    {
        return AddProperty(owner, name, name, className, offset, elementSize);
    }

    // Adds a property at the next free address with the name it's printed with and keeps it by key.
    private Address AddProperty(Address owner, string key, string name, string className, int offset, int elementSize)
    {
        var property = _nextProperty;
        _nextProperty += 0x1000;

        AddProperty(owner, property, name, className, offset, elementSize);
        _properties[key] = property;

        return property;
    }

    // Adds a property in the property shape with the class of its kind.
    private void AddProperty(Address owner, Address property, string name, string className, int offset, int elementSize)
    {
        if (_shape == FieldShape)
        {
            Memory.AddField(property, _fieldLayout, Id(name), _propertyClasses[className]);
        }
        else
        {
            Memory.AddObject(property, ObjectLayout, Id(name), outer: owner, klass: _propertyClasses[className]);
        }

        Memory.WriteInt32(property + PropertyArrayDimOffset, 1);
        Memory.WriteInt32(property + PropertyElementSizeOffset, elementSize);
        Memory.WriteInt32(property + PropertyOffsetOffset, offset);
    }

    // Points the struct at its 1st property and each property at the next one in the property shape.
    private void LinkProperties(Address owner, params Address[] properties)
    {
        int first = _shape == FieldShape ? FieldChildPropertiesOffset : ObjectChildrenOffset;
        int next = _shape == FieldShape ? _fieldLayout.Next : _childLayout.Next;

        Memory.WritePointer(owner + first, properties[0]);

        for (int i = 0; i < properties.Length; i++)
        {
            Memory.WritePointer(properties[i] + next, i + 1 < properties.Length ? properties[i + 1] : Address.Zero);
        }
    }
}
