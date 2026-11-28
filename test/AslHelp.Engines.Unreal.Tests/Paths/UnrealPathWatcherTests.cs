using AslHelp.Engines.Errors;
using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Tests.Paths;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealPathWatcherTests(
    MemoryLayout memoryLayout,
    Address start,
    string shape)
{
    private const int ProbeCount = 1000;

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

    // A boxed watcher prints the path it reads.
    [Test]
    public void CreateWatcher_AMember_PrintsThePath()
    {
        Assert.That(Watch("Hp").ToString(), Is.EqualTo("GWorld.GameMode.Data.Hp"));
    }

    // The 1st good read gives the value in the type of the kind, Old stays null and Changed stays false.
    [TestCaseSource(nameof(KindValues))]
    public void CreateWatcher_FirstGoodRead_GivesTheTypedValue(KindCase kindCase)
    {
        var watcher = Watch(kindCase.Member);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.TypeOf(kindCase.Expected.GetType()).And.EqualTo(kindCase.Expected));
            Assert.That(watcher.Old, Is.Null);
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // A value written between 2 ticks shows up as a change on the 2nd tick.
    [Test]
    public void CreateWatcher_ValueChangesBetweenTicks_ReportsOldAndCurrent()
    {
        ManualTicks ticks = new();
        var watcher = Watch("Hp", ticks);

        _ = watcher.Current;
        _fakeGame.Memory.WriteInt32(_fakeGame.Data + FakeGame.HpOffset, 990);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.EqualTo(FakeGame.Hp));
            Assert.That(watcher.Current, Is.EqualTo(990));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A null game mode fails every read before the 1st good one, so both values stay null without a change for Clear and KeepOld.
    [TestCase(FailAction.Clear)]
    [TestCase(FailAction.KeepOld)]
    public void CreateWatcher_BeforeFirstGoodRead_CurrentAndOldAreNullAndChangedIsFalse(FailAction failAction)
    {
        ManualTicks ticks = new();
        var watcher = Watch("Hp", ticks);
        watcher.FailAction = failAction;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        (object? current, object? old, bool changed) = (watcher.Current, watcher.Old, watcher.Changed);
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, _fakeGame.GameMode);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That((current, old, changed), Is.EqualTo(((object?)null, (object?)null, false)));
            Assert.That(watcher.Current, Is.EqualTo(FakeGame.Hp));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // FailAction.Throw throws before the 1st good read with the path and the null object.
    [Test]
    public void CreateWatcher_BeforeFirstGoodReadWithThrow_ThrowsWithThePath()
    {
        var watcher = Watch("Hp");
        watcher.FailAction = FailAction.Throw;
        _fakeGame.Memory.WritePointer(_fakeGame.World + FakeGame.GameModeOffset, Address.Zero);

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.StartsWith(
                "Couldn't read GWorld.GameMode.Data.Hp. Can't read GWorld.GameMode.Data.Hp: GWorld.GameMode is null."));
    }

    // FloatFixtureData has a Hp that is a float, so the watcher keeps the int as Old and gives the float with a change.
    [Test]
    public void CreateWatcher_KindChangesBetweenReads_GivesTheNewTypeAndChanged()
    {
        ManualTicks ticks = new();
        var watcher = Watch("Hp", ticks);

        _ = watcher.Current;
        _fakeGame.Memory.WritePointer(_fakeGame.GameMode + FakeGame.DataOffset, _fakeGame.FloatData);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.TypeOf<int>().And.EqualTo(FakeGame.Hp));
            Assert.That(watcher.Current, Is.TypeOf<float>().And.EqualTo(FakeGame.FloatHp));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // The same characters in another buffer keep the text without a change and other characters change it.
    [Test]
    public void CreateWatcher_StringChanges_ChangedByContent()
    {
        ManualTicks ticks = new();
        var watcher = Watch("StringValue", ticks);
        object? first = watcher.Current;

        _fakeGame.WriteString(FakeGame.StringText);
        ticks.Value++;
        (object? same, bool sameChanged) = (watcher.Current, watcher.Changed);
        _fakeGame.WriteString("other-text");
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(same, Is.SameAs(first));
            Assert.That(sameChanged, Is.False);
            Assert.That(watcher.Current, Is.EqualTo("other-text"));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // The path goes through the game mode and data pointers and then into the Coord struct by its offset.
    [Test]
    public void CreateWatcher_ThroughObjectsAndStruct_ReadsTheValue()
    {
        var watcher = Watch("Coord.Y");

        Assert.That(watcher.Current, Is.EqualTo(FakeGame.CoordY));
    }

    // Mixed has an int and a float, so its boxed value gives each member in its own type.
    [Test]
    public void CreateWatcher_PathEndsOnMixedStruct_GivesEachMemberInItsOwnType()
    {
        var watcher = Watch("Mixed");

        Assert.That(watcher.Current, Is.EqualTo(new StructValue("FixtureMixed", ["A", "B"], [1, 2f])));
    }

    // A kind the library doesn't read fails with the property and its class.
    [Test]
    public void CreateWatcher_UnsupportedKind_FailsWithThePropertyAndClass()
    {
        var watcher = Watch("TextValue");
        watcher.FailAction = FailAction.Throw;

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.Contains("TextValue is TextProperty, which the library doesn't read."));
    }

    // The kind doesn't depend on memory, so the failure is a shape mismatch and a later read can't work.
    [Test]
    public void CreateWatcher_UnsupportedKind_FailsAsShapeMismatch()
    {
        var watcher = Watch("TextValue");
        _ = watcher.Current;

        Assert.That(watcher.Error, Is.InstanceOf<MemberPathError>().With.Property(nameof(MemberPathError.IsShapeMismatch)).True);
    }

    // The path of the global alone reads the address of the world, 0 while the slot is 0.
    [Test]
    public void CreateWatcher_PathOfTheRootAlone_ReadsTheAddressOfTheObject()
    {
        ManualTicks ticks = new();
        var watcher = _game.GWorld.Path.CreateWatcher(ticks);

        object? world = watcher.Current;
        _fakeGame.Memory.WritePointer(_fakeGame.GWorldSlot, Address.Zero);
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world, Is.EqualTo(_fakeGame.World));
            Assert.That(watcher.Current, Is.EqualTo(Address.Zero));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A tick of an unchanged value returns the box of the tick before, so a kind doesn't allocate after the 1st read.
    [TestCaseSource(nameof(KindValues))]
    public void CreateWatcher_SteadyStateEachKind_DoesNotAllocate(KindCase kindCase)
    {
        ManualTicks ticks = new();
        var watcher = Watch(kindCase.Member, ticks);
        _ = watcher.Current;
        ticks.Value++;
        _ = watcher.Current;

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int count = 0; count < ProbeCount; count++)
        {
            ticks.Value++;
            _ = watcher.Current;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(kindCase.Expected));
            Assert.That(bytes, Is.Zero);
        }
    }

    // An index step goes into an array element, a key into a map value or a set entry and Count reads the number of elements.
    [Test]
    public void Item_IndexAndKeySteps_WatchElements()
    {
        var hp = Data.Member("Items").Item(1).Member("Hp").Unwrap();
        var two = Data.Member("Dict").Item("two").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hp.CreateWatcher(new ManualTicks()).Current, Is.EqualTo(500));
            Assert.That(two.CreateWatcher(new ManualTicks()).Current, Is.EqualTo(2));
            Assert.That(Data.Member("IntSet").Item(22).Unwrap().CreateWatcher(new ManualTicks()).Current, Is.True);
            Assert.That(Data.Member("Items").Count.Unwrap().CreateWatcher(new ManualTicks()).Current, Is.EqualTo(2));
            Assert.That(hp.ToString(), Is.EqualTo("GWorld.GameMode.Data.Items[1].Hp"));
            Assert.That(two.ToString(), Is.EqualTo("GWorld.GameMode.Data.Dict[\"two\"]"));
        }
    }

    // A long index, a string key and a bool key find their items. A missing key and an index past the count still fail the read.
    [Test]
    public void Item_KeyKinds_FindArrayMapAndSetItems()
    {
        var missingKey = Data.Member("Dict").Item("four").Unwrap().CreateWatcher(new ManualTicks());
        var pastCount = Data.Member("IntArray").Item(3).Unwrap().CreateWatcher(new ManualTicks());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Data.Member("IntArray").Item(2L).Unwrap().CreateWatcher(new ManualTicks()).Current, Is.EqualTo(300));
            Assert.That(Data.Member("IntDict").Item(5).Unwrap().CreateWatcher(new ManualTicks()).Current, Is.EqualTo(50));
            Assert.That(missingKey.Current, Is.Null);
            Assert.That(missingKey.Error?.Message, Does.EndWith("Dict doesn't have the key \"four\"."));
            Assert.That(pastCount.Current, Is.Null);
            Assert.That(pastCount.Error?.Message, Does.EndWith("IntArray doesn't have index 3. It has 3 elements."));
        }
    }

    private static IEnumerable<KindCase> KindValues()
    {
        yield return new KindCase("Hp", FakeGame.Hp);
        yield return new KindCase("Speed", FakeGame.Speed);
        yield return new KindCase("Int8Value", FakeGame.Int8Value);
        yield return new KindCase("Int16Value", FakeGame.Int16Value);
        yield return new KindCase("UInt16Value", FakeGame.UInt16Value);
        yield return new KindCase("UInt32Value", FakeGame.UInt32Value);
        yield return new KindCase("Int64Value", FakeGame.Int64Value);
        yield return new KindCase("UInt64Value", FakeGame.UInt64Value);
        yield return new KindCase("DoubleValue", FakeGame.DoubleValue);
        yield return new KindCase("ByteValue", FakeGame.ByteValue);
        yield return new KindCase("BoolValue", true);
        yield return new KindCase("bSecond", false);
        yield return new KindCase("Kind", FakeGame.Kind);
        yield return new KindCase("WideKind", FakeGame.WideKind);
        yield return new KindCase("LongKind", FakeGame.LongKind);
        yield return new KindCase("StringValue", FakeGame.StringText);
        yield return new KindCase("NameValue", FakeGame.NameText);
        yield return new KindCase("Coord.X", FakeGame.CoordX);
        yield return new KindCase("IntArray", (int[])[100, 200, 300]);
        yield return new KindCase("StringArray", (string[])["a", "b", "c"]);
        yield return new KindCase("IntSet", (HashSet<int>)[11, 22, 33]);
        yield return new KindCase("IntDict", new Dictionary<int, int> { [4] = 40, [5] = 50, [6] = 60 });
        yield return new KindCase("NameDict", new Dictionary<string, int> { [FakeGame.NameText] = 7, ["Hp_4"] = 8 });
        yield return new KindCase("Position", new StructValue("Vector", ["X", "Y", "Z"], [1f, 2f, 3f]));
        yield return new KindCase("Mixed", new StructValue("FixtureMixed", ["A", "B"], [1, 2f]));
    }

    // Get each member after GWorld.GameMode.Data like a script line does, with Coord.X for a member of a struct.
    private Watcher<object?> Watch(string member, ManualTicks? ticks = null)
    {
        var path = Data;

        foreach (string name in member.Split('.'))
        {
            path = path.Member(name);
        }

        return path.Unwrap().CreateWatcher(ticks ?? new ManualTicks());
    }
}
