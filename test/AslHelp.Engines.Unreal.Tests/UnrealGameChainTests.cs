using AslHelp.Memory;
using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Dynamic;

namespace AslHelp.Engines.Unreal.Tests;

[TestFixtureSource(nameof(_fixtures))]
public sealed class UnrealGameChainTests(
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

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeGame(memoryLayout, start, shape);
        _game = _fakeGame.Attach();
    }

    // A chain prints the same text as the path with the same members.
    [Test]
    public void GWorldChain_MembersAfterTheChain_PrintsThePath()
    {
        object chain = _game.GWorldChain.GameMode.Data.Hp;

        Assert.That(chain.ToString(), Is.EqualTo(_game.GWorld.Member("GameMode").Member("Data").Member<int>("Hp").ToString()));
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

    // The chain goes through the game mode and data pointers and then into the Coord struct by its offset.
    [Test]
    public void CreateWatcher_ThroughObjectsAndStruct_ReadsTheValue()
    {
        var watcher = Watch("Coord.Y");

        Assert.That(watcher.Current, Is.EqualTo(FakeGame.CoordY));
    }

    // A chain that ends on a struct of mixed members fails with the struct and a hint to go on into a field.
    [Test]
    public void CreateWatcher_ChainEndsOnStruct_FailsWithContinueMessage()
    {
        var watcher = Watch("Mixed");
        watcher.FailAction = FailAction.Throw;

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.StartsWith(
                "Couldn't read GWorld.GameMode.Data.Mixed. Can't read GWorld.GameMode.Data.Mixed. "
                + "Mixed is the struct FixtureMixed. Continue the path into 1 of its fields."));
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

    // A misspelled member doesn't throw on the script line. It fails when the watcher reads, with the class and the name.
    [Test]
    public void CreateWatcher_MisspelledMember_FailsWhenTheWatcherReads()
    {
        var watcher = Watch("Hpp");
        watcher.FailAction = FailAction.Throw;

        Assert.That(
            () => watcher.Current,
            Throws.InvalidOperationException.With.Message.StartsWith(
                "Couldn't read GWorld.GameMode.Data.Hpp. "
                + "Can't read GWorld.GameMode.Data.Hpp. Class FixtureData doesn't have a property Hpp."));
    }

    // CreateWatcher throws for the chain of the global alone, because it doesn't end on a member.
    [Test]
    public void CreateWatcher_ChainOfTheGlobalAlone_Throws()
    {
        IWatcherSource world = _game.GWorldChain;

        Assert.That(
            () => world.CreateWatcher(new ManualTicks()),
            Throws.InvalidOperationException.With.Message.EqualTo("Can't watch GWorld: it isn't a property."));
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
        yield return new KindCase("Position", (float[])[1f, 2f, 3f]);
    }

    // An index hop goes into an array element, a key into a map value or a set entry and Count reads the number of elements.
    [Test]
    public void GWorldChain_IndexAndKeyHops_WatchElements()
    {
        dynamic data = _game.GWorldChain.GameMode.Data;
        object hp = data.Items[1].Hp;
        object two = data.Dict["two"];
        object hasKey = data.IntSet[22];
        object count = data.Items.Count;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Read(hp), Is.EqualTo(500));
            Assert.That(Read(two), Is.EqualTo(2));
            Assert.That(Read(hasKey), Is.True);
            Assert.That(Read(count), Is.EqualTo(2));
            Assert.That(hp.ToString(), Is.EqualTo("GWorld.GameMode.Data.Items[1].Hp"));
            Assert.That(two.ToString(), Is.EqualTo("GWorld.GameMode.Data.Dict[\"two\"]"));
        }
    }

    // A double can't be an index or a key, so the script line throws instead of a watcher that always fails.
    [Test]
    public void GWorldChain_UnsupportedIndexType_ThrowsOnTheScriptLine()
    {
        dynamic data = _game.GWorldChain.GameMode.Data;

        Assert.That(
            () => { object _ = data.Items[1.5]; },
            Throws.InvalidOperationException.With.Message.EqualTo("Can't index Items with a Double. Use an integer, a bool or a string."));
    }

    private static object? Read(object chain)
    {
        return ((IWatcherSource)chain).CreateWatcher(new ManualTicks()) is Watcher<object?> watcher ? watcher.Current : null;
    }

    // Get each member after GWorld.GameMode.Data like a script line does, with Coord.X for a member of a struct.
    private Watcher<object?> Watch(string member, ManualTicks? ticks = null)
    {
        object chain = _game.GWorldChain.GameMode.Data;

        foreach (string name in member.Split('.'))
        {
            _ = ((DynamicObject)chain).TryGetMember(new NameBinder(name), out chain!);
        }

        return (Watcher<object?>)((IWatcherSource)chain).CreateWatcher(ticks ?? new ManualTicks());
    }
}

file sealed class NameBinder(
    string name) : GetMemberBinder(name, ignoreCase: false)
{
    public override DynamicMetaObject FallbackGetMember(DynamicMetaObject target, DynamicMetaObject? errorSuggestion)
    {
        throw new NotSupportedException();
    }
}
