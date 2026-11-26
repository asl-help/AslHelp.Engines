using AslHelp.Memory;
using System.Linq;

namespace AslHelp.Engines.Unreal.Tests;

/// <summary>
///     Builds and attaches to a game whose world points at a game mode with a data object that has <c>Hp</c> and <c>Speed</c>.
/// </summary>
/// <remarks>
///     <c>GameMode</c> is a property of <c>World</c>. <c>Data</c> is a property of <c>FixtureGameMode</c>, the class of the game mode,
///     and of <c>OtherGameMode</c> at another offset. <c>GameModeBase</c>, the super class of both, doesn't have it.
/// </remarks>
internal sealed class FakeGame
{
    /// <summary>
    ///     The offset of <c>Data</c> in an object of <c>FixtureGameMode</c>.
    /// </summary>
    public const int DataOffset = 0x90;

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
    ///     The offset of the value offset in a property, <c>Offset_Internal</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int PropertyOffsetOffset = 0x44;

    /// <summary>
    ///     The offset of the super class in a class, <c>SuperStruct</c> in the documents of <see cref="Document"/>.
    /// </summary>
    public const int SuperStructOffset = 0x30;

    /// <summary>
    ///     The code bytes in front of the operand of the <c>GWorld</c> instruction that the documents of <see cref="Document"/> search for.
    /// </summary>
    public static readonly byte[] GWorldCode = [0xC1, 0xC2, 0xC3];

    private const int DataId = 6;
    private const int FieldChildPropertiesOffset = 0x50;
    private const string FieldShape = "ffield";
    private const int FixtureDataId = 7;
    private const int FixtureGameModeId = 3;
    private const int FloatPropertyId = 13;
    private const int GameModeBaseId = 2;
    private const int GameModeId = 5;
    private const string GUObjectArrayLocator = $$"""{ "4.0": {{GUObjectArrayRoute}} }""";
    private const string GWorldLocator = """{ "4.0": [{ "forward": "C1 C2 C3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const int HpId = 8;
    private const int IntPropertyId = 11;
    private const string ModuleName = "game.exe";
    private const int ModuleSize = 0x1000;
    private const string NamesLocator = """{ "4.0": [{ "forward": "A1 A2 A3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const int ObjectChildrenOffset = 0x38;
    private const int ObjectPropertyId = 12;
    private const int ObjObjects = 0x10;
    private const int OtherDataOffset = 0xA0;
    private const int OtherGameModeId = 4;
    private const int PropertyElementSizeOffset = 0x34;
    private const int PropertyId = 10;
    private const int SpeedId = 9;
    private const int SpeedOffset = 0x8C;
    private const int WorldId = 1;

    private static readonly ChildLayout _childLayout = new(0x28);
    private static readonly NameEntryLayout _entryLayout = new(0x0, 0x10);
    private static readonly FieldClassLayout _fieldClassLayout = new(0x8);
    private static readonly FieldPropertyLayout _fieldLayout = new(0x8, 0x18, 0x20);
    private static readonly ObjectItemLayout _item = new(0x8, 0x18);
    private static readonly ItemObjectArrayLayout _itemLayout = new(0x0, 0xC);
    private static readonly ObjectLayout _objectLayout = new(0x20, 0x18, 0x10);

    private readonly Address _dataClass;
    private readonly Address _dataProperty;
    private readonly Address _floatPropertyClass;
    private readonly Address _gameModeProperty;
    private readonly Address _intPropertyClass;
    private readonly MemoryModule _module;
    private readonly Address _objectPropertyClass;
    private readonly Address _otherDataProperty;
    private readonly Address _otherGameModeClass;
    private readonly string _shape;
    private readonly Address _speedProperty;
    private readonly Address _worldClass;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeGame"/> class and builds the memory of the game.
    /// </summary>
    /// <param name="memoryLayout">The pointer size of the game.</param>
    /// <param name="start">The address of the module. Every other address of the game sits above it.</param>
    /// <param name="shape">The property shape, <c>uproperty</c> or <c>ffield</c>.</param>
    public FakeGame(MemoryLayout memoryLayout, Address start, string shape)
    {
        _shape = shape;
        _module = new MemoryModule(ModuleName, null, new MemoryRange(start, ModuleSize));

        _worldClass = start + 0x20_1000;
        GameModeBaseClass = start + 0x22_1000;
        FixtureGameModeClass = start + 0x22_2000;
        _otherGameModeClass = start + 0x23_1000;
        _dataClass = start + 0x24_1000;
        _intPropertyClass = start + 0x61_0000;
        _objectPropertyClass = start + 0x61_1000;
        _floatPropertyClass = start + 0x61_3000;
        _gameModeProperty = start + 0x60_0000;
        _dataProperty = start + 0x60_1000;
        _otherDataProperty = start + 0x60_2000;
        HpProperty = start + 0x60_3000;
        _speedProperty = start + 0x60_4000;

        GWorldSlot = start + 0x12_0000;
        World = start + 0x20_0000;
        GameMode = start + 0x22_0000;
        BaseGameMode = start + 0x22_3000;
        OtherGameMode = start + 0x23_0000;
        Data = start + 0x24_0000;
        OtherData = start + 0x25_0000;
        UnreadableAddress = start + 0xF0_0000;

        Memory = new FakeMemory { Layout = memoryLayout };
        AddModule(start + 0x10_0000, start + 0x11_0000, start + 0x40_0000);
        AddNames(start + 0x10_0000, start + 0x30_0000, start + 0x30_1000, start + 0x31_0000);
        AddClasses(start + 0x61_2000);
        AddObjects();
    }

    /// <summary>
    ///     Gets the address of an object of <c>GameModeBase</c>, which doesn't have <c>Data</c>.
    /// </summary>
    public Address BaseGameMode { get; }

    /// <summary>
    ///     Gets the address of the data object that the game mode points at.
    /// </summary>
    public Address Data { get; }

    /// <summary>
    ///     Gets the address of <c>FixtureGameMode</c>.
    /// </summary>
    public Address FixtureGameModeClass { get; }

    /// <summary>
    ///     Gets the address of the game mode that the world points at, an object of <c>FixtureGameMode</c>.
    /// </summary>
    public Address GameMode { get; }

    /// <summary>
    ///     Gets the address of <c>GameModeBase</c>.
    /// </summary>
    public Address GameModeBaseClass { get; }

    /// <summary>
    ///     Gets the address of the <c>GWorld</c> slot.
    /// </summary>
    public Address GWorldSlot { get; }

    /// <summary>
    ///     Gets the address of the property <c>Hp</c>.
    /// </summary>
    public Address HpProperty { get; }

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public FakeMemory Memory { get; }

    /// <summary>
    ///     Gets the address of the data object that the other game mode points at, with <c>Hp</c> of 500.
    /// </summary>
    public Address OtherData { get; }

    /// <summary>
    ///     Gets the address of an object of <c>OtherGameMode</c>.
    /// </summary>
    public Address OtherGameMode { get; }

    /// <summary>
    ///     Gets an address outside every block.
    /// </summary>
    public Address UnreadableAddress { get; }

    /// <summary>
    ///     Gets the address of the world.
    /// </summary>
    public Address World { get; }

    /// <summary>
    ///     Writes a profile document with every offset that any shape reads, so a test only picks the shapes.
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
              "versions": ["4.0"],
              "shapes": { "FField": "{{field}}", "FNameEntry": "{{names}}", "FUObjectArray": "{{array}}" },
              "offsets": {
                "FChunkedFixedUObjectArray": { "Objects": "0x0", "NumElements": "0x14", "NumChunks": "0x1C", "NumElementsPerChunk": "0x10000" },
                "FField": { "ClassPrivate": "0x8", "Next": "0x18", "NamePrivate": "0x20" },
                "FFieldClass": { "Name": "0x8" },
                "FFixedUObjectArray": { "Objects": "0x0", "NumElements": "0xC" },
                "FNameEntry": { "Index": "0x0", "AnsiName": "0x10" },
                "FProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44" },
                "FUObjectArray": { "ObjObjects": "0x10" },
                "FUObjectItem": { "Object": "0x8", "sizeof": "0x18" },
                "TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>": { "Chunks": "0x0", "NumElements": "0x1000", "NumChunks": "0x1004" },
                "UField": { "Next": "0x28" },
                "UObjectBase": { "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
                "UProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44" },
                "UStruct": { "SuperStruct": "0x30", "Children": "0x38", "ChildProperties": "0x50" }
              },
              "globals": {
                "{{namesGlobal}}": {{NamesLocator}},
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
    public static void WriteGlobalReferences(FakeMemory memory, Address module, Address names, Address gUObjectArray, Address gWorld)
    {
        memory.WriteGlobalReference(module + 0x100, [0xA1, 0xA2, 0xA3], names);
        memory.WriteGlobalReference(module + 0x200, [0xB1, 0xB2, 0xB3], gUObjectArray);
        memory.WriteGlobalReference(module + 0x300, GWorldCode, gWorld);
    }

    /// <summary>
    ///     Attaches to the game with a profile of the property shape, indirect names and a flat object array.
    /// </summary>
    /// <returns>
    ///     The game.
    /// </returns>
    public UnrealGame Attach()
    {
        var profile = UnrealProfile.Parse(Document(_shape, "indirect", "flat")).Unwrap();

        return UnrealGame.Attach(Memory, _module, profile).Unwrap();
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
        Memory.WriteInt32(FixtureGameModeClass + _objectLayout.Name, OtherGameModeId);
        Memory.WriteInt32(_dataProperty + PropertyOffsetOffset, OtherDataOffset);
        Memory.WritePointer(GameMode + OtherDataOffset, OtherData);
    }

    // Adds the module, the GWorld slot and a flat array of the world class and the world.
    private void AddModule(Address names, Address gUObjectArray, Address items)
    {
        Memory.Add(_module.Range.Start, new byte[ModuleSize]);
        WriteGlobalReferences(Memory, _module.Range.Start, names, gUObjectArray, GWorldSlot);
        Memory.AddPointers(GWorldSlot, World);
        Memory.AddItemObjectArray(gUObjectArray + ObjObjects, _itemLayout, items, 2);
        Memory.AddObjectItems(items, _item, _worldClass, World);
    }

    // Adds an indirect name table, so a name's id is its position in the list.
    private void AddNames(Address names, Address nameTable, Address nameChunk, Address entries)
    {
        string[] texts =
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
        ];
        var entryAddresses = Enumerable.Range(0, texts.Length).Select(id => entries + (id * 0x100)).ToArray();

        Memory.AddPointers(names, nameTable);
        Memory.AddPointers(nameTable, nameChunk);
        Memory.AddPointers(nameChunk, entryAddresses);

        for (int id = 0; id < texts.Length; id++)
        {
            Memory.AddNameEntry(entryAddresses[id], _entryLayout, id << 1, texts[id]);
        }
    }

    // Adds the classes with their properties in the property shape.
    private void AddClasses(Address propertyClass)
    {
        AddClass(_worldClass, WorldId, Address.Zero);
        AddClass(GameModeBaseClass, GameModeBaseId, Address.Zero);
        AddClass(FixtureGameModeClass, FixtureGameModeId, GameModeBaseClass);
        AddClass(_otherGameModeClass, OtherGameModeId, GameModeBaseClass);
        AddClass(_dataClass, FixtureDataId, Address.Zero);

        if (_shape == FieldShape)
        {
            Memory.AddFieldClass(_intPropertyClass, _fieldClassLayout, IntPropertyId);
            Memory.AddFieldClass(_objectPropertyClass, _fieldClassLayout, ObjectPropertyId);
            Memory.AddFieldClass(_floatPropertyClass, _fieldClassLayout, FloatPropertyId);
        }
        else
        {
            AddClass(propertyClass, PropertyId, Address.Zero);
            AddClass(_intPropertyClass, IntPropertyId, propertyClass);
            AddClass(_objectPropertyClass, ObjectPropertyId, propertyClass);
            AddClass(_floatPropertyClass, FloatPropertyId, propertyClass);
        }

        int pointerSize = Memory.Layout.PointerSize;

        AddProperty(_worldClass, _gameModeProperty, GameModeId, _objectPropertyClass, GameModeOffset, pointerSize, Address.Zero);
        AddProperty(FixtureGameModeClass, _dataProperty, DataId, _objectPropertyClass, DataOffset, pointerSize, Address.Zero);
        AddProperty(_otherGameModeClass, _otherDataProperty, DataId, _objectPropertyClass, OtherDataOffset, pointerSize, Address.Zero);
        AddProperty(_dataClass, HpProperty, HpId, _intPropertyClass, HpOffset, sizeof(int), _speedProperty);
        AddProperty(_dataClass, _speedProperty, SpeedId, _floatPropertyClass, SpeedOffset, sizeof(float), Address.Zero);

        int first = _shape == FieldShape ? FieldChildPropertiesOffset : ObjectChildrenOffset;

        Memory.WritePointer(_worldClass + first, _gameModeProperty);
        Memory.WritePointer(FixtureGameModeClass + first, _dataProperty);
        Memory.WritePointer(_otherGameModeClass + first, _otherDataProperty);
        Memory.WritePointer(_dataClass + first, HpProperty);
    }

    // Adds the world, the game modes and the data objects with the pointers between them.
    private void AddObjects()
    {
        Memory.AddObject(World, _objectLayout, WorldId, klass: _worldClass);
        Memory.AddObject(GameMode, _objectLayout, GameModeId, klass: FixtureGameModeClass);
        Memory.AddObject(BaseGameMode, _objectLayout, GameModeId, klass: GameModeBaseClass);
        Memory.AddObject(OtherGameMode, _objectLayout, GameModeId, klass: _otherGameModeClass);
        Memory.AddObject(Data, _objectLayout, DataId, klass: _dataClass);
        Memory.AddObject(OtherData, _objectLayout, DataId, klass: _dataClass);

        Memory.WritePointer(World + GameModeOffset, GameMode);
        Memory.WritePointer(GameMode + DataOffset, Data);
        Memory.WritePointer(OtherGameMode + OtherDataOffset, OtherData);
        Memory.WriteInt32(Data + HpOffset, Hp);
        Memory.WriteInt32(OtherData + HpOffset, 500);
    }

    private void AddClass(Address type, int nameId, Address super)
    {
        Memory.AddObject(type, _objectLayout, nameId);
        Memory.WritePointer(type + SuperStructOffset, super);
    }

    // Adds a property in the property shape, with the class of its kind and its next property.
    private void AddProperty(Address owner, Address property, int nameId, Address kind, int offset, int elementSize, Address next)
    {
        if (_shape == FieldShape)
        {
            Memory.AddField(property, _fieldLayout, nameId, kind, next);
        }
        else
        {
            Memory.AddObject(property, _objectLayout, nameId, outer: owner, klass: kind);
            Memory.WritePointer(property + _childLayout.Next, next);
        }

        Memory.WriteInt32(property + PropertyElementSizeOffset, elementSize);
        Memory.WriteInt32(property + PropertyOffsetOffset, offset);
    }
}
