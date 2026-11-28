using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using NUnit.Framework;
using System.Linq;
using static AslHelp.Engines.Unreal.Tests.FakeGame;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameTests(
    MemoryLayout memoryLayout,
    Address start)
{
    private const int DataId = 3;
    private const int DataOffset = 0x80;
    private const int DataPoolId = 0x18;
    private const int HpId = 4;
    private const int HpOffset = 0x88;
    private const int HpPoolId = 0x10;
    private const int IntPropertyId = 5;
    private const int IntPropertyPoolId = 0x20;
    private const int LevelId = 2;
    private const string MissingLocator = $$"""{ "4.0": {{MissingRoute}} }""";
    private const string MissingRoute = """[{ "forward": "D1 D2 D3 ????????", "offset": 3 }, { "read": "global" }]""";
    private const string ModuleName = "game.exe";
    private const int ModuleSize = 0x1000;
    private const int ObjectPropertyId = 6;
    private const int ObjectPropertyPoolId = 0x28;
    private const int ObjObjects = 0x10;
    private const int PropertyId = 7;
    private const int WorldId = 1;
    private const int WorldPoolId = 8;

    private static readonly ChunkedObjectArrayLayout _chunkedLayout = new(0x0, 0x14, 0x1C, 0x10000);
    private static readonly NameEntryLayout _entryLayout = new(0x0, 0x10);
    private static readonly FieldClassLayout _fieldClassLayout = new(0x8);
    private static readonly FieldPropertyLayout _fieldLayout = new(0x8, 0x18, 0x20);
    private static readonly FieldStructLayout _fieldStructLayout = new(0x50);

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64,
    // so an operand read as signed or a pointer read as 4 bytes misses.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private static readonly IndirectObjectArrayLayout _indirectLayout = new(0x0, 0x1000, 0x1004);
    private static readonly ObjectItemLayout _item = new(0x8, 0x18);
    private static readonly ItemObjectArrayLayout _itemLayout = new(0x0, 0xC);
    private static readonly ObjectLayout _objectLayout = new(0x20, 0x18, 0x10);
    private static readonly PropertyLayout _propertyLayout = new(0x30, 0x34, 0x44);
    private static readonly StructLayout _structLayout = new(0x30, 0x38);

    private readonly Address _chunk = start + 0x42_0000;
    private readonly Address _data = start + 0x21_0000;
    private readonly Address _dataClass = start + 0x21_1000;
    private readonly Address _dataProperty = start + 0x60_1000;
    private readonly Address _entries = start + 0x31_0000;
    private readonly Address _field = start + 0x60_0000;
    private readonly Address _gEngine = start + 0x12_1000;
    private readonly Address _gUObjectArray = start + 0x11_0000;
    private readonly Address _gWorld = start + 0x12_0000;
    private readonly Address _hpProperty = start + 0x60_2000;
    private readonly Address _intPropertyClass = start + 0x61_0000;
    private readonly Address _items = start + 0x40_0000;
    private readonly Address _module = start;
    private readonly Address _nameChunk = start + 0x30_1000;
    private readonly Address _names = start + 0x10_0000;
    private readonly Address _nameTable = start + 0x30_0000;
    private readonly Address _objectPropertyClass = start + 0x61_1000;
    private readonly Address _poolBlock = start + 0x50_0000;
    private readonly Address _propertyClass = start + 0x61_2000;
    private readonly Address _table = start + 0x41_0000;
    private readonly Address _world = start + 0x20_0000;
    private readonly Address _worldClass = start + 0x20_1000;

    private Address Array => _gUObjectArray + ObjObjects;

    private Address[] ArrayObjects => [_worldClass, _world];

    private MemoryModule Module => new(ModuleName, null, new MemoryRange(_module, ModuleSize));

    // The flat array reads its items at ObjObjects and not at the global. The class of GWorld reads as World.
    [Test]
    public void Attach_ItemArrayAndIndirectNames_ReadsTheClassNameOfGWorld()
    {
        var game = Attach(MemoryWithGame(), Document("uproperty", "indirect", "flat"));

        var world = game.GWorld.Path.Read().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_world));
            Assert.That(game.ObjectReader.NameOf(game.ObjectReader.ClassOf(world).Unwrap()).Unwrap(), Is.EqualTo("World"));
            Assert.That(ReadObjects(game), Is.EqualTo(ArrayObjects));
        }
    }

    // The chunked and ffield shapes read names from the pool, objects from the chunks and the property name from FField.NamePrivate at 0x20.
    [Test]
    public void Attach_ChunkedArrayChunkedNamesAndFields_BuildsThoseReaders()
    {
        var memory = MemoryWithChunkedGame();
        memory.AddField(_field, _fieldLayout, HpPoolId);

        var game = Attach(memory, Document("ffield", "chunked", "chunked"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(game.ObjectReader.NameOf(_worldClass).Unwrap(), Is.EqualTo("World"));
            Assert.That(ReadObjects(game), Is.EqualTo(ArrayObjects));
            Assert.That(game.PropertyReader.NameOf(_field).Unwrap(), Is.EqualTo("Hp"));
        }
    }

    // The pointers shape reads a data pointer and a count at ObjObjects.
    [Test]
    public void Attach_PointerArray_ReadsObjects()
    {
        var memory = MemoryWithNames();
        memory.AddNameArray(Array, _items, 2, _worldClass, _world);

        Assert.That(ReadObjects(Attach(memory, Document("uproperty", "indirect", "pointers"))), Is.EqualTo(ArrayObjects));
    }

    // The indirect shape reads the chunk table inside the array at ObjObjects.
    [Test]
    public void Attach_IndirectArray_ReadsObjects()
    {
        var memory = MemoryWithNames();
        memory.AddIndirectObjectArray(Array, _indirectLayout, 2, _chunk);
        memory.AddPointers(_chunk, _worldClass, _world);

        Assert.That(ReadObjects(Attach(memory, Document("uproperty", "indirect", "indirect"))), Is.EqualTo(ArrayObjects));
    }

    // A game without a world yet still attaches, because the world can come later.
    [Test]
    public void Attach_GWorldNull_AttachesAndReadsZero()
    {
        var memory = MemoryWithGame();
        memory.WritePointer(_gWorld, Address.Zero);

        Assert.That(Attach(memory, Document("uproperty", "indirect", "flat")).GWorld.Path.Read().Unwrap(), Is.EqualTo(Address.Zero));
    }

    // The slot reads on each access without another search, so a world set after attach shows up.
    [Test]
    public void Attach_GWorldSetAfterAttach_ReadsTheNewPointer()
    {
        var memory = MemoryWithGame();
        memory.WritePointer(_gWorld, Address.Zero);
        var game = Attach(memory, Document("uproperty", "indirect", "flat"));
        int searches = memory.ReadsAt(_module);

        memory.WritePointer(_gWorld, _world);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(game.GWorld.Path.Read().Unwrap(), Is.EqualTo(_world));
            Assert.That(memory.ReadsAt(_module), Is.EqualTo(searches));
        }
    }

    // A signature that isn't in the module fails with the global, the module and the route.
    [TestCase("GEngine")]
    [TestCase("GWorld")]
    [TestCase("GUObjectArray")]
    [TestCase("NameTable")]
    public void Attach_LocatorWithoutMatch_FailsWithTheGlobalAndTheRoute(string global)
    {
        string json = Set(Document("uproperty", "indirect", "flat"), MissingLocator, "globals", global);

        Assert.That(
            UnrealGame.Attach(MemoryWithGame(), Module, UnrealProfile.Parse(json).Unwrap()).UnwrapErr().Message,
            Does.StartWith($"Can't find {global} in {ModuleName}. No route located the address. '4.0': "));
    }

    // A global with 2 routes attaches when only the 2nd matches, as GUObjectArray of 4.8 to 4.10 needs.
    [Test]
    public void Attach_OnlySecondRouteMatches_Attaches()
    {
        string routes = $$"""{ "4.8.3": {{MissingRoute}}, "4.10.4": {{GUObjectArrayRoute}} }""";
        string json = Set(Document("uproperty", "indirect", "flat"), routes, "globals", "GUObjectArray");

        Assert.That(ReadObjects(Attach(MemoryWithGame(), json)), Is.EqualTo(ArrayObjects));
    }

    // 2 matches that reach 2 addresses fail attach, so the game never picks 1 of them.
    [Test]
    public void Attach_RouteReachesTwoAddresses_Fails()
    {
        var memory = MemoryWithGame();
        memory.WriteGlobalReference(_module + 0x500, GWorldCode, _gWorld + 0x100);

        Assert.That(
            UnrealGame.Attach(memory, Module, UnrealProfile.Parse(Document("uproperty", "indirect", "flat")).Unwrap()).UnwrapErr().Message,
            Does.StartWith($"Can't find GWorld in {ModuleName}.").And.Contain("reaches both"));
    }

    // An item without room for a pointer fails attach with a result, where the array constructor alone would throw.
    [Test]
    public void Attach_ItemSmallerThanAPointer_FailsWithoutThrowing()
    {
        string json = Set(Document("uproperty", "indirect", "flat"), "\"0x8\"", "offsets", "FUObjectItem", "sizeof");

        Assert.That(
            UnrealGame.Attach(MemoryWithGame(), Module, UnrealProfile.Parse(json).Unwrap()).UnwrapErr().Message,
            Is.EqualTo($"An item of 8 bytes doesn't fit a {memoryLayout.PointerSize}-byte pointer at offset 8."));
    }

    // 0 items per chunk fails attach with a result, where the array constructor alone would throw.
    [Test]
    public void Attach_ElementsPerChunkZero_FailsWithoutThrowing()
    {
        string json = Set(Document("uproperty", "indirect", "chunked"), "\"0x0\"", "offsets", "FChunkedFixedUObjectArray", "NumElementsPerChunk");

        Assert.That(
            UnrealGame.Attach(MemoryWithGame(), Module, UnrealProfile.Parse(json).Unwrap()).UnwrapErr().Message,
            Is.EqualTo("ElementsPerChunk is 0, which is below 1."));
    }

    // Reads Hp from GWorld through public members only, with the property shape of the profile.
    [TestCase("uproperty")]
    [TestCase("ffield")]
    public void Read_PropertyThroughGWorld_ReadsTheValue(string field)
    {
        var game = field == "ffield"
            ? Attach(MemoryWithChunkedData(), Document(field, "chunked", "chunked"))
            : Attach(MemoryWithData(), Document(field, "indirect", "flat"));

        var world = game.GWorld.Path.Read().Unwrap();
        var data = game.Read<Address>(world, game.ClassOf(world).Unwrap().FindProperty("Data").Unwrap()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data, Is.EqualTo(_data));
            Assert.That(game.Read<int>(data, game.ClassOf(data).Unwrap().FindProperty("Hp").Unwrap()).Unwrap(), Is.EqualTo(995));
        }
    }

    // A null name is a mistake of the caller, so it throws and doesn't fail as a result.
    [Test]
    public void FindClass_NullName_Throws()
    {
        var game = Attach(MemoryWithGame(), Document("uproperty", "indirect", "flat"));

        Assert.That(() => game.FindClass(null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("name"));
    }

    private static void AddStruct(FakeMemory memory, Address type, int nameId, Address super)
    {
        memory.AddObject(type, _objectLayout, nameId);
        memory.WritePointer(type + _structLayout.SuperStruct, super);
    }

    private static Address[] ReadObjects(UnrealGame game)
    {
        return [.. game.ObjectArray.Read(2).Select(static read => read.Unwrap())];
    }

    private UnrealGame Attach(FakeMemory memory, string json)
    {
        return UnrealGame.Attach(memory, Module, UnrealProfile.Parse(json).Unwrap()).Unwrap();
    }

    // Adds the module with 1 instruction per global, the GWorld slot that points at the world and a GEngine slot of 0.
    private FakeMemory MemoryWithModule()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_module, new byte[ModuleSize]);
        WriteGlobalReferences(memory, _module, _names, _gUObjectArray, _gWorld, _gEngine);
        memory.AddPointers(_gWorld, _world);
        memory.AddPointers(_gEngine, Address.Zero);

        return memory;
    }

    // Adds the indirect names that the tests read and the world with its class.
    private FakeMemory MemoryWithNames()
    {
        string[] names = ["None", "World", "Level", "Data", "Hp", "IntProperty", "ObjectProperty", "Property"];
        var entries = Enumerable.Range(0, names.Length).Select(id => _entries + (id * 0x100)).ToArray();

        var memory = MemoryWithModule();
        memory.AddPointers(_names, _nameTable);
        memory.AddPointers(_nameTable, _nameChunk);
        memory.AddPointers(_nameChunk, entries);

        for (int id = 0; id < names.Length; id++)
        {
            memory.AddNameEntry(entries[id], _entryLayout, id << 1, names[id]);
        }

        memory.AddObject(_world, _objectLayout, LevelId, klass: _worldClass);
        memory.AddObject(_worldClass, _objectLayout, WorldId);

        return memory;
    }

    // Adds the names, the world and a flat array of the world class and the world.
    private FakeMemory MemoryWithGame()
    {
        var memory = MemoryWithNames();
        memory.AddItemObjectArray(Array, _itemLayout, _items, 2);
        memory.AddObjectItems(_items, _item, _worldClass, _world);

        return memory;
    }

    // Gives the class of the world the property Data, which points at an object whose Hp is 995, with properties as objects.
    private FakeMemory MemoryWithData()
    {
        var memory = MemoryWithGame();
        AddStruct(memory, _propertyClass, PropertyId, Address.Zero);
        AddStruct(memory, _intPropertyClass, IntPropertyId, _propertyClass);
        AddStruct(memory, _objectPropertyClass, ObjectPropertyId, _propertyClass);
        AddStruct(memory, _dataClass, DataId, Address.Zero);
        memory.AddObject(_data, _objectLayout, DataId, klass: _dataClass);

        memory.AddObject(_dataProperty, _objectLayout, DataId, outer: _worldClass, klass: _objectPropertyClass);
        memory.WritePointer(_worldClass + _structLayout.Children, _dataProperty);
        memory.AddObject(_hpProperty, _objectLayout, HpId, outer: _dataClass, klass: _intPropertyClass);
        memory.WritePointer(_dataClass + _structLayout.Children, _hpProperty);
        WriteValues(memory);

        return memory;
    }

    // Adds the names World and Hp in a pool, the world with its class and a chunked array of the world class and the world.
    private FakeMemory MemoryWithChunkedGame()
    {
        var memory = MemoryWithModule();
        memory.AddNamePool(_names, _poolBlock);
        memory.AddPoolEntry(_poolBlock + (WorldPoolId * 2), wide: false, "World");
        memory.AddPoolEntry(_poolBlock + (HpPoolId * 2), wide: false, "Hp");
        memory.AddObject(_world, _objectLayout, WorldPoolId, klass: _worldClass);
        memory.AddObject(_worldClass, _objectLayout, WorldPoolId);
        memory.AddChunkedObjectArray(Array, _chunkedLayout, _table, 2, _chunk);
        memory.AddObjectItems(_chunk, _item, _worldClass, _world);

        return memory;
    }

    // Adds the same Data and Hp as MemoryWithData, with properties as fields.
    private FakeMemory MemoryWithChunkedData()
    {
        var memory = MemoryWithChunkedGame();
        memory.AddPoolEntry(_poolBlock + (DataPoolId * 2), wide: false, "Data");
        memory.AddPoolEntry(_poolBlock + (IntPropertyPoolId * 2), wide: false, "IntProperty");
        memory.AddPoolEntry(_poolBlock + (ObjectPropertyPoolId * 2), wide: false, "ObjectProperty");
        memory.AddFieldClass(_intPropertyClass, _fieldClassLayout, IntPropertyPoolId);
        memory.AddFieldClass(_objectPropertyClass, _fieldClassLayout, ObjectPropertyPoolId);
        memory.AddObject(_dataClass, _objectLayout, DataPoolId);
        memory.AddObject(_data, _objectLayout, DataPoolId, klass: _dataClass);

        memory.AddField(_dataProperty, _fieldLayout, DataPoolId, _objectPropertyClass);
        memory.WritePointer(_worldClass + _fieldStructLayout.ChildProperties, _dataProperty);
        memory.AddField(_hpProperty, _fieldLayout, HpPoolId, _intPropertyClass);
        memory.WritePointer(_dataClass + _fieldStructLayout.ChildProperties, _hpProperty);
        WriteValues(memory);

        return memory;
    }

    // Writes the offsets and the sizes of Data and Hp, the pointer to the data object and its Hp.
    private void WriteValues(FakeMemory memory)
    {
        memory.WriteInt32(_dataProperty + _propertyLayout.ElementSize, memoryLayout.PointerSize);
        memory.WriteInt32(_dataProperty + _propertyLayout.Offset, DataOffset);
        memory.WriteInt32(_hpProperty + _propertyLayout.ElementSize, sizeof(int));
        memory.WriteInt32(_hpProperty + _propertyLayout.Offset, HpOffset);
        memory.WritePointer(_world + DataOffset, _data);
        memory.WriteInt32(_data + HpOffset, 995);
    }
}
