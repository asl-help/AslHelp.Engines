using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Engines.Measuring.Cli.Tests.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealMeasurementEdgeTests
{
    private const uint CodeRva = 0x3000;
    private const uint DataRva = 0x2000;
    private const string Decorated = "?GetNames@FName@@CAAEAVNames@@XZ";
    private const string Function = "FName::GetNames";
    private const string Version = "4.19.2-win-x64-shipping";

    private static readonly UnrealMemberTarget _always = new();
    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly uint[] _sections = [CodeBuilder.CodeRva, DataRva, CodeRva];
    private static readonly string[] _sizeAlone = ["Player.sizeof"];

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

    // A real x64 image sits above 4 GiB, so the RVA has to come out of the whole 64-bit address.
    [Test]
    public void Measure_FunctionAboveFourGiB_ReportsTheRva()
    {
        var image = new CodeBuilder(true, imageBase: 0x1_4000_0000).MovRax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // The search looks at 512 instructions, with the read at the last one still found.
    [Test]
    public void Measure_LoadAtTheLastInstruction_ReportsIt()
    {
        var image = new CodeBuilder(true).Fillers(511).MovRax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // A read behind 512 instructions belongs to another function by then, so the search stops with its failure.
    [Test]
    public void Measure_LoadPastTheLastInstruction_Fails()
    {
        var image = new CodeBuilder(true).Fillers(512).MovRax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound(Function, "1000")));
    }

    // An instruction cut off by the end of the section fails like a function without a read and doesn't throw.
    [Test]
    public void Measure_FunctionCutInsideAnInstruction_Fails()
    {
        var image = new CodeBuilder(true).Raw(0x48, 0x8B, 0x05, 0x00).Build();

        Assert.That(MeasureFunction(image).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound(Function, "1000")));
    }

    // Bytes that don't decode end the function, so a read behind them doesn't count.
    [Test]
    public void Measure_FunctionWithBytesThatDoNotDecode_Fails()
    {
        var image = new CodeBuilder(true).Raw(0x06).MovRax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound(Function, "1000")));
    }

    // A symbol right behind the section isn't in it, so it fails with its RVA and doesn't read past the array.
    [Test]
    public void Measure_FunctionRightBehindItsSection_Fails()
    {
        var image = new CodeBuilder(true).Ret().Build();

        Assert.That(MeasureFunction(image, offset: 1).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound(Function, "1001")));
    }

    // x64 code loads the image base for a jump table and x86 code pushes it. The start of the headers isn't a static.
    [TestCase(true)]
    [TestCase(false)]
    public void Measure_FunctionReadsTheImageBase_SkipsToTheStatic(bool is64Bit)
    {
        var image = is64Bit
            ? new CodeBuilder(true).LeaRcx(0).MovRax(DataRva).Ret().Build()
            : new CodeBuilder(false).Push(0).MovEax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // The last byte of the image is still inside it.
    [Test]
    public void Measure_AddressAtTheLastByteOfTheImage_ReportsIt()
    {
        var image = new CodeBuilder(false).Push(CodeBuilder.ImageSize - 1).MovEax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(CodeBuilder.ImageSize - 1));
    }

    // The address right after the image is a plain number.
    [Test]
    public void Measure_AddressRightAfterTheImage_SkipsToTheStatic()
    {
        var image = new CodeBuilder(false).Push(CodeBuilder.ImageSize).MovEax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // The target of a call isn't a read, even when the distance reaches outside the code.
    [TestCase(true)]
    [TestCase(false)]
    public void Measure_CallBeforeTheLoad_SkipsTheCall(bool is64Bit)
    {
        var image = is64Bit
            ? new CodeBuilder(true).Call(0x1000).MovRax(DataRva).Ret().Build()
            : new CodeBuilder(false).Call(0x1000).MovEax(DataRva).Ret().Build();

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // A function in a later code section decodes from its own section and its own offset.
    [Test]
    public void Measure_FunctionInSecondCodeSection_ReportsTheStatic()
    {
        CodeSection first = new(CodeBuilder.CodeRva, (byte[])[0xC3]);
        var second = new CodeBuilder(true, rva: CodeRva).Ret().MovRax(DataRva).Ret().Section();
        CodeImage image = new(true, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, [first, second]);

        Assert.That(MeasureFunction(image, section: 3, offset: 1).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // An address in any code section is code, so a read of the 2nd section from the 1st doesn't count.
    [Test]
    public void Measure_FunctionReadsSecondCodeSection_SkipsToTheStatic()
    {
        var first = new CodeBuilder(true).LeaRcx(CodeRva).MovRax(DataRva).Ret().Section();
        CodeSection second = new(CodeRva, (byte[])[0xC3]);
        CodeImage image = new(true, CodeBuilder.DefaultBase, CodeBuilder.ImageSize, [first, second]);

        Assert.That(MeasureFunction(image).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // A version inside the constructed range reads the static without a public, on both ends of the range.
    [TestCase("4.8.3-win-x64-shipping")]
    [TestCase("4.10.4-win-x64-shipping")]
    public void Measure_GlobalInsideItsConstructedRange_ReadsTheStatic(string player)
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic("??0Table@@QEAA@XZ", 1, 0, isFunction: true);

        var image = new CodeBuilder(true).Ret().LeaRcx(DataRva).CallTo(CodeBuilder.CodeRva).Build();

        var measurement = Measure(pdb, Constructed(), image, player).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Globals["Global"].Single(), Is.EqualTo(DataRva));
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // A version inside the constructed range without the constructor fails with its decorated name, because a person can look that up.
    [Test]
    public void Measure_GlobalInsideItsConstructedRange_FailsWithoutTheConstructor()
    {
        string message = Measure(new FakePdb(_guid, _sections), Constructed(), id: "4.8.3-win-x64-shipping").UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbSymbolNotFound("??0Table@@QEAA@XZ")));
    }

    // A version right outside the constructed range needs the public, so the row fails without it.
    [TestCase("4.7.6-win-x64-shipping")]
    [TestCase("4.11.2-win-x64-shipping")]
    public void Measure_GlobalRightOutsideItsConstructedRange_FailsWithoutThePublic(string player)
    {
        string message = Measure(new FakePdb(_guid, _sections), Constructed(), id: player).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbSymbolNotFound("Global")));
    }

    // A version outside the range skips the target, also on a target with a constructed range.
    [Test]
    public void Measure_GlobalOutsideItsRangeWithAConstructedRange_SkipsIt()
    {
        UnrealGlobalTarget target = new(From: "4.20", ConstructedType: "Table", ConstructedFrom: "4.25", ConstructedUntil: "4.26");

        var measurement = Measure(new FakePdb(_guid, _sections), new Dictionary<string, UnrealTarget> { ["Global"] = target }).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Globals, Is.Empty);
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // A public that isn't a function doesn't start the search, because the bytes at a global aren't code.
    [Test]
    public void Measure_FunctionTargetFindsADataPublic_FailsWithItsName()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic(Decorated, 1, 0, isFunction: false);

        string message = Measure(pdb, Static(), new CodeBuilder(true).Ret().Build()).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbSymbolNotFound(Function)));
    }

    // A function doesn't count as a global, because its RVA would send the route search to code.
    [Test]
    public void Measure_GlobalTargetFindsAFunctionPublic_FailsWithItsName()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic("?GWorld@@3VUWorldProxy@@A", 2, 0, isFunction: true);

        string message = Measure(
            pdb,
            new Dictionary<string, UnrealTarget> { ["GWorld"] = new UnrealGlobalTarget() },
            new CodeBuilder(true).Ret().Build()).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbSymbolNotFound("GWorld")));
    }

    // 2 publics with 1 name and 2 RVAs fail the row with both, because either RVA could be the wrong one.
    [Test]
    public void Measure_GlobalWithTwoRvas_FailsListingBoth()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic("?GWorld@@3VUWorldProxy@@A", 2, 0, isFunction: false);
        pdb.AddPublic("?GWorld@@3VUWorldProxy@@A", 2, 0x10, isFunction: false);

        string message = Measure(
            pdb,
            new Dictionary<string, UnrealTarget> { ["GWorld"] = new UnrealGlobalTarget() },
            new CodeBuilder(true).Ret().Build()).UnwrapErr().Message;

        Assert.That(message, Does.Contain("GWorld").And.Contain("0x2000").And.Contain("0x2010"));
    }

    // 3 layouts with 2 values list each value once and in the order of the PDB.
    [Test]
    public void Measure_ThreeLayoutsWithTwoValues_ListsEachValueOnce()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10), FakePdb.Member("Other", 0x18)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x8)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10)));

        string message = Measure(pdb, Layout("Member")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatLayoutsDiffer("Player", "Member", "0x10, 0x8")));
    }

    // A member that only 1 of 2 layouts has fails, because the other compile unit disagrees about the type.
    [Test]
    public void Measure_MemberInOneOfTwoLayouts_Fails()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Other", 0x10)));

        string message = Measure(pdb, Layout("Member")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatMemberNotFound("Player", "Member")));
    }

    // A member outside its range doesn't get compared, so layouts that disagree on it don't fail the row.
    [Test]
    public void Measure_LayoutsDisagreeOutsideTheMemberRange_Passes()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10), FakePdb.Member("Old", 0x8)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10), FakePdb.Member("Old", 0x18)));

        var targets = new Dictionary<string, UnrealTarget>
        {
            ["Player"] = new UnrealLayoutTarget(new Dictionary<string, UnrealMemberTarget>
            {
                ["Member"] = _always,
                ["Old"] = new UnrealMemberTarget(Until: "4.18"),
            }),
        };

        var measurement = Measure(pdb, targets).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets["Player.Member"], Is.EqualTo(0x10));
            Assert.That(measurement.Offsets, Does.Not.ContainKey("Player.Old"));
        }
    }

    // A base class with 2 layouts shows up on the type that derives from it, with the offset of the base added to both values.
    [Test]
    public void Measure_BaseClassWithTwoLayouts_FailsListingBoth()
    {
        FakePdb pdb = new(_guid, _sections);
        uint baseType = pdb.AddForward("Base");
        pdb.AddStruct("Base", 0x10, pdb.AddFieldList(FakePdb.Member("Inherited", 0x4)));
        pdb.AddStruct("Base", 0x10, pdb.AddFieldList(FakePdb.Member("Inherited", 0x8)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.BaseClass(baseType, 0x10)));

        string message = Measure(pdb, Layout("Inherited")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatLayoutsDiffer("Player", "Inherited", "0x14, 0x18")));
    }

    // A type with an empty member list reports its size and nothing else.
    [Test]
    public void Measure_TypeWithoutMembers_ReportsTheSizeAlone()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10)));

        var measurement = Measure(pdb, Layout()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets.Keys, Is.EqualTo(_sizeAlone));
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    private static Dictionary<string, UnrealTarget> Constructed()
    {
        return new Dictionary<string, UnrealTarget>
        {
            ["Global"] = new UnrealGlobalTarget(ConstructedType: "Table", ConstructedFrom: "4.8", ConstructedUntil: "4.10"),
        };
    }

    private static Dictionary<string, UnrealTarget> Static()
    {
        return new Dictionary<string, UnrealTarget> { ["Static"] = new UnrealFunctionStaticTarget(Function) };
    }

    private static Dictionary<string, UnrealTarget> Layout(params string[] members)
    {
        return new Dictionary<string, UnrealTarget>
        {
            ["Player"] = new UnrealLayoutTarget(members.ToDictionary(static member => member, static _ => _always)),
        };
    }

    private Result<PlayerMeasurement> MeasureFunction(CodeImage image, ushort section = 1, uint offset = 0)
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic(Decorated, section, offset, isFunction: true);

        return Measure(pdb, Static(), image);
    }

    private Result<PlayerMeasurement> Measure(FakePdb pdb, Dictionary<string, UnrealTarget> targets, CodeImage? image = null, string id = Version)
    {
        string path = Path.Combine(_folder.Root, "file.pdb");
        pdb.Write(path);

        using var file = PdbFile.Open(path).Unwrap();

        return UnrealMeasurement.Measure(
            file,
            image ?? new CodeBuilder(true).Ret().Build(),
            PlayerId.Parse(id).Version,
            targets,
            "file.bin",
            NullLogger.Instance,
            CancellationToken.None);
    }
}
