using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameEdgeTests(
    MemoryLayout memoryLayout,
    Address start)
{
    private const string ModuleName = "ゲーム ü.exe";
    private const int ModuleSize = 0x1000;

    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so an operand read as signed misses.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000)),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000)),
    ];

    private static readonly byte[] _gEngineCode = [0xE1, 0xE2, 0xE3];
    private static readonly byte[] _gUObjectArrayCode = [0xB1, 0xB2, 0xB3];
    private static readonly byte[] _gWorldCode = [0xC1, 0xC2, 0xC3];
    private static readonly byte[] _namesCode = [0xA1, 0xA2, 0xA3];

    private readonly Address _gEngine = start + 0x12_1000;
    private readonly Address _gUObjectArray = start + 0x11_0000;
    private readonly Address _gWorld = start + 0x12_0000;
    private readonly Address _module = start;
    private readonly Address _names = start + 0x10_0000;
    private readonly Address _otherWorld = start + 0x30_0000;
    private readonly Address _world = start + 0x20_0000;

    private MemoryModule Module => new(ModuleName, null, new MemoryRange(_module, ModuleSize));

    // The chunked name shape looks for NamePoolData, so the message has that global and the module name as is.
    [Test]
    public void Attach_ChunkedNamesWithoutMatch_FailsWithNamePoolData()
    {
        var memory = MemoryWithModule(names: false);

        Assert.That(
            UnrealGame.Attach(memory, Module, Profile("chunked", "pointers")).UnwrapErr().Message,
            Does.StartWith($"Can't find NamePoolData in {ModuleName}. "));
    }

    // Attach locates the name table 1st, so with every global missing the message has NameTable.
    [Test]
    public void Attach_EveryGlobalWithoutMatch_FailsWithTheNameTable()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_module, new byte[ModuleSize]);

        Assert.That(
            UnrealGame.Attach(memory, Module, Profile("indirect", "pointers")).UnwrapErr().Message,
            Does.StartWith($"Can't find NameTable in {ModuleName}. "));
    }

    // An item past the 1024-byte block of the reads fails attach with a result.
    [Test]
    public void Attach_ItemLargerThanTheLimit_FailsWithoutThrowing()
    {
        string json = Set(Document("indirect", "flat"), "\"0x408\"", "offsets", "FUObjectItem", "sizeof");

        Assert.That(
            UnrealGame.Attach(MemoryWithModule(), Module, UnrealProfile.Parse(json).Unwrap()).UnwrapErr().Message,
            Is.EqualTo("An item of 1032 bytes is larger than the 1024-byte limit."));
    }

    // 1 profile attaches to 2 games and each game reads its own GWorld, so the profile doesn't keep a slot.
    [Test]
    public void Attach_SameProfileTwice_EachGameReadsItsOwnWorld()
    {
        var profile = Profile("indirect", "pointers");
        var first = MemoryWithModule();
        var second = MemoryWithModule();
        first.WritePointer(_gWorld, _world);
        second.WritePointer(_gWorld, _otherWorld);

        var firstGame = UnrealGame.Attach(first, Module, profile).Unwrap();
        var secondGame = UnrealGame.Attach(second, Module, profile).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstGame.GWorld.Path.Read<Address>().Unwrap(), Is.EqualTo(_world));
            Assert.That(secondGame.GWorld.Path.Read<Address>().Unwrap(), Is.EqualTo(_otherWorld));
        }
    }

    // Attach only locates the slot, so a slot that isn't readable yet attaches and the read fails with a result.
    [Test]
    public void Attach_GWorldSlotUnreadable_AttachesAndReadFails()
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_module, new byte[ModuleSize]);
        memory.WriteGlobalReference(_module + 0x100, _namesCode, _names);
        memory.WriteGlobalReference(_module + 0x200, _gUObjectArrayCode, _gUObjectArray);
        memory.WriteGlobalReference(_module + 0x300, _gWorldCode, _gWorld);
        memory.WriteGlobalReference(_module + 0x400, _gEngineCode, _gEngine);

        var game = UnrealGame.Attach(memory, Module, Profile("indirect", "pointers")).Unwrap();

        Assert.That(game.GWorld.Path.Read<Address>().UnwrapErr().Message, Is.EqualTo($"Can't read GWorld. No block contains {_gWorld}."));
    }

    // Writes a document with the offsets of the indirect and chunked name tables and the flat and pointer arrays.
    private static string Document(string names, string array)
    {
        string namesGlobal = names == "chunked" ? "NamePoolData" : "NameTable";

        return $$"""
            {
              "engine": "unreal4",
              "versions": ["4.0"],
              "shapes": { "FField": "uproperty", "FNameEntry": "{{names}}", "FUObjectArray": "{{array}}" },
              "offsets": {
                "FFixedUObjectArray": { "Objects": "0x0", "NumElements": "0xC" },
                "FNameEntry": { "Index": "0x0", "AnsiName": "0x10" },
                "FUObjectArray": { "ObjObjects": "0x10" },
                "FUObjectItem": { "Object": "0x8", "sizeof": "0x18" },
                "UField": { "Next": "0x28" },
                "UObjectBase": { "ClassPrivate": "0x10", "NamePrivate": "0x18", "OuterPrivate": "0x20" },
                "UProperty": { "ArrayDim": "0x30", "ElementSize": "0x34", "Offset_Internal": "0x44" },
                "UStruct": { "SuperStruct": "0x30", "Children": "0x38" }
              },
              "globals": {
                "{{namesGlobal}}": { "4.0": [{ "forward": "A1 A2 A3 ????????", "offset": 3 }, { "read": "global" }] },
                "GEngine": { "4.0": [{ "forward": "E1 E2 E3 ????????", "offset": 3 }, { "read": "global" }] },
                "GUObjectArray": { "4.0": [{ "forward": "B1 B2 B3 ????????", "offset": 3 }, { "read": "global" }] },
                "GWorld": { "4.0": [{ "forward": "C1 C2 C3 ????????", "offset": 3 }, { "read": "global" }] }
              }
            }
            """;
    }

    private static UnrealProfile Profile(string names, string array)
    {
        return UnrealProfile.Parse(Document(names, array)).Unwrap();
    }

    // Adds the module with 1 instruction per global and the slots of GWorld and GEngine at 0.
    private FakeMemory MemoryWithModule(bool names = true)
    {
        FakeMemory memory = new() { Layout = memoryLayout };
        memory.Add(_module, new byte[ModuleSize]);

        if (names)
        {
            memory.WriteGlobalReference(_module + 0x100, _namesCode, _names);
        }

        memory.WriteGlobalReference(_module + 0x200, _gUObjectArrayCode, _gUObjectArray);
        memory.WriteGlobalReference(_module + 0x300, _gWorldCode, _gWorld);
        memory.WriteGlobalReference(_module + 0x400, _gEngineCode, _gEngine);
        memory.AddPointers(_gWorld, Address.Zero);
        memory.AddPointers(_gEngine, Address.Zero);

        return memory;
    }
}
