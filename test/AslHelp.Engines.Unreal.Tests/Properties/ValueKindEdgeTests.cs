using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;

namespace AslHelp.Engines.Unreal.Tests.Properties;

[TestFixtureSource(nameof(_fixtures))]
public sealed class ValueKindEdgeTests(
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

    private UnrealPath Data => _game.GWorld.Member("GameMode").Member("Data");

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // An enum whose underlying pointer points back at itself fails with its class instead of binding the same enum again and again.
    [Test]
    public void Read_EnumUnderlyingIsItself_FailsWithTheUnderlyingClass()
    {
        var kind = _fakeGame.PropertyAddress("Kind");
        _fakeGame.Memory.WritePointer(kind + FakeGame.StructTypeOffset, kind);

        Assert.That(
            Data.Member<byte>("Kind").Read().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.Kind. The underlying property of Kind is EnumProperty, which isn't a number. "
                + "Check UnderlyingProp of EnumProperty in the profile (0x70)."));
    }

    // An underlying property that is a string, like from a wrong offset, fails with its class instead of reading the enum as a string.
    [Test]
    public void Read_EnumWithStringUnderlying_FailsWithItsClass()
    {
        var kind = _fakeGame.PropertyAddress("Kind");
        _fakeGame.Memory.WritePointer(kind + FakeGame.StructTypeOffset, _fakeGame.PropertyAddress("StringValue"));

        Assert.That(
            Data.Member<byte>("Kind").Read().UnwrapErr().Message,
            Is.EqualTo(
                "Can't read GWorld.GameMode.Data.Kind. The underlying property of Kind is StrProperty, which isn't a number. "
                + "Check UnderlyingProp of EnumProperty in the profile (0x70)."));
    }

    // A failed read of the underlying property isn't kept, so the enum reads once the pointer is fixed.
    [Test]
    public void Read_EnumUnderlyingUnreadable_ReadsAgainNextTime()
    {
        var pointer = _fakeGame.PropertyAddress("Kind") + FakeGame.StructTypeOffset;
        var kind = Data.Member<byte>("Kind");
        _fakeGame.Memory.WritePointer(pointer, _fakeGame.UnreadableAddress);

        var failed = kind.Read();
        _fakeGame.Memory.WritePointer(pointer, _fakeGame.PropertyAddress("KindUnderlying"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.UnwrapErr().Message, Does.StartWith("Can't read GWorld.GameMode.Data.Kind. "));
            Assert.That(kind.Read().Unwrap(), Is.EqualTo(FakeGame.Kind));
        }
    }

    // A struct type whose name doesn't read isn't kept, so the step reads once the pointer is fixed.
    [Test]
    public void Read_StructTypeUnreadable_ReadsAgainNextTime()
    {
        var pointer = _fakeGame.PropertyAddress("Coord") + FakeGame.StructTypeOffset;
        var x = Data.Member("Coord").Member<int>("X");
        var coordType = _fakeGame.Memory.ReadPointer(pointer).Unwrap();
        _fakeGame.Memory.WritePointer(pointer, _fakeGame.UnreadableAddress);

        var failed = x.Read();
        _fakeGame.Memory.WritePointer(pointer, coordType);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.UnwrapErr().Message, Does.StartWith("Can't read GWorld.GameMode.Data.Coord.X at GWorld.GameMode.Data.Coord. "));
            Assert.That(x.Read().Unwrap(), Is.EqualTo(FakeGame.CoordX));
        }
    }

    // A negative name number only comes from damaged memory and fails with the number.
    [Test]
    public void Read_NameNumberNegative_FailsWithTheNumber()
    {
        var name = _fakeGame.Data + FakeGame.NameValueOffset;
        _fakeGame.Memory.WriteInt32(name + 4, -1);

        Assert.That(
            Data.Member<string>("NameValue").Read().UnwrapErr().Message,
            Is.EqualTo($"Can't read GWorld.GameMode.Data.NameValue. The name at {name} has the number -1, which is below 0."));
    }

    // An id past the name table fails, then the kept text doesn't hide a later id.
    [Test]
    public void Read_NameIdPastTableThenNone_FailsThenReadsNone()
    {
        var nameValue = Data.Member<string>("NameValue");
        _ = nameValue.Read().Unwrap();
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.NameValueOffset, 100_000);

        var failed = nameValue.Read();
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.NameValueOffset, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.UnwrapErr().Message, Does.StartWith("Can't read GWorld.GameMode.Data.NameValue. "));
            Assert.That(nameValue.Read().Unwrap(), Is.EqualTo("None"));
        }
    }

    // A count near int.MaxValue fails on the bound and doesn't touch the data pointer.
    [Test]
    public void Read_StringCountHuge_FailsWithoutReadingTheData()
    {
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.StringCountOffset, int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Data.Member<string>("StringValue").Read().UnwrapErr().Message,
                Is.EqualTo("Can't read GWorld.GameMode.Data.StringValue. StringValue has 2147483646 characters, past the limit of 512."));
            Assert.That(_fakeGame.Memory.ReadsAt(_fakeGame.Data + FakeGame.StringValueOffset), Is.Zero);
        }
    }

    // The count decides the length, so a 0 inside the text stays.
    [Test]
    public void Read_StringWithZeroInside_ReadsByCount()
    {
        _fakeGame.WriteString("ab\0cd");

        Assert.That(Data.Member<string>("StringValue").Read().Unwrap(), Is.EqualTo("ab\0cd"));
    }

    // A surrogate pair is 2 UTF-16 units in the count, so it comes back whole.
    [Test]
    public void Read_StringSurrogatePair_ReadsBothUnits()
    {
        _fakeGame.WriteString("x😀y");

        Assert.That(Data.Member<string>("StringValue").Read().Unwrap(), Is.EqualTo("x😀y"));
    }

    // A short text after a long one reuses the buffer and doesn't keep characters of the long one.
    [Test]
    public void CreateWatcher_ShortStringAfterLongOne_ReadsOnlyTheShortOne()
    {
        ManualTicks ticks = new();
        var watcher = Data.Member<string>("StringValue").CreateWatcher(ticks);
        _fakeGame.WriteString(new string('x', 512));
        ticks.Value++;
        _ = watcher.Current;

        _fakeGame.WriteString("hi");
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo("hi"));
            Assert.That(watcher.Old, Has.Length.EqualTo(512));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A data pointer that doesn't read fails with the object and the address of the characters.
    [Test]
    public void Read_StringDataUnreadable_FailsWithTheDataAddress()
    {
        var data = _fakeGame.UnreadableAddress;
        _fakeGame.Memory.WritePointer(_fakeGame.Data + FakeGame.StringValueOffset, data);

        Assert.That(
            Data.Member<string>("StringValue").Read().UnwrapErr().Message,
            Is.EqualTo(
                $"Can't read GWorld.GameMode.Data.StringValue. Can't read StringValue of the object at {_fakeGame.Data}: "
                + $"reading {data} failed. No block contains {data}."));
    }

    // A kind with a profile part still fails a read of another type with both types and before a read of the value.
    [TestCase("BoolValue", "BoolProperty", "Boolean")]
    [TestCase("NameValue", "NameProperty", "String")]
    [TestCase("StringValue", "StrProperty", "String")]
    public void Read_LayoutKindAsInt64_FailsWithBothTypes(string name, string className, string readType)
    {
        var path = Data.Member<long>(name);
        _ = path.Read();
        int reads = _fakeGame.Memory.Reads;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                path.Read().UnwrapErr().Message,
                Is.EqualTo($"Can't read GWorld.GameMode.Data.{name}. {name} is {className}, which reads as {readType}, not Int64."));
            Assert.That(_fakeGame.Memory.Reads - reads, Is.EqualTo(1 + (2 * 3) + 2));
        }
    }

    // A chain watcher that fails with Clear doesn't keep a value, so the next good read fills Current and Changed stays false.
    [Test]
    public void CreateWatcher_ChainClearedThenGood_FillsWithoutChange()
    {
        ManualTicks ticks = new();
        var watcher = (Watcher<object?>)((IWatcherSource)_game.GWorldChain.GameMode.Data.Speed).CreateWatcher(ticks);
        watcher.FailAction = FailAction.Clear;
        _ = watcher.Current;

        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, Address.Zero);
        ticks.Value++;
        object? cleared = watcher.Current;

        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, _fakeGame.Data);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleared, Is.Null);
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Speed));
            Assert.That(watcher.Old, Is.Null);
            Assert.That(watcher.Changed, Is.False);
        }
    }
}
