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
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealMeasurementTests
{
    private const uint DataRva = 0x2000;
    private const string Decorated = "?GetNames@FName@@CAAEAVNames@@XZ";
    private const string Executable = "file.bin";
    private const uint PublicRva = 0x2010;

    private static readonly UnrealMemberTarget _always = new();
    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly uint[] _sections = [0x1000, 0x2000, 0x3000];

    private TestFolder _folder = null!;

    private string PdbPath => Path.Combine(_folder.Root, "file.pdb");

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

    // The report has the offset of a member and the size of its type under the key sizeof.
    [Test]
    public void Measure_MemberInRange_ReportsOffsetAndSizeof()
    {
        var measurement = Measure("4.19.2-win-x64-shipping", Type(0x28, ("Member", 0x10)), Layout("Player", "Member")).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets["Player.Member"], Is.EqualTo(0x10));
            Assert.That(measurement.Offsets["Player.sizeof"], Is.EqualTo(0x28));
            Assert.That(measurement.ExecutablePath, Is.EqualTo(Executable));
        }
    }

    // A member a base class declares reads through the type of the target, with the offset of the base added.
    [Test]
    public void Measure_MemberOfBaseClass_ReportsItUnderTheType()
    {
        FakePdb pdb = new(_guid, _sections);
        uint baseType = pdb.AddForward("Base");
        pdb.AddStruct("Base", 0x10, pdb.AddFieldList(FakePdb.Member("Inherited", 0x8)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.BaseClass(baseType, 0x10)));

        var measurement = Measure("4.19.2-win-x64-shipping", pdb, Layout("Player", "Inherited")).Unwrap();

        Assert.That(measurement.Offsets["Player.Inherited"], Is.EqualTo(0x18));
    }

    // A type outside its range drops out of the row and the row passes, without a look at the PDB.
    [Test]
    public void Measure_TypeOutsideRange_SkipsIt()
    {
        var targets = new Dictionary<string, UnrealTarget>
        {
            ["Later"] = new UnrealLayoutTarget(new Dictionary<string, UnrealMemberTarget>(), From: "4.25"),
            ["Earlier"] = new UnrealLayoutTarget(new Dictionary<string, UnrealMemberTarget>(), Until: "4.18"),
            ["Between"] = new UnrealLayoutTarget(new Dictionary<string, UnrealMemberTarget>(), From: "4.8", Until: "4.18"),
        };

        var measurement = Measure("4.19.2-win-x64-shipping", Pdb(), targets).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Is.Empty);
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // A global or a static outside its range drops out too, so an old list doesn't fail a newer player.
    [Test]
    public void Measure_GlobalAndStaticOutsideRange_SkipsThem()
    {
        var targets = new Dictionary<string, UnrealTarget>
        {
            ["Global"] = new UnrealGlobalTarget(Until: "4.10"),
            ["Static"] = new UnrealFunctionStaticTarget("FName::GetNames", Until: "4.18"),
        };

        var measurement = Measure("4.19.2-win-x64-shipping", Pdb(), targets).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Globals, Is.Empty);
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // A type in range without a record fails the row with the error of the PDB.
    [Test]
    public void Measure_TypeMissingInRange_FailsWithTheType()
    {
        string message = Measure("4.19.2-win-x64-shipping", Pdb(), Layout("Missing")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbTypeNotFound("Missing")));
    }

    // A forward reference alone fails the row with the error of the PDB.
    [Test]
    public void Measure_ForwardOnlyInRange_FailsWithTheType()
    {
        FakePdb pdb = new(_guid, _sections);
        _ = pdb.AddForward("Forward");

        string message = Measure("4.19.2-win-x64-shipping", pdb, Layout("Forward")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbTypeForwardOnly("Forward")));
    }

    // A member outside its range drops out alone and the other members of the type stay.
    [Test]
    public void Measure_MemberOutsideRange_SkipsOnlyThatMember()
    {
        var targets = new Dictionary<string, UnrealTarget>
        {
            ["Player"] = new UnrealLayoutTarget(new Dictionary<string, UnrealMemberTarget>
            {
                ["Member"] = _always,
                ["Old"] = new UnrealMemberTarget(Until: "4.22"),
            }),
        };

        var measurement = Measure("4.25.1-win-x64-shipping", Type(0x28, ("Member", 0x10), ("Old", 0x18)), targets).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets, Does.ContainKey("Player.Member"));
            Assert.That(measurement.Offsets, Does.Not.ContainKey("Player.Old"));
            Assert.That(measurement.Unmeasured, Is.Empty);
        }
    }

    // A member in range that the PDB lacks fails the row, because the fix is 1 line in the list and the report would hide it.
    [Test]
    public void Measure_MemberMissingInRange_Fails()
    {
        string message = Measure("4.19.2-win-x64-shipping", Type(0x28, ("Member", 0x10)), Layout("Player", "Other")).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatMemberNotFound("Player", "Other")));
    }

    // A PDB up to the end of the old name lists the member under it, so the report keeps the new name with the offset of the old one.
    [TestCase("4.0.2-win-x64-shipping")]
    [TestCase("4.7.4-win-x64-shipping")]
    public void Measure_MemberWithOldNameUpToItsEnd_ReportsTheOldMemberUnderTheNewName(string id)
    {
        var measurement = Measure(id, Type(0x28, ("Renamed", 0x10), ("Old", 0x18)), Renamed()).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Offsets["Player.Renamed"], Is.EqualTo(0x18));
            Assert.That(measurement.Offsets, Does.Not.ContainKey("Player.Old"));
        }
    }

    // A PDB after the end of the old name lists the member under the new name, even beside another member with the old name.
    [Test]
    public void Measure_MemberWithOldNameAfterItsEnd_ReadsTheNewName()
    {
        var measurement = Measure("4.8.3-win-x64-shipping", Type(0x28, ("Renamed", 0x10), ("Old", 0x18)), Renamed()).Unwrap();

        Assert.That(measurement.Offsets["Player.Renamed"], Is.EqualTo(0x10));
    }

    // A PDB up to the end that lists only the new name fails on the old one, because an end too early would hide a rename.
    [Test]
    public void Measure_MemberWithOldNameMissingUpToItsEnd_FailsOnTheOldName()
    {
        string message = Measure("4.7.4-win-x64-shipping", Type(0x28, ("Renamed", 0x10)), Renamed()).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatMemberNotFound("Player", "Old")));
    }

    // A PDB after the end that lists only the old name fails on the new one, because an end too late would hide it too.
    [Test]
    public void Measure_MemberWithOnlyTheOldNameAfterItsEnd_FailsOnTheNewName()
    {
        string message = Measure("4.8.3-win-x64-shipping", Type(0x28, ("Old", 0x10)), Renamed()).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatMemberNotFound("Player", "Renamed")));
    }

    // 2 compile units that disagree on 1 member fail with both offsets, because either value in the report could be wrong.
    [Test]
    public void Measure_TwoLayoutsDisagree_FailsListingBoth()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x8)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10)));

        string message = Measure("4.19.2-win-x64-shipping", pdb, Layout("Player", "Member")).UnwrapErr().Message;

        Assert.That(message, Does.Contain("Player.Member").And.Contain("0x8").And.Contain("0x10"));
    }

    // 2 compile units with other sizes fail on sizeof, because the distance between array items isn't known then.
    [Test]
    public void Measure_TwoLayoutsWithOtherSizes_FailsListingBoth()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x8)));
        pdb.AddStruct("Player", 0x30, pdb.AddFieldList(FakePdb.Member("Member", 0x8)));

        string message = Measure("4.19.2-win-x64-shipping", pdb, Layout("Player", "Member")).UnwrapErr().Message;

        Assert.That(message, Does.Contain("Player.sizeof").And.Contain("0x28").And.Contain("0x30"));
    }

    // 2 layouts that agree on every reported value pass, because other members can differ in 2 compile units.
    [Test]
    public void Measure_TwoLayoutsAgree_Passes()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10), FakePdb.Member("Extra", 0x18)));
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10), FakePdb.Member("Other", 0x20)));

        var measurement = Measure("4.19.2-win-x64-shipping", pdb, Layout("Player", "Member")).Unwrap();

        Assert.That(measurement.Offsets["Player.Member"], Is.EqualTo(0x10));
    }

    // A global reads from the public symbols as the RVA of its section plus its offset, on x64 and x86.
    [TestCase(true)]
    [TestCase(false)]
    public void Measure_Globals_ReadsPublicsOnX64AndX86(bool is64Bit)
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic("?GWorld@@3VUWorldProxy@@A", 3, 0x10, isFunction: false);

        var measurement = Measure(
            "4.19.2-win-x64-shipping",
            pdb,
            new Dictionary<string, UnrealTarget> { ["GWorld"] = new UnrealGlobalTarget() },
            new CodeBuilder(is64Bit).Ret()).Unwrap();

        Assert.That(measurement.Globals["GWorld"].Single(), Is.EqualTo(0x3010));
    }

    // A global in range without a public fails with its name.
    [Test]
    public void Measure_GlobalMissingInRange_Fails()
    {
        string message = Measure(
            "4.19.2-win-x64-shipping",
            Pdb(),
            new Dictionary<string, UnrealTarget> { ["GWorld"] = new UnrealGlobalTarget() }).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbSymbolNotFound("GWorld")));
    }

    // The static is the 1st address the function reads, so the route search gets the table and not the function.
    [Test]
    public void Measure_FunctionStatic_ReportsTheFirstLoad()
    {
        var measurement = MeasureFunction(new CodeBuilder(true).MovRax(DataRva).Raw(0x48, 0x85, 0xC0).Ret()).Unwrap();

        Assert.That(measurement.Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // x86 code reads the static by its absolute address, in the form without a ModRM byte too.
    [Test]
    public void Measure_FunctionStaticOnX86_ReportsTheFirstLoad()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MeasureFunction(new CodeBuilder(false).MovEax(DataRva).Ret()).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
            Assert.That(MeasureFunction(new CodeBuilder(false).MovEcx(DataRva).Ret()).Unwrap().Globals["Static"].Single(), Is.EqualTo(DataRva));
        }
    }

    // A security cookie has a public and the static doesn't, so a read of the cookie before the static doesn't count.
    [TestCase(true)]
    [TestCase(false)]
    public void Measure_FunctionReadsAPublicFirst_SkipsToTheStatic(bool is64Bit)
    {
        var code = is64Bit
            ? new CodeBuilder(true).MovRax(PublicRva).MovRax(DataRva).Ret()
            : new CodeBuilder(false).MovEax(PublicRva).MovEax(DataRva).Ret();

        var measurement = MeasureFunction(code, "__security_cookie").Unwrap();

        Assert.That(measurement.Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // An import slot has a public too, so a call through it before the static doesn't count.
    [TestCase(true)]
    [TestCase(false)]
    public void Measure_FunctionCallsThroughAnImportSlotFirst_SkipsToTheStatic(bool is64Bit)
    {
        var code = is64Bit
            ? new CodeBuilder(true).Raw(0xFF, 0x15).Relative(PublicRva).MovRax(DataRva).Ret()
            : new CodeBuilder(false).Raw(0xFF, 0x15).Absolute(PublicRva).MovEax(DataRva).Ret();

        var measurement = MeasureFunction(code, "__imp_GetCurrentThreadId").Unwrap();

        Assert.That(measurement.Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // A function that only reads publics fails, because none of them is the static.
    [Test]
    public void Measure_FunctionReadsOnlyAPublic_Fails()
    {
        string message = MeasureFunction(new CodeBuilder(true).MovRax(PublicRva).Ret(), "__security_cookie").UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound("FName::GetNames", "1000")));
    }

    // An address inside the code is another function or a jump table and not the static.
    [Test]
    public void Measure_FunctionReadsCodeFirst_SkipsToTheStatic()
    {
        var measurement = MeasureFunction(new CodeBuilder(true).LeaRcx(CodeBuilder.CodeRva + 0x8).MovRax(DataRva).Ret()).Unwrap();

        Assert.That(measurement.Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // An operand past the end of the image is a plain number and not an address.
    [Test]
    public void Measure_FunctionWithNumberOutsideTheImage_SkipsToTheStatic()
    {
        var measurement = MeasureFunction(new CodeBuilder(false).Raw(0x68).Absolute(CodeBuilder.ImageSize + 0x10).MovEax(DataRva).Ret()).Unwrap();

        Assert.That(measurement.Globals["Static"].Single(), Is.EqualTo(DataRva));
    }

    // A function without a read fails with its name and RVA. Code after its return doesn't count.
    [Test]
    public void Measure_FunctionWithoutLoad_Fails()
    {
        string message = MeasureFunction(new CodeBuilder(true).Ret().MovRax(DataRva)).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound("FName::GetNames", "1000")));
    }

    // A function in another section than the code can't be decoded, so it fails like a function without a read.
    [Test]
    public void Measure_FunctionOutsideTheCode_Fails()
    {
        FakePdb pdb = new(_guid, _sections);
        pdb.AddPublic(Decorated, 3, 0, isFunction: true);

        string message = Measure(
            "4.19.2-win-x64-shipping",
            pdb,
            new Dictionary<string, UnrealTarget> { ["Static"] = new UnrealFunctionStaticTarget("FName::GetNames") },
            new CodeBuilder(true).Ret()).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatFunctionStaticNotFound("FName::GetNames", "3000")));
    }

    // A function without a public fails with its name.
    [Test]
    public void Measure_FunctionMissing_FailsWithItsName()
    {
        string message = Measure(
            "4.19.2-win-x64-shipping",
            Pdb(),
            new Dictionary<string, UnrealTarget> { ["Static"] = new UnrealFunctionStaticTarget("FName::GetNames") }).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatPdbSymbolNotFound("FName::GetNames")));
    }

    // A constant sits next to the offsets of its scope, so a reader finds the chunk size beside the chunk table.
    [Test]
    public void Measure_Constant_ReportsTheValueUnderItsScope()
    {
        var pdb = Pdb();
        pdb.AddEnum("Table::<unnamed-enum-PerChunk>", pdb.AddFieldList(FakePdb.Enumerate("PerChunk", 0x10000)));

        var measurement = Measure(
            "4.20.3-win-x64-shipping",
            pdb,
            new Dictionary<string, UnrealTarget> { ["Table::PerChunk"] = new UnrealConstantTarget() }).Unwrap();

        Assert.That(measurement.Offsets["Table.PerChunk"], Is.EqualTo(0x10000));
    }

    // A report value has 32 bits, so a larger constant fails instead of getting cut.
    [Test]
    public void Measure_ConstantPast32Bits_Fails()
    {
        var pdb = Pdb();
        pdb.AddConstant("Table::Mask", 0x09, 0x80, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00);

        string message = Measure(
            "4.20.3-win-x64-shipping",
            pdb,
            new Dictionary<string, UnrealTarget> { ["Table::Mask"] = new UnrealConstantTarget() }).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatConstantOutOfRange("Table::Mask", 0x1_0000_0000)));
    }

    // The shapes come from the version of the player.
    [Test]
    public void Measure_Shapes_ComeFromTheVersion()
    {
        var shapes = Measure("4.19.2-win-x64-shipping", Pdb(), []).Unwrap().Shapes;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shapes["FUObjectArray"], Is.EqualTo("flat"));
            Assert.That(shapes["FNameEntry"], Is.EqualTo("indirect"));
            Assert.That(shapes["FField"], Is.EqualTo("uproperty"));
        }
    }

    // A canceled token stops the measurement before the 1st target, so Ctrl+C doesn't wait for a whole target list.
    [Test]
    public void Measure_CanceledToken_ThrowsOperationCanceledException()
    {
        Type(0x28, ("Member", 0x10)).Write(PdbPath);
        using var file = PdbFile.Open(PdbPath).Unwrap();

        Assert.That(
            () => UnrealMeasurement.Measure(
                file,
                new CodeBuilder(true).Ret().Build(),
                new Version(4, 19, 2),
                Layout("Player", "Member"),
                Executable,
                NullLogger.Instance,
                new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static FakePdb Pdb()
    {
        return new FakePdb(_guid, _sections);
    }

    private static FakePdb Type(ulong size, params (string Name, ulong Offset)[] members)
    {
        var pdb = Pdb();
        pdb.AddStruct("Player", size, pdb.AddFieldList([.. members.Select(static member => FakePdb.Member(member.Name, member.Offset))]));

        return pdb;
    }

    private static Dictionary<string, UnrealTarget> Layout(string type, params string[] members)
    {
        Dictionary<string, UnrealMemberTarget> targets = [];

        foreach (string member in members)
        {
            targets[member] = _always;
        }

        return new Dictionary<string, UnrealTarget> { [type] = new UnrealLayoutTarget(targets) };
    }

    private static Dictionary<string, UnrealTarget> Renamed()
    {
        return new Dictionary<string, UnrealTarget>
        {
            ["Player"] = new UnrealLayoutTarget(new Dictionary<string, UnrealMemberTarget>
            {
                ["Renamed"] = new UnrealMemberTarget(OldName: "Old", OldNameUntil: "4.7"),
            }),
        };
    }

    private Result<PlayerMeasurement> MeasureFunction(CodeBuilder code, string? publicAtPublicRva = null)
    {
        var pdb = Pdb();
        pdb.AddPublic(Decorated, 1, 0, isFunction: true);

        if (publicAtPublicRva is not null)
        {
            pdb.AddPublic(publicAtPublicRva, 2, PublicRva - DataRva, isFunction: false);
        }

        return Measure(
            "4.19.2-win-x64-shipping",
            pdb,
            new Dictionary<string, UnrealTarget> { ["Static"] = new UnrealFunctionStaticTarget("FName::GetNames") },
            code);
    }

    private Result<PlayerMeasurement> Measure(string id, FakePdb pdb, Dictionary<string, UnrealTarget> targets, CodeBuilder? code = null)
    {
        pdb.Write(PdbPath);

        using var file = PdbFile.Open(PdbPath).Unwrap();

        return UnrealMeasurement.Measure(
            file,
            (code ?? new CodeBuilder(true).Ret()).Build(),
            PlayerId.Parse(id).Version,
            targets,
            Executable,
            NullLogger.Instance,
            CancellationToken.None);
    }
}
