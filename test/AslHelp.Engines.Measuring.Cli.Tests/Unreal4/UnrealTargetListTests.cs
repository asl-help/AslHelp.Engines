using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed partial class UnrealTargetListTests
{
    private const uint ConstructedRva = 0x2800;

    // The offset of the constructor in the code, behind the 8 bytes of the function that reads the name table.
    private const uint ConstructorOffset = 8;

    private const uint DataRva = 0x2000;
    private const string Decorated = "?GetNames@FName@@CAAEAVNames@@XZ";
    private const string FixtureTypes = "eng/fixtures/unreal4/Source/Fixture/FixtureTypes.cpp";

    // The types that the fixture completes under another spelling, with that spelling.
    private static readonly Dictionary<string, string> _completedThrough = new()
    {
        ["FScriptSparseSetLayout"] = "FScriptSetLayout",
        ["TScriptSparseSet<FDefaultSetAllocator>"] = "FScriptSet",
        ["TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>"] = "FUObjectArray",
    };

    private static readonly IReadOnlyDictionary<string, UnrealTarget> _embedded = UnrealTargets.ReadEmbedded().Unwrap();

    private static readonly string[] _gameTypes =
    [
        "UEngine",
        "UGameEngine",
        "ULocalPlayer",
        "AGameMode",
        "AGameStateBase",
        "AGameState",
        "ACharacter",
        "APlayerState",
        "UPackage",
        "UDataTable",
        "AWorldSettings",
        "UMovementComponent",
        "UCharacterMovementComponent",
        "FWorldContext",
        "UGameViewportClient",
    ];

    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");

    private static readonly KeyValuePair<string, UnrealLayoutTarget>[] _layouts =
    [
        .. _embedded
            .Where(static target => target.Value is UnrealLayoutTarget)
            .Select(static target => KeyValuePair.Create(target.Key, (UnrealLayoutTarget)target.Value)),
    ];

    // The minors that get a fixture player.
    private static readonly string[] _minors =
    [
        .. Enumerable.Range(0, 28).Select(static minor => $"4.{minor}"),
        .. Enumerable.Range(0, 9).Select(static minor => $"5.{minor}"),
    ];

    private static readonly (string Type, string Member)[] _profileMembers =
    [
        ("UObjectBase", "ClassPrivate"),
        ("UObjectBase", "NamePrivate"),
        ("UObjectBase", "OuterPrivate"),
        ("UStruct", "SuperStruct"),
        ("UStruct", "ChildProperties"),
        ("UStruct", "PropertiesSize"),
        ("FField", "ClassPrivate"),
        ("FField", "Next"),
        ("FField", "NamePrivate"),
        ("FFieldClass", "Name"),
        ("FProperty", "ElementSize"),
        ("FProperty", "Offset_Internal"),
        ("FProperty", "PropertyLinkNext"),
        ("FBoolProperty", "ByteOffset"),
        ("FBoolProperty", "FieldMask"),
        ("FObjectProperty", "PropertyClass"),
        ("FStructProperty", "Struct"),
        ("FArrayProperty", "Inner"),
        ("FMapProperty", "KeyProp"),
        ("FMapProperty", "ValueProp"),
        ("FMapProperty", "MapLayout"),
        ("FSetProperty", "ElementProp"),
        ("FSetProperty", "SetLayout"),
        ("FScriptMapLayout", "ValueOffset"),
        ("FScriptMapLayout", "SetLayout"),
        ("FScriptSetLayout", "ElementOffset"),
        ("FScriptSetLayout", "Size"),
        ("FString", "Data"),
        ("FScriptArray", "Data"),
        ("FScriptArray", "ArrayNum"),
        ("FScriptArray", "ArrayMax"),
        ("FScriptSparseArray", "Data"),
        ("FScriptSparseArray", "AllocationFlags"),
        ("FScriptSparseArray", "FirstFreeIndex"),
        ("FScriptSparseArray", "NumFreeIndices"),
        ("FScriptBitArray", "AllocatorInstance"),
        ("FScriptBitArray", "NumBits"),
        ("FScriptBitArray", "MaxBits"),
        ("FScriptSet", "Elements"),
        ("FScriptMap", "Pairs"),
        ("FWeakObjectPtr", "ObjectIndex"),
        ("FWeakObjectPtr", "ObjectSerialNumber"),
        ("UEnum", "Names"),
        ("FByteProperty", "Enum"),
        ("FEnumProperty", "UnderlyingProp"),
        ("FEnumProperty", "Enum"),
        ("FUObjectArray", "ObjObjects"),
        ("FChunkedFixedUObjectArray", "Objects"),
        ("FChunkedFixedUObjectArray", "NumElements"),
        ("FChunkedFixedUObjectArray", "NumChunks"),
        ("FUObjectItem", "Object"),
        ("FUObjectItem", "SerialNumber"),
        ("FNameEntry", "AnsiName"),
        ("FNameEntry", "WideName"),
        ("FNameEntry", "Header"),
        ("FNameEntry", "Index"),
        ("UGameInstance", "SubsystemCollection"),
        ("FSubsystemCollectionBase", "SubsystemMap"),
    ];

    private static readonly string[] _sizedTypes = ["FUObjectItem"];
    private static readonly string[] _sourceLines = File.ReadAllLines(FindFixtureTypes());
    private static readonly string[] _unneededMembers = ["UObjectBase.ObjectFlags", "UProperty.PropertyFlags", "FProperty.PropertyFlags"];

    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // The list covers the game types that plugins read, so a plugin can pick its sizes per group.
    [Test]
    public void Embedded_GameTypes_AreAllListed()
    {
        Assert.That(_embedded.Keys, Is.SupersetOf(_gameTypes));
    }

    // The list covers every member the engine library reads, so a profile can come out of a report.
    [Test]
    public void Embedded_MembersOfTheProfiles_AreAllListed()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach ((string type, string member) in _profileMembers)
            {
                Assert.That(((UnrealLayoutTarget)_embedded[type]).Members, Does.ContainKey(member), type);
            }
        }
    }

    // The groups follow the members of the profiles and the size of an object item, because the engine library reads only those.
    [Test]
    public void Embedded_NeededValues_AreTheProfileMembersAndTheItemSize()
    {
        string[] unneeded = [.. _layouts.SelectMany(static layout => layout.Value.Members
            .Where(static member => !member.Value.Needed)
            .Select(member => $"{layout.Key}.{member.Key}"))];
        string[] sized = [.. _layouts.Where(static layout => layout.Value.SizeNeeded).Select(static layout => layout.Key)];

        using (Assert.EnterMultipleScope())
        {
            foreach ((string type, string member) in _profileMembers)
            {
                Assert.That(((UnrealLayoutTarget)_embedded[type]).Members[member].Needed, Is.True, $"{type}.{member}");
            }

            Assert.That(unneeded, Is.EquivalentTo(_unneededMembers));
            Assert.That(sized, Is.EqualTo(_sizedTypes));
        }
    }

    // A range ends on a minor with a fixture player, because a typo such as 4.28 wouldn't fail on any player.
    [Test]
    public void Embedded_RangeEnds_AreRealMinors()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach ((string name, var target) in _embedded)
            {
                AssertRealMinor(name, target.From);
                AssertRealMinor(name, target.Until);

                if (target is UnrealGlobalTarget global)
                {
                    AssertRealMinor(name, global.ConstructedFrom);
                    AssertRealMinor(name, global.ConstructedUntil);
                }

                if (target is UnrealLayoutTarget layout)
                {
                    foreach ((string member, var range) in layout.Members)
                    {
                        AssertRealMinor($"{name}.{member}", range.From);
                        AssertRealMinor($"{name}.{member}", range.Until);
                    }
                }
            }
        }
    }

    // A member with a range outside its type would never get measured and the report wouldn't show why.
    [Test]
    public void Embedded_MemberRanges_FitTheirType()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach ((string type, var layout) in _layouts)
            {
                foreach ((string member, var range) in layout.Members)
                {
                    bool overlaps = _minors.Any(minor => Within(minor, layout.From, layout.Until)
                        && Within(minor, range.From, range.Until));

                    Assert.That(overlaps, Is.True, $"{type}.{member}");
                }
            }
        }
    }

    // A type only shows in the PDB when the fixture completes it, so the fixture has to keep it on each minor in its range.
    [Test]
    public void Embedded_LayoutsInRange_AreKeptByTheFixture()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (string minor in _minors)
            {
                var kept = KeptTypes(Version.Parse(minor));

                foreach ((string type, var layout) in _layouts)
                {
                    if (Within(minor, layout.From, layout.Until))
                    {
                        Assert.That(kept, Does.Contain(_completedThrough.GetValueOrDefault(type, type)), $"{type} on {minor}");
                    }
                }
            }
        }
    }

    // The report lists a type outside its range as missing on that minor, so the fixture can't compile it there.
    [Test]
    public void Embedded_LayoutsOutsideRange_AreLeftOutByTheFixture()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (string minor in _minors)
            {
                var kept = KeptTypes(Version.Parse(minor));

                foreach ((string type, var layout) in _layouts)
                {
                    if (!Within(minor, layout.From, layout.Until) && !_completedThrough.ContainsValue(type))
                    {
                        Assert.That(kept, Does.Not.Contain(type), $"{type} on {minor}");
                    }
                }
            }
        }
    }

    // 4.8 to 4.10 keep the object array behind a static inside GetUObjectArray(), so its constructor shows the RVA without a public.
    [TestCase("4.8")]
    [TestCase("4.9")]
    [TestCase("4.10")]
    public void Measure_ObjectArrayBehindAGetter_ReadsTheStaticOfTheConstructor(string minor)
    {
        var measurement = Measure(minor, "GUObjectArray");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Globals["GUObjectArray"].Single(), Is.EqualTo(ConstructedRva));
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // The minors around the getter list the public, so they use it and a static of the constructor doesn't count.
    [TestCase("4.7")]
    [TestCase("4.11")]
    public void Measure_ObjectArrayAroundTheGetter_ReadsThePublic(string minor)
    {
        Assert.That(Measure(minor).Globals["GUObjectArray"].Single(), Is.Not.EqualTo(ConstructedRva));
    }

    // The minors around the getter list the public, so a miss there fails the row.
    [TestCase("4.7")]
    [TestCase("4.11")]
    public void Measure_ObjectArrayAroundTheGetter_FailsWithoutThePublic(string minor)
    {
        Assert.That(MeasureResult(minor, "GUObjectArray").UnwrapErr().Message, Is.EqualTo(SR.FormatPdbSymbolNotFound("GUObjectArray")));
    }

    // 4.23 and later keep the names in a file-static pool without a public, so its constructor shows the RVA.
    [TestCase("4.23")]
    [TestCase("5.8")]
    public void Measure_NamePoolFrom423_ReadsTheStaticOfTheConstructor(string minor)
    {
        Assert.That(Measure(minor).Globals["NamePoolData"].Single(), Is.EqualTo(ConstructedRva));
    }

    // PDBs from 5.7 on spell the set types another way, so the row passes without the old spellings and reports the new ones.
    [TestCase("5.7")]
    [TestCase("5.8")]
    public void Measure_SetTypesFrom57_ReadTheNewNames(string minor)
    {
        var measurement = Measure(minor, "FScriptSetLayout", "FScriptSet");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Does.ContainKey("FScriptSparseSetLayout.Size"));
            Assert.That(measurement.Offsets, Does.ContainKey("TScriptSparseSet<FDefaultSetAllocator>.Elements"));
        }
    }

    // PDBs through 5.6 keep the old spelling of the set types, so the row passes without the new ones.
    [TestCase("4.8")]
    [TestCase("5.6")]
    public void Measure_SetTypesThrough56_ReadTheOldNames(string minor)
    {
        var measurement = Measure(minor, "FScriptSparseSetLayout", "TScriptSparseSet<FDefaultSetAllocator>");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Does.ContainKey("FScriptSetLayout.Size"));
            Assert.That(measurement.Offsets, Does.ContainKey("FScriptSet.Elements"));
        }
    }

    // 4.8 to 4.10 keep the objects in the table that the engine library reads under the keys of the chunked array.
    [TestCase("4.8")]
    [TestCase("4.10")]
    public void Measure_IndirectObjectTable_ReportsItsMembers(string minor)
    {
        var measurement = Measure(minor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Does.ContainKey("TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>.Chunks"));
            Assert.That(measurement.Offsets, Does.ContainKey("TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>.NumElements"));
            Assert.That(measurement.Offsets, Does.ContainKey("TStaticIndirectArrayThreadSafeRead<UObjectBase,8388608,16384>.NumChunks"));
        }
    }

    // Every value of the list shows up on every minor inside its range and nowhere outside it.
    [TestCaseSource(nameof(_minors))]
    public void Measure_EmbeddedListOnEveryMinor_ReportsOrSkipsEachValue(string minor)
    {
        var measurement = Measure(minor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Unmeasured, Is.Empty);

            foreach ((string name, var target) in _embedded)
            {
                bool inRange = IsInRange(minor, target);
                string reportKey = target is UnrealConstantTarget ? UnrealConstantTarget.ReportKey(name)! : name;

                if (target is UnrealLayoutTarget layout)
                {
                    Assert.That(measurement.Offsets.ContainsKey($"{name}.sizeof"), Is.EqualTo(inRange), name);

                    foreach ((string member, var range) in layout.Members)
                    {
                        bool memberInRange = Within(minor, range.From, range.Until);
                        string key = $"{name}.{member}";

                        Assert.That(measurement.Offsets.ContainsKey(key), Is.EqualTo(inRange && memberInRange), key);
                    }
                }
                else if (target is UnrealConstantTarget)
                {
                    Assert.That(measurement.Offsets.ContainsKey(reportKey), Is.EqualTo(inRange), reportKey);
                }
                else
                {
                    Assert.That(measurement.Globals.ContainsKey(name), Is.EqualTo(inRange), name);
                }
            }
        }
    }

    [GeneratedRegex(@"FIXTURE_KEEP_TYPE\((\w+)\)")]
    private static partial Regex KeepType();

    [GeneratedRegex(@"^//@if\s*(>=|<=|==|<|>)\s*(\d+\.\d+)$")]
    private static partial Regex IfLine();

    private static bool IsInRange(string minor, UnrealTarget target)
    {
        return Within(minor, target.From, target.Until);
    }

    private static bool Within(string minor, string? from, string? until)
    {
        return UnrealVersions.Within(Version.Parse(minor), UnrealVersions.ParseMinor(from), UnrealVersions.ParseMinor(until));
    }

    private static void AssertRealMinor(string name, string? minor)
    {
        if (minor is not null)
        {
            Assert.That(_minors, Does.Contain(minor), name);
        }
    }

    // Applies the //@if lines of the fixture source for 1 minor, the same way the fixture gets rendered.
    private static HashSet<string> KeptTypes(Version minor)
    {
        HashSet<string> kept = [with(StringComparer.Ordinal)];
        Stack<bool> gates = [];

        foreach (string line in _sourceLines)
        {
            string trimmed = line.Trim();

            if (IfLine().Match(trimmed) is { Success: true } gate)
            {
                var bound = Version.Parse(gate.Groups[2].Value);

                gates.Push(gate.Groups[1].Value switch
                {
                    ">=" => minor >= bound,
                    "<=" => minor <= bound,
                    "==" => minor == bound,
                    "<" => minor < bound,
                    _ => minor > bound,
                });
            }
            else if (trimmed == "//@else")
            {
                gates.Push(!gates.Pop());
            }
            else if (trimmed == "//@endif")
            {
                _ = gates.Pop();
            }
            else if (gates.All(static open => open) && KeepType().Match(trimmed) is { Success: true } keep)
            {
                _ = kept.Add(keep.Groups[1].Value);
            }
        }

        return kept;
    }

    private static string FindFixtureTypes()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, FixtureTypes);

            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(FixtureTypes, AppContext.BaseDirectory);
    }

    private PlayerMeasurement Measure(string minor, params string[] leftOut)
    {
        return MeasureResult(minor, leftOut).Unwrap();
    }

    // Measures a PDB with every target of the list except the ones to leave out.
    private Result<PlayerMeasurement> MeasureResult(string minor, params string[] leftOut)
    {
        FakePdb pdb = new(_guid, [CodeBuilder.CodeRva, DataRva]);
        pdb.AddPublic("??0FUObjectArray@@QEAA@XZ", 1, ConstructorOffset, isFunction: true);
        pdb.AddPublic("??0FNamePool@@QEAA@XZ", 1, ConstructorOffset, isFunction: true);
        uint at = 0;

        foreach ((string name, var target) in _embedded.Where(target => !leftOut.Contains(target.Key)))
        {
            switch (target)
            {
                // Leave out the public of a static that goes through its constructor on every version, because the static never has one.
                case UnrealGlobalTarget { ConstructedType: not null, ConstructedFrom: null }:
                {
                    break;
                }
                case UnrealGlobalTarget:
                {
                    pdb.AddPublic($"?{name}@@3HA", 2, at += 8, isFunction: false);
                    break;
                }
                case UnrealFunctionStaticTarget:
                {
                    pdb.AddPublic(Decorated, 1, 0, isFunction: true);
                    break;
                }
                case UnrealConstantTarget:
                {
                    pdb.AddConstant(name, 0x10, 0x00);
                    break;
                }
                case UnrealLayoutTarget layout:
                {
                    var minorVersion = Version.Parse(minor);
                    var members = layout.Members.Select((member, index) => FakePdb.Member(member.Value.PdbName(member.Key, minorVersion), (ulong)index * 8));
                    pdb.AddStruct(name, 0x100, pdb.AddFieldList([.. members]));
                    break;
                }
            }
        }

        string path = Path.Combine(_folder.Root, "file.pdb");
        pdb.Write(path);

        using var file = PdbFile.Open(path).Unwrap();

        return UnrealMeasurement.Measure(
            file,
            new CodeBuilder(true).MovRax(DataRva).Ret().Ret().LeaRcx(ConstructedRva).CallTo(CodeBuilder.CodeRva + ConstructorOffset).Build(),
            new Version($"{minor}.0"),
            _embedded,
            "file.bin",
            NullLogger.Instance,
            CancellationToken.None);
    }
}
