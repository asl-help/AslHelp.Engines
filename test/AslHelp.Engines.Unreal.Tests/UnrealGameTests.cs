using AslHelp.Memory;
using NUnit.Framework;
using System.Linq;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameTests(
    MemoryLayout memoryLayout,
    Address start)
{
    private const string GUObjectArrayLocator = $$"""{ "4.0": {{GUObjectArrayRoute}} }""";
    private const string GUObjectArrayRoute = """[{ "forward": "B1 B2 B3 ????????", "offset": 3 }, { "read": "global" }]""";
    private const string GWorldLocator = """{ "4.0": [{ "forward": "C1 C2 C3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const int HpPoolId = 0x10;
    private const int LevelId = 2;
    private const string MissingLocator = $$"""{ "4.0": {{MissingRoute}} }""";
    private const string MissingRoute = """[{ "forward": "D1 D2 D3 ????????", "offset": 3 }, { "read": "global" }]""";
    private const string ModuleName = "game.exe";
    private const int ModuleSize = 0x1000;
    private const string NamesLocator = """{ "4.0": [{ "forward": "A1 A2 A3 ????????", "offset": 3 }, { "read": "global" }] }""";
    private const int ObjObjects = 0x10;
    private const int WorldId = 1;
    private const int WorldPoolId = 8;

    private static readonly ChunkedObjectArrayLayout _chunkedLayout = new(0x0, 0x14, 0x1C, 0x10000);
    private static readonly NameEntryLayout _entryLayout = new(0x0, 0x10);
    private static readonly FieldPropertyLayout _fieldLayout = new(0x8, 0x18, 0x20);

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64,
    // so an operand read as signed or a pointer read as 4 bytes misses.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private static readonly byte[] _gUObjectArrayCode = [0xB1, 0xB2, 0xB3];
    private static readonly byte[] _gWorldCode = [0xC1, 0xC2, 0xC3];
    private static readonly IndirectObjectArrayLayout _indirectLayout = new(0x0, 0x1000, 0x1004);
    private static readonly ObjectItemLayout _item = new(0x8, 0x18);
    private static readonly ItemObjectArrayLayout _itemLayout = new(0x0, 0xC);
    private static readonly byte[] _namesCode = [0xA1, 0xA2, 0xA3];
    private static readonly ObjectLayout _objectLayout = new(0x20, 0x18, 0x10);

    private readonly Address _chunk = start + 0x42_0000;
    private readonly Address _entries = start + 0x31_0000;
    private readonly Address _field = start + 0x60_0000;
    private readonly Address _gUObjectArray = start + 0x11_0000;
    private readonly Address _gWorld = start + 0x12_0000;
    private readonly Address _items = start + 0x40_0000;
    private readonly Address _module = start;
    private readonly Address _nameChunk = start + 0x30_1000;
    private readonly Address _names = start + 0x10_0000;
    private readonly Address _nameTable = start + 0x30_0000;
    private readonly Address _poolBlock = start + 0x50_0000;
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

        var world = game.GWorld.Read().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_world));
            Assert.That(game.Objects.NameOf(game.Objects.ClassOf(world).Unwrap()).Unwrap(), Is.EqualTo("World"));
            Assert.That(ReadObjects(game), Is.EqualTo(ArrayObjects));
        }
    }

    // The chunked and ffield shapes read names from the pool, objects from the chunks and the property name from FField.NamePrivate at 0x20.
    [Test]
    public void Attach_ChunkedArrayChunkedNamesAndFields_BuildsThoseReaders()
    {
        var memory = MemoryWithModule();
        memory.AddNamePool(_names, _poolBlock);
        memory.AddPoolEntry(_poolBlock + (WorldPoolId * 2), wide: false, "World");
        memory.AddPoolEntry(_poolBlock + (HpPoolId * 2), wide: false, "Hp");
        memory.AddObject(_world, _objectLayout, WorldPoolId, klass: _worldClass);
        memory.AddObject(_worldClass, _objectLayout, WorldPoolId);
        memory.AddChunkedObjectArray(Array, _chunkedLayout, _table, 2, _chunk);
        memory.AddObjectItems(_chunk, _item, _worldClass, _world);
        memory.AddField(_field, _fieldLayout, HpPoolId);

        var game = Attach(memory, Document("ffield", "chunked", "chunked"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(game.Objects.NameOf(_worldClass).Unwrap(), Is.EqualTo("World"));
            Assert.That(ReadObjects(game), Is.EqualTo(ArrayObjects));
            Assert.That(game.Properties.NameOf(_field).Unwrap(), Is.EqualTo("Hp"));
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

        Assert.That(Attach(memory, Document("uproperty", "indirect", "flat")).GWorld.Read().Unwrap(), Is.EqualTo(Address.Zero));
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
            Assert.That(game.GWorld.Read().Unwrap(), Is.EqualTo(_world));
            Assert.That(memory.ReadsAt(_module), Is.EqualTo(searches));
        }
    }

    // A signature that isn't in the module fails with the global, the module and the route.
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
        memory.WriteGlobalReference(_module + 0x400, _gWorldCode, _gWorld + 0x100);

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

    // Writes a document with every offset that any shape reads, so a test only picks the shapes.
    // The layout fields of this class use the same offsets.
    private static string Document(string field, string names, string array)
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

    private static Address[] ReadObjects(UnrealGame game)
    {
        return [.. game.ObjectArray.Read(2).Select(static read => read.Unwrap())];
    }

    private UnrealGame Attach(FakeMemory memory, string json)
    {
        return UnrealGame.Attach(memory, Module, UnrealProfile.Parse(json).Unwrap()).Unwrap();
    }

    // Adds the module with 1 instruction per global and the GWorld slot that points at the world.
    private FakeMemory MemoryWithModule()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_module, new byte[ModuleSize]);
        memory.WriteGlobalReference(_module + 0x100, _namesCode, _names);
        memory.WriteGlobalReference(_module + 0x200, _gUObjectArrayCode, _gUObjectArray);
        memory.WriteGlobalReference(_module + 0x300, _gWorldCode, _gWorld);
        memory.AddPointers(_gWorld, _world);

        return memory;
    }

    // Adds the indirect names None, World and Level and the world with its class.
    private FakeMemory MemoryWithNames()
    {
        string[] names = ["None", "World", "Level"];
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
}
