using AslHelp.Engines.Errors;
using AslHelp.Memory;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPathReadEdgeTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    // Starts every address above the signed 4-byte range on x86 and above 4 GB on x64, so a 4-byte or sign-extended pointer read fails.
    private static readonly TestFixtureData[] _fixtures =
    [
        new(MemoryLayout.X86, new Address(0x8000_0000), "uproperty"),
        new(MemoryLayout.X86, new Address(0x8000_0000), "ffield"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "uproperty"),
        new(MemoryLayout.X64, new Address(0x7FF6_0000_0000), "ffield"),
    ];

    private FakeGame _fakeGame = null!;
    private UnrealGame _game = null!;

    private Result<IMemberPath> Data => _game.GWorld.Path.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // The keys of IntDict are Int32 on the declared class too, so a text key fails on the step while Data is null.
    [Test]
    public void Item_NullObjectKeyOfTheWrongType_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        var error = Data.Member("IntDict").Item("x").UnwrapErr();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                error.Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.IntDict[\"x\"]. The keys of IntDict are Int32, which the key \"x\" can't match."));
            Assert.That(error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
        }
    }

    // A key of the right type can't be found without the object, so it stays unchecked while Data is null.
    [Test]
    public void Item_NullObjectKeyOfTheRightType_StaysUnchecked()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        Assert.That(Data.Member("IntDict").Item(6).Unwrap().IsChecked, Is.False);
    }

    // ObjectSet declares FixtureData for its elements, so a typo after a position fails on the step while Data is null.
    [Test]
    public void Member_NullObjectTypoAfterAnObjectSetPosition_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("ObjectSet").Item(1).Member("Hp").Unwrap().IsChecked, Is.True);
            Assert.That(
                Data.Member("ObjectSet").Item(1).Member("Hpp").UnwrapErr().Message,
                Is.EqualTo(
                    "Can't read GWorld.GameMode.Data.ObjectSet[1].Hpp. "
                    + "Class FixtureData and its loaded derived classes don't have a property Hpp."));
        }
    }

    // Items declares FixtureData for its elements and ObjectDict for its values, so both check a member after the item while Data is null.
    [Test]
    public void Member_NullObjectAfterAnArrayAndAMapOfObjects_ChecksTheDeclaredClass()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("Items").Item(0).Member("Speed").Unwrap().IsChecked, Is.True);
            Assert.That(Data.Member("ObjectDict").Item("data").Member("Hp").Unwrap().IsChecked, Is.True);
            Assert.That(Data.Member("Items").Item(0).Member("Spede").IsErr, Is.True);
            Assert.That(Data.Member("ObjectDict").Item("data").Member("Spede").IsErr, Is.True);
        }
    }

    // A set of ints reads whether it has the key and a map keyed by objects reads ints, so a member after either fails while Data is null.
    [Test]
    public void Member_NullObjectAfterAValueItem_FailsOnTheStep()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("IntSet").Item(22).Member("X").IsErr, Is.True);
            Assert.That(Data.Member("ObjectKeyDict").Item(0).Member("X").IsErr, Is.True);
        }
    }

    // A member in another case gets checked on the declared class and the path keeps the text the script wrote.
    [Test]
    public void Member_NullObjectNameInOtherCase_IsCheckedAndReadsOnceDataIsBack()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        var hp = Data.Member("hP").Unwrap();
        bool isChecked = hp.IsChecked;

        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, _fakeGame.Data);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isChecked, Is.True);
            Assert.That(hp.ToString(), Is.EqualTo("GWorld.GameMode.Data.hP"));
            Assert.That(hp.Read<int>().Unwrap(), Is.EqualTo(FakeGame.Hp));
        }
    }

    // Only FixtureGameMode has Data, so data in another case stays unchecked on GameModeBase and a typo in caps fails with its own text.
    [Test]
    public void Member_NullObjectDerivedMemberInOtherCase_StaysUnchecked()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);
        var gameMode = _game.GWorld.Path.Member("GameMode");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gameMode.Member("data").Unwrap().IsChecked, Is.False);
            Assert.That(
                gameMode.Member("DATAA").UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.DATAA. Class GameModeBase and its loaded derived classes don't have a property DATAA."));
        }
    }

    // GEngine is 0, so the steps after it get checked on Engine, the class that the engine declares it as.
    [Test]
    public void Member_NullEngine_ChecksAgainstTheEngineClass()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GEngineSlot, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_game.GEngine.Path.Member("Data").Member("Hp").Unwrap().IsChecked, Is.True);
            Assert.That(
                _game.GEngine.Path.Member("Dat").UnwrapErr().Message,
                Is.EqualTo("Can't read GEngine.Dat. Class Engine and its loaded derived classes don't have a property Dat."));
        }
    }

    // An empty name isn't a member of any class, so it fails on the step with and without the object.
    [Test]
    public void Member_EmptyName_FailsOnTheStep()
    {
        var live = Data.Member("");
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        var unread = Data.Member("");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(live.UnwrapErr(), Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
            Assert.That(unread.UnwrapErr(), Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
        }
    }

    // A read of Count boxes the number of used slots of ObjectSet, not the 3 slots with the free one.
    [Test]
    public void Read_CountOfAnObjectSet_ReturnsTheUsedSlots()
    {
        Assert.That(Data.Member("ObjectSet").Count.Read().Unwrap(), Is.EqualTo(2));
    }

    // The 2nd used slot of ObjectSet points at OtherData, so a read after the free slot reads its Hp of 500.
    [Test]
    public void Read_MemberOfTheSecondUsedSlotOfAnObjectSet_SkipsTheFreeSlot()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("ObjectSet").Item(1).Read().Unwrap(), Is.EqualTo(_fakeGame.OtherData));
            Assert.That(Data.Member("ObjectSet").Item(1).Member("Hp").Read<int>().Unwrap(), Is.EqualTo(500));
        }
    }

    // An object item of an array, a set and a map reads as an Address both boxed and typed.
    [Test]
    public void Read_ObjectItemOfEachCollection_ReadsAsAnAddress()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("Items").Item(1).Read().Unwrap(), Is.EqualTo(_fakeGame.OtherData));
            Assert.That(Data.Member("ObjectSet").Item(0).Read<Address>().Unwrap(), Is.EqualTo(_fakeGame.Data));
            Assert.That(Data.Member("ObjectDict").Item("other").Read().Unwrap(), Is.EqualTo(_fakeGame.OtherData));
        }
    }

    // A failed read of the root reaches every caller as a MemberPathError that can work later.
    [Test]
    public void Read_RootSlotThatDoesNotRead_FailsWithoutAShapeMismatch()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, _fakeGame.UnreadableAddress);
        var hp = Data.Member("Hp");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.Read().UnwrapErr(), Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).False);
            Assert.That(hp.Read<int>().UnwrapErr(), Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).False);
        }
    }

    // The root alone only reads as an Address, so any other type fails as a shape mismatch.
    [Test]
    public void Read_RootAsAnotherType_FailsWithAShapeMismatch()
    {
        var error = _game.GWorld.Path.Read<int>().UnwrapErr();

        Assert.That(error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
    }
}
