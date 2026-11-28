using AslHelp.Engines.Measuring.Cli.Tests.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using AslHelp.Engines.Measuring.Cli.Unreal4;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealMeasurerTests
{
    private const string ConstructedTargets = """
        {
          "GUObjectArray": { "$type": "global", "constructedType": "FUObjectArray", "constructedFrom": "4.19", "constructedUntil": "4.19" }
        }
        """;

    private const uint GlobalRva = 0x2000;
    private const string Hash = "0123456789abcdef";

    // Lists the types that complete in every 4.19 x64 Shipping player.
    private const string PinnedTargets = """
        {
          "GUObjectArray": { "$type": "global" },
          "GWorld": { "$type": "global" },
          "GEngine": { "$type": "global" },
          "NameTable": { "$type": "functionStatic", "function": "FName::GetNames" },
          "UObjectBase": {
            "$type": "layout",
            "members": { "ObjectFlags": {}, "InternalIndex": {}, "ClassPrivate": {}, "NamePrivate": {}, "OuterPrivate": {} }
          },
          "UField": { "$type": "layout", "members": { "Next": {} } },
          "UStruct": { "$type": "layout", "members": { "SuperStruct": {}, "Children": {}, "PropertiesSize": {} } },
          "UProperty": {
            "$type": "layout",
            "members": { "ArrayDim": {}, "ElementSize": {}, "PropertyFlags": {}, "Offset_Internal": {} }
          },
          "FUObjectItem": { "$type": "layout", "members": { "Object": {}, "SerialNumber": {} } }
        }
        """;

    private const string PlayerVariable = "FIXTURES_UNREAL_PLAYER";
    private const string TargetsFile = """
        {
          "GWorld": { "$type": "global" },
          "Player": { "$type": "layout", "members": { "Member": {} } },
          "Later": { "$type": "layout", "from": "4.25", "members": {} }
        }
        """;

    private static readonly string[] _archAndConfig = ["4.19.2-win-x86-development"];

    private static readonly string[] _everyPlayer =
    [
        "4.19.2-win-x64-development",
        "4.19.2-win-x64-shipping",
        "4.19.2-win-x86-development",
        "4.19.2-win-x86-shipping",
    ];

    private static readonly Guid _guid = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly string[] _keys = ["win-x64-shipping", "win-x86-shipping"];
    private static readonly Guid _otherGuid = new("11111111-2222-3333-4444-555555555555");
    private static readonly uint[] _sections = [CodeBuilder.CodeRva, GlobalRva];
    private static readonly string[] _sizeAndMember = ["Member", "sizeof"];
    private static readonly string[] _x64Versions = ["4.19.2-win-x64-shipping"];
    private static readonly string[] _x86Versions = ["4.19.2-win-x86-shipping"];

    private TestFolder _folder = null!;

    private string Report => Path.Combine(_folder.Root, "report.json");

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

    // A null path loads the built-in list.
    [Test]
    public void LoadTargets_Null_UsesTheEmbeddedList()
    {
        UnrealMeasurer measurer = new();

        measurer.LoadTargets(null).Unwrap();

        Assert.That(measurer.Targets, Does.ContainKey("GUObjectArray").And.ContainKey("UProperty"));
    }

    // A file swaps the list without a rebuild.
    [Test]
    public void LoadTargets_File_ReplacesTheEmbeddedList()
    {
        string path = Path.Combine(_folder.Root, "targets.json");
        File.WriteAllText(path, TargetsFile);
        UnrealMeasurer measurer = new();

        measurer.LoadTargets(path).Unwrap();

        Assert.That(measurer.Targets, Has.Count.EqualTo(3));
    }

    // A file in a folder with spaces and letters outside ASCII loads like any other.
    [Test]
    public void LoadTargets_PathWithSpacesAndUnicode_Loads()
    {
        string folder = Path.Combine(_folder.Root, "tärgets フォルダ");
        _ = Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "targets.json");
        File.WriteAllText(path, TargetsFile);
        UnrealMeasurer measurer = new();

        measurer.LoadTargets(path).Unwrap();

        Assert.That(measurer.Targets, Has.Count.EqualTo(3));
    }

    // A bad list stops the run before any player gets read, so the run doesn't write a report.
    [TestCase("")]
    [TestCase("{}")]
    [TestCase("not JSON")]
    [TestCase("""{ "Later": { "$type": "layout", "from": "4", "members": {} } }""")]
    public async Task RunAsync_BadTargetsFile_WritesNoReport(string json)
    {
        string targets = Path.Combine(_folder.Root, "targets.example");
        await File.WriteAllTextAsync(targets, json);
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, $"unreal4-4.19.2-win-x64-shipping-{Hash}"));

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", targets);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error, Does.Contain(targets).And.Not.Contain("Unhandled"));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // A folder or a missing file as the list fails without a stack trace or a report.
    [Test]
    public async Task RunAsync_TargetsPathNotAFile_WritesNoReport()
    {
        string missing = Path.Combine(_folder.Root, "missing.example");
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, $"unreal4-4.19.2-win-x64-shipping-{Hash}"));

        var (folderCode, _, folderError) = await RunAsync(_folder.Root, "--targets", _folder.Root);
        var (missingCode, _, missingError) = await RunAsync(_folder.Root, "--targets", missing);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(folderCode, Is.EqualTo(1));
            Assert.That(folderError, Does.Not.Contain("Unhandled"));
            Assert.That(missingCode, Is.EqualTo(1));
            Assert.That(missingError, Does.Contain(missing).And.Not.Contain("Unhandled"));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // A PDB of another build fails the row with both identities, so a stale PDB never reports offsets of the wrong build.
    [Test]
    public async Task MeasureAsync_IdentityMismatch_FailsWithBothValues()
    {
        var player = WritePlayer("4.19.2-win-x64-shipping", new CodeBuilder(true).Ret(), _guid, 1, _otherGuid, 2);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(SR.FormatPdbIdentityMismatch(_guid, 1, _otherGuid, 2)));
    }

    // A folder without the executable fails with the path it looked at.
    [Test]
    public async Task MeasureAsync_MissingExecutable_FailsWithThePath()
    {
        string directory = Path.Combine(_folder.Root, "player");
        _ = Directory.CreateDirectory(directory);
        string expected = Path.Combine(directory, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.exe");

        PlayerFolder player = new(directory, "4.19.2-win-x64-shipping");

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatExecutableNotFound(expected)));
    }

    // An executable without the PDB beside it fails with the path of the PDB.
    [Test]
    public async Task MeasureAsync_MissingPdb_FailsWithThePath()
    {
        var player = WritePlayer("4.19.2-win-x64-shipping", new CodeBuilder(true).Ret(), _guid, 1, _guid, 1);
        string pdb = Path.Combine(player.Directory, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.pdb");
        File.Delete(pdb);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatPdbNotFound(pdb)));
    }

    // A version that isn't a player id fails before any file read.
    [Test]
    public async Task MeasureAsync_BadPlayerId_FailsWithTheId()
    {
        var result = await Measurer().MeasureAsync(new PlayerFolder(_folder.Root, "x"), NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatPlayerIdInvalid("x")));
    }

    // A file that isn't a PE file fails the row with its path.
    [Test]
    public async Task MeasureAsync_ExecutableNotPe_FailsWithThePath()
    {
        var player = WritePlayer("4.19.2-win-x64-shipping", new CodeBuilder(true).Ret(), _guid, 1, _guid, 1);
        string executable = Path.Combine(player.Directory, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.exe");
        await File.WriteAllBytesAsync(executable, new byte[16]);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Does.Contain(executable));
    }

    // An executable with a good identity for another machine fails the read of its code, as a row and not as an exception.
    [Test]
    public async Task MeasureAsync_ExecutableOfOtherMachine_FailsWithThePath()
    {
        var player = WritePlayer("4.19.2-win-x64-shipping", new CodeBuilder(true).Ret(), _guid, 1, _guid, 1);
        string executable = Path.Combine(player.Directory, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.exe");
        FakeExecutable.Write(executable, new CodeBuilder(true).Ret(), _guid, 1, Machine.Arm64);

        var result = await Measurer().MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableMachineUnsupported(executable)));
    }

    // A canceled run stops before the files get read.
    [Test]
    public void MeasureAsync_Canceled_Throws()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        Assert.That(
            async () => await Measurer().MeasureAsync(new PlayerFolder(_folder.Root, "x"), NullLogger.Instance, canceled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // Without options every player stays and each option narrows on its own.
    [TestCase(new string[0], 4)]
    [TestCase(new[] { "--arch", "x86" }, 2)]
    [TestCase(new[] { "--config", "development" }, 2)]
    public void SelectPlayers_ArchAndConfig_NarrowThePlayers(string[] args, int expected)
    {
        var selected = Select(args, _everyPlayer);

        Assert.That(selected, Has.Count.EqualTo(expected));
    }

    // Both options keep the 1 player with both values.
    [Test]
    public void SelectPlayers_ArchAndConfigTogether_KeepsThePlayerWithBoth()
    {
        var selected = Select(["--arch", "x86", "--config", "development"], _everyPlayer);

        Assert.That(selected.Select(static player => player.Version), Is.EqualTo(_archAndConfig));
    }

    // A folder whose version isn't a player id gets dropped with a log line, because it can't be a player of this engine and the rest can still run.
    [Test]
    public void SelectPlayers_UnparsableVersion_SkipsIt()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        UnrealMeasurer measurer = new();
        RootCommand root = [.. measurer.Options];

        var selected = measurer.SelectPlayers(
            [new PlayerFolder("folder", "garbage"), new PlayerFolder("folder", "4.19.2-win-x64-shipping")],
            root.Parse([]),
            new WriterLogger(output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected.Select(static player => player.Version), Is.EqualTo(_x64Versions));
            Assert.That(output.ToString(), Does.Contain("skipped: garbage: "));
        }
    }

    // The key is the tag of the player id, so a group stays within 1 architecture and configuration.
    [Test]
    public void GroupOf_PlayerId_ReturnsTheTag()
    {
        Assert.That(new UnrealMeasurer().GroupOf("4.19.2-win-x86-development"), Is.EqualTo("win-x86-development"));
    }

    // The built-in list groups on what the engine library reads, so the size of a game type or the flags of an object don't split a group.
    [TestCase("UObjectBase", "ClassPrivate", true)]
    [TestCase("UObjectBase", "InternalIndex", true)]
    [TestCase("FUObjectItem", "sizeof", true)]
    [TestCase("FProperty", "Offset_Internal", true)]
    [TestCase("FChunkedFixedUObjectArray", "NumElementsPerChunk", true)]
    [TestCase("FNameDebugVisualizer", "OffsetBits", true)]
    [TestCase("UObjectBase", "ObjectFlags", false)]
    [TestCase("UProperty", "PropertyFlags", false)]
    [TestCase("UObjectBase", "sizeof", false)]
    [TestCase("ACharacter", "sizeof", false)]
    [TestCase("UObjectBase", "Unlisted", false)]
    [TestCase("GWorld", "sizeof", false)]
    [TestCase("Unlisted", "sizeof", false)]
    public void IsNeeded_EmbeddedList_ChecksTheValuesTheLibraryReads(string type, string member, bool expected)
    {
        Assert.That(Measurer().IsNeeded(type, member), Is.EqualTo(expected));
    }

    // A value that the option doesn't list stops the parse, so a typo doesn't drop every player.
    [TestCase("--arch", "arm64")]
    [TestCase("--config", "test")]
    public async Task RunAsync_OptionWithWrongValue_FailsParse(string option, string value)
    {
        var (code, _, error) = await RunAsync(_folder.Root, option, value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error, Does.Contain(value));
            Assert.That(File.Exists(Report), Is.False);
        }
    }

    // The options show up in the help of the subcommand.
    [Test]
    public async Task RunAsync_SubcommandHelp_ListsArchAndConfig()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(["unreal4", "--help"], output, error, [new UnrealMeasurer()], new WriterLogger(output, error));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("--arch").And.Contain("--config").And.Contain(UnrealSR.ArchDescription));
        }
    }

    // 2 players with equal PDBs and another build stay in 2 groups, each with its key, its shapes and the route of its global.
    [Test]
    public async Task RunAsync_ReportJson_HasKeyAndShapesPerGroup()
    {
        // Put fillers in front of the load, because a window ends at the return and a route needs 6 literals.
        _ = WritePlayer("4.19.2-win-x64-shipping", new CodeBuilder(true).Fillers(2).MovRax(GlobalRva).Ret(), _guid, 1, _guid, 1);
        _ = WritePlayer("4.19.2-win-x86-shipping", new CodeBuilder(false).Fillers(3).MovEcx(GlobalRva).Ret(), _guid, 1, _guid, 1);
        string targets = Path.Combine(_folder.Root, "targets.example");
        await File.WriteAllTextAsync(targets, TargetsFile);

        var (code, output, error) = await RunAsync(_folder.Root, "--targets", targets);

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var row = report.RootElement.GetProperty("players")[0];
        var groups = report.RootElement.GetProperty("groups");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(report.RootElement.GetProperty("engine").GetString(), Is.EqualTo("unreal4"));
            Assert.That(
                row.GetProperty("offsets").GetProperty("Player").EnumerateObject().Select(static member => member.Name),
                Is.EqualTo(_sizeAndMember));
            Assert.That(row.GetProperty("globals").GetProperty("GWorld")[0].GetString(), Is.EqualTo("0x2000"));
            Assert.That(row.GetProperty("shapes").GetProperty("FUObjectArray").GetString(), Is.EqualTo("flat"));
            Assert.That(row.GetProperty("unmeasured").EnumerateObject(), Is.Empty);
            Assert.That(groups.EnumerateArray().Select(static group => group.GetProperty("key").GetString()), Is.EqualTo(_keys));
            Assert.That(groups[0].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_x64Versions));
            Assert.That(groups[1].GetProperty("versions").EnumerateArray().Select(static entry => entry.GetString()), Is.EqualTo(_x86Versions));
            Assert.That(groups[0].GetProperty("shapes").GetProperty("FField").GetString(), Is.EqualTo("uproperty"));
            Assert.That(groups[0].GetProperty("routes").GetProperty("GWorld").GetProperty("routes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(groups[1].GetProperty("routes").GetProperty("GWorld").GetProperty("routes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(output, Does.Contain("[win-x64-shipping: 4.19.2-win-x64-shipping]"));
        }
    }

    // The option narrows the run, so only 1 of the 2 players gets a row.
    [Test]
    public async Task RunAsync_ArchOption_MeasuresOnlyThatArch()
    {
        _ = WritePlayer("4.19.2-win-x64-shipping", new CodeBuilder(true).MovRax(GlobalRva).Ret(), _guid, 1, _guid, 1);
        _ = WritePlayer("4.19.2-win-x86-shipping", new CodeBuilder(false).MovEcx(GlobalRva).Ret(), _guid, 1, _guid, 1);
        string targets = Path.Combine(_folder.Root, "targets.example");
        await File.WriteAllTextAsync(targets, TargetsFile);

        var (code, _, error) = await RunAsync(_folder.Root, "--targets", targets, "--arch", "x86");

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Report));
        var players = report.RootElement.GetProperty("players");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, error);
            Assert.That(players.GetArrayLength(), Is.EqualTo(1));
            Assert.That(players[0].GetProperty("version").GetString(), Is.EqualTo("4.19.2-win-x86-shipping"));
        }
    }

    // The values of the real 4.19 x64 Shipping player, pinned from its PDB and executable.
    [Test]
    public async Task MeasureAsync_RealPlayer_PinsTheValues()
    {
        string targets = Path.Combine(_folder.Root, "targets.example");
        await File.WriteAllTextAsync(targets, PinnedTargets);
        UnrealMeasurer measurer = new();
        measurer.LoadTargets(targets).Unwrap();

        var measurement = (await measurer.MeasureAsync(RealPlayer(), NullLogger.Instance, CancellationToken.None)).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Globals["GUObjectArray"].Single(), Is.EqualTo(0x2ABE500));
            Assert.That(measurement.Globals["GWorld"].Single(), Is.EqualTo(0x2AC7F58));
            Assert.That(measurement.Globals["GEngine"].Single(), Is.EqualTo(0x2AD6E78));
            Assert.That(measurement.Globals["NameTable"].Single(), Is.EqualTo(0x2AAFDD8));
            Assert.That(measurement.Offsets["UObjectBase.sizeof"], Is.EqualTo(0x28));
            Assert.That(measurement.Offsets["UObjectBase.ObjectFlags"], Is.EqualTo(0x8));
            Assert.That(measurement.Offsets["UObjectBase.InternalIndex"], Is.EqualTo(0xC));
            Assert.That(measurement.Offsets["UObjectBase.ClassPrivate"], Is.EqualTo(0x10));
            Assert.That(measurement.Offsets["UObjectBase.NamePrivate"], Is.EqualTo(0x18));
            Assert.That(measurement.Offsets["UObjectBase.OuterPrivate"], Is.EqualTo(0x20));
            Assert.That(measurement.Offsets["UField.Next"], Is.EqualTo(0x28));
            Assert.That(measurement.Offsets["UStruct.sizeof"], Is.EqualTo(0x88));
            Assert.That(measurement.Offsets["UStruct.SuperStruct"], Is.EqualTo(0x30));
            Assert.That(measurement.Offsets["UStruct.Children"], Is.EqualTo(0x38));
            Assert.That(measurement.Offsets["UStruct.PropertiesSize"], Is.EqualTo(0x40));
            Assert.That(measurement.Offsets["UProperty.sizeof"], Is.EqualTo(0x70));
            Assert.That(measurement.Offsets["UProperty.ArrayDim"], Is.EqualTo(0x30));
            Assert.That(measurement.Offsets["UProperty.ElementSize"], Is.EqualTo(0x34));
            Assert.That(measurement.Offsets["UProperty.PropertyFlags"], Is.EqualTo(0x38));
            Assert.That(measurement.Offsets["UProperty.Offset_Internal"], Is.EqualTo(0x44));
            Assert.That(measurement.Offsets["FUObjectItem.sizeof"], Is.EqualTo(0x18));
            Assert.That(measurement.Offsets["FUObjectItem.Object"], Is.Zero);
            Assert.That(measurement.Offsets["FUObjectItem.SerialNumber"], Is.EqualTo(0x10));
            Assert.That(measurement.Shapes["FUObjectArray"], Is.EqualTo("flat"));
            Assert.That(measurement.Shapes["FNameEntry"], Is.EqualTo("indirect"));
            Assert.That(measurement.Shapes["FField"], Is.EqualTo("uproperty"));
        }
    }

    // The constructor of the object array runs on the public on 4.19, so the read through its call finds the same RVA as the public.
    [Test]
    public async Task MeasureAsync_RealPlayerThroughTheConstructor_FindsThePublic()
    {
        string targets = Path.Combine(_folder.Root, "targets.example");
        await File.WriteAllTextAsync(targets, ConstructedTargets);
        UnrealMeasurer measurer = new();
        measurer.LoadTargets(targets).Unwrap();

        var measurement = (await measurer.MeasureAsync(RealPlayer(), NullLogger.Instance, CancellationToken.None)).Unwrap();

        Assert.That(measurement.Globals["GUObjectArray"].Single(), Is.EqualTo(0x2ABE500));
    }

    // The built-in list measures a player of the fixture project.
    [Test]
    public async Task MeasureAsync_RealPlayerWithBuiltInList_MeasuresEveryTargetInRange()
    {
        var player = RealPlayer();
        UnrealMeasurer measurer = new();
        measurer.LoadTargets(null).Unwrap();

        var result = await measurer.MeasureAsync(player, NullLogger.Instance, CancellationToken.None);

        Assert.That(result.Unwrap().Offsets, Does.ContainKey("UEngine.sizeof"));
    }

    private static PlayerFolder RealPlayer()
    {
        string? player = Environment.GetEnvironmentVariable(PlayerVariable);
        Assume.That(player is not null && Directory.Exists(player), $"Set {PlayerVariable} to a 4.19 x64 Shipping player folder to run this test.");

        return new PlayerFolder(player!, "4.19.2-win-x64-shipping");
    }

    private static List<PlayerFolder> Select(string[] args, string[] versions)
    {
        UnrealMeasurer measurer = new();
        RootCommand root = [.. measurer.Options];

        PlayerFolder[] players = [.. versions.Select(static version => new PlayerFolder("folder", version))];

        return [.. measurer.SelectPlayers(players, root.Parse(args), NullLogger.Instance)];
    }

    private static UnrealMeasurer Measurer()
    {
        UnrealMeasurer measurer = new();
        measurer.LoadTargets(null).Unwrap();

        return measurer;
    }

    private PlayerFolder WritePlayer(string version, CodeBuilder code, Guid pdbGuid, int pdbAge, Guid executableGuid, int executableAge)
    {
        string directory = Path.Combine(_folder.Root, $"unreal4-{version}-{Hash}");
        var id = PlayerId.Parse(version);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(id.ExecutablePath(directory))!);

        FakePdb pdb = new(pdbGuid, _sections) { DbiAge = (uint)pdbAge };
        pdb.AddPublic("?GWorld@@3VUWorldProxy@@A", 2, 0, isFunction: false);
        pdb.AddStruct("Player", 0x28, pdb.AddFieldList(FakePdb.Member("Member", 0x10)));
        pdb.Write(id.PdbPath(directory));

        FakeExecutable.Write(id.ExecutablePath(directory), code, executableGuid, executableAge);

        return new PlayerFolder(directory, version);
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(string players, params string[] options)
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int code = await CommandLine.RunAsync(
            ["unreal4", players, "--output", Report, .. options],
            output,
            error,
            [new UnrealMeasurer()],
            new WriterLogger(output, error));

        return (code, output.ToString(), error.ToString());
    }
}
