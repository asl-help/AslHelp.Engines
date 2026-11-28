using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using AslHelp.Memory;
using AslHelp.Memory.Locating;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AslHelp.Engines.Measuring.Cli.Tests;

public sealed class CommandLineProfilesTests
{
    private const string First = "engine-1.0-0123456789abcdef";
    private const uint GlobalRva = 0x2000;
    private const int ImageSize = 0x4000;
    private const string Second = "engine-2.0-fedcba9876543210";

    private static readonly string[] _bareVersions = ["1.0", "2.0"];
    private static readonly string[] _bothFiles = ["engine-1.0.json", "engine-2.0.json"];
    private static readonly string[] _firstVersion = ["1.0"];
    private static readonly Address _globalAddress = new(CodeBuilder.DefaultBase + GlobalRva);
    private static readonly string[] _keyedFile = ["engine-1.0-win-x64.json"];
    private static readonly string[] _reportFile = ["report.json"];
    private static readonly string[] _secondFile = ["engine-2.0.json"];

    private StringWriter _error = null!;
    private TestFolder _folder = null!;
    private string? _key;
    private int _measured;
    private StringWriter _output = null!;
    private string _profiles = null!;
    private string _report = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _output = new StringWriter();
        _error = new StringWriter();
        _key = null;
        _measured = 0;
        _profiles = Path.Combine(_folder.Root, "out", "profiles");
        _report = Path.Combine(_folder.Root, "report.json");
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _error.Dispose();
        _folder.Delete();
    }

    // Each group gets 1 document in the profiles folder that the run creates and the report still comes out.
    [Test]
    public async Task RunAsync_Profiles_WritesOneDocumentPerGroup()
    {
        int code = await RunAsync(
            ["--profiles", _profiles],
            (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28),
            (Second, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x2C));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(FileNames(_profiles), Is.EquivalentTo(_bothFiles));
            Assert.That(_output.ToString(), Does.Contain($"profiles: 2 in '{_profiles}'"));
            Assert.That(File.Exists(_report), Is.True);
        }
    }

    // Without the option the run only writes the report.
    [Test]
    public async Task RunAsync_WithoutProfiles_WritesOnlyTheReport()
    {
        int code = await RunAsync([], (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(FileNames(_folder.Root), Is.EqualTo(_reportFile));
            Assert.That(Directory.Exists(Path.Combine(_folder.Root, "out")), Is.False);
            Assert.That(_output.ToString(), Does.Not.Contain("profiles:"));
        }
    }

    // 2 versions that differ only by case get 1 file on Windows, so the run fails before it writes any document.
    [Test]
    public async Task RunAsync_TwoGroupsSameFileName_FailsAndWritesNoDocument()
    {
        int code = await RunAsync(
            ["--profiles", _profiles],
            ("engine-2.0-BETA-0123456789abcdef", new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28),
            ("engine-2.0-beta-fedcba9876543210", new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x2C));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_error.ToString(), Does.Contain("[2.0-BETA]").And.Contain("[2.0-beta]").And.Contain(".json"));
            Assert.That(FileNames(_profiles), Is.Empty);
            Assert.That(File.Exists(_report), Is.True);
        }
    }

    // A failed player fails the run but the measured groups still get their documents.
    [Test]
    public async Task RunAsync_FailedPlayer_StillWritesTheProfilesOfTheRest()
    {
        int code = await RunAsync(["--profiles", _profiles], (First, null, 0x28), (Second, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(FileNames(_profiles), Is.EqualTo(_secondFile));
        }
    }

    // The locator stays readable in the raw file, with the question marks unescaped and without the report's search details.
    [Test]
    public async Task RunAsync_RawProfile_HasThePlainSignature()
    {
        int code = await RunAsync(["--profiles", _profiles], (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        string profile = await File.ReadAllTextAsync(Path.Combine(_profiles, "engine-1.0.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(profile, Does.Contain("\"forward\": \"8B 0D ????????\""));
            Assert.That(profile, Does.Contain("\"offset\": 2"));
            Assert.That(profile, Does.Contain("\"read\": \"global\""));
            Assert.That(profile, Does.Not.Contain("reached"));
            Assert.That(profile, Does.Not.Contain("file.bin"));
        }
    }

    // The x64 locator reads the RIP-relative displacement and gets to the RVA the route reached.
    [Test]
    public async Task RunAsync_X64Route_LocatesTheGlobalWithMemoryLocator()
    {
        var builder = new CodeBuilder(true).MovRax(GlobalRva).Ret();

        int code = await RunAsync(["--profiles", _profiles], (First, builder, 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(await LocateAsync("engine-1.0.json", builder, MemoryLayout.X64), Is.EqualTo(_globalAddress));
        }
    }

    // The x86 locator reads the absolute address and gets to the RVA the route reached.
    [Test]
    public async Task RunAsync_X86Route_LocatesTheGlobalWithMemoryLocator()
    {
        var builder = new CodeBuilder(false).MovEcx(GlobalRva).Ret();

        int code = await RunAsync(["--profiles", _profiles], (First, builder, 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(await LocateAsync("engine-1.0.json", builder, MemoryLayout.X86), Is.EqualTo(_globalAddress));
        }
    }

    // MemoryLocator parses a route with a ModRM wildcard from the register tier and uses it in the version it covers.
    [Test]
    public async Task RunAsync_RegisterWildcardRoute_ParsesAndLocates()
    {
        var builder = new CodeBuilder(false).Raw(0xB8).MovEdx(GlobalRva).Ret();

        int code = await RunAsync(
            ["--profiles", _profiles],
            (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28),
            (Second, builder, 0x28));

        string profile = await File.ReadAllTextAsync(Path.Combine(_profiles, "engine-1.0.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(profile, Does.Contain("\"forward\": \"8B ?? ????????\""));
            Assert.That(await LocateAsync("engine-1.0.json", builder, MemoryLayout.X86), Is.EqualTo(_globalAddress));
        }
    }

    // A profiles path that is a file fails the run before any player is measured, so a long run doesn't end without its documents.
    [Test]
    public async Task RunAsync_ProfilesPathIsAFile_FailsBeforeMeasuring()
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(_profiles)!);
        await File.WriteAllTextAsync(_profiles, "file");

        int code = await RunAsync(["--profiles", _profiles], (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(_measured, Is.Zero);
            Assert.That(File.Exists(_report), Is.False);
            Assert.That(_error.ToString(), Is.Not.Empty);
        }
    }

    // A folder in the way of a document fails the run after the report, which stays on disk.
    [Test]
    public async Task RunAsync_FolderInPlaceOfDocument_FailsAndKeepsTheReport()
    {
        _ = Directory.CreateDirectory(Path.Combine(_profiles, "engine-1.0.json"));

        int code = await RunAsync(["--profiles", _profiles], (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(File.Exists(_report), Is.True);
            Assert.That(_error.ToString(), Does.Contain("engine-1.0.json"));
            Assert.That(_output.ToString(), Does.Not.Contain("profiles:"));
        }
    }

    // A run where every player failed doesn't have a group, so it writes 0 documents into the folder it created.
    [Test]
    public async Task RunAsync_EveryPlayerFailed_WritesNoDocument()
    {
        int code = await RunAsync(["--profiles", _profiles], (First, null, 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(_profiles), Is.Empty);
            Assert.That(_output.ToString(), Does.Contain($"profiles: 0 in '{_profiles}'"));
        }
    }

    // A document of an older run stays in the folder, because the run only writes its own documents.
    [Test]
    public async Task RunAsync_DocumentOfOlderRun_LeavesItAlone()
    {
        _ = Directory.CreateDirectory(_profiles);
        string older = Path.Combine(_profiles, "engine-0.9.json");
        await File.WriteAllTextAsync(older, "old");

        int code = await RunAsync(["--profiles", _profiles], (First, new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(await File.ReadAllTextAsync(older), Is.EqualTo("old"));
        }
    }

    // The run cuts the key off the versions and route versions of a keyed group, puts it once in the file name and leaves out empty shapes.
    [Test]
    public async Task RunAsync_KeyedGroup_WritesBareVersionsAndKeyInFileName()
    {
        _key = "win-x64";

        int code = await RunAsync(
            ["--profiles", _profiles],
            ("engine-1.0-win-x64-0123456789abcdef", new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28),
            ("engine-2.0-win-x64-fedcba9876543210", new CodeBuilder(false).MovEcx(GlobalRva).Ret(), 0x28));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_profiles, "engine-1.0-win-x64.json")));
        var root = json.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(FileNames(_profiles), Is.EqualTo(_keyedFile));
            Assert.That(root.GetProperty("key").GetString(), Is.EqualTo("win-x64"));
            Assert.That(root.GetProperty("versions").EnumerateArray().Select(static version => version.GetString()), Is.EqualTo(_bareVersions));
            Assert.That(
                root.GetProperty("globals").GetProperty("Global").EnumerateObject().Select(static route => route.Name),
                Is.EqualTo(_firstVersion));
            Assert.That(root.TryGetProperty("shapes", out _), Is.False);
        }
    }

    // An x64 global below the code gets a negative displacement and the locator still reaches the RVA in the report.
    [Test]
    public async Task RunAsync_X64GlobalBelowCode_LocatesTheReachedRva()
    {
        const uint CodeRva = 0x3000;
        var builder = new CodeBuilder(true, rva: CodeRva).MovRax(GlobalRva).Ret();

        int code = await RunAsync(["--profiles", _profiles], (First, builder, 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(
                await LocateAsync("engine-1.0.json", builder, MemoryLayout.X64, CodeRva),
                Is.EqualTo(new Address(CodeBuilder.DefaultBase + await ReachedAsync())));
        }
    }

    // The locator of an x86 route through an immediate address, like push, reads the address as absolute and reaches the RVA in the report.
    [Test]
    public async Task RunAsync_X86ImmediateRoute_LocatesTheReachedRva()
    {
        var builder = new CodeBuilder(false).Push(GlobalRva).Ret();

        int code = await RunAsync(["--profiles", _profiles], (First, builder, 0x28));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.Zero, _error.ToString());
            Assert.That(
                await LocateAsync("engine-1.0.json", builder, MemoryLayout.X86),
                Is.EqualTo(new Address(CodeBuilder.DefaultBase + await ReachedAsync())));
        }
    }

    private static string?[] FileNames(string directory)
    {
        return [.. Directory.GetFiles(directory).Select(Path.GetFileName)];
    }

    private Task<int> RunAsync(string[] options, params (string Folder, CodeBuilder? Builder, int Offset)[] players)
    {
        Dictionary<string, (string Path, int Offset)> measured = [with(StringComparer.Ordinal)];

        foreach ((string folder, var builder, int offset) in players)
        {
            if (builder is null)
            {
                _ = Directory.CreateDirectory(Path.Combine(_folder.Root, folder));
            }
            else
            {
                measured[folder] = (Executable(folder, builder), offset);
            }
        }

        return CommandLine.RunAsync(
            ["engine", _folder.Root, "--output", _report, "--min-literals", "1", .. options],
            _output,
            _error,
            [
                new ScriptedMeasurer(directory =>
                {
                    _measured++;

                    if (!measured.TryGetValue(Path.GetFileName(directory), out var player))
                    {
                        return new TextError("not measured");
                    }

                    return new PlayerMeasurement(
                        new Dictionary<string, IReadOnlyList<uint>> { ["Global"] = [GlobalRva] },
                        new Dictionary<string, int> { ["Type.Member"] = player.Offset },
                        new Dictionary<string, string>(),
                        player.Path);
                })
                {
                    Key = _key,
                },
            ],
            new WriterLogger(_output, _error));
    }

    private string Executable(string folder, CodeBuilder builder)
    {
        string directory = Path.Combine(_folder.Root, folder);
        _ = Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "file.bin");
        CodeBuilder.WritePe(path, builder.Build());

        return path;
    }

    // Reads the RVA that the 1st route of Global reached in version 1.0 from the report.
    private async Task<ulong> ReachedAsync()
    {
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(_report));
        var route = report.RootElement.GetProperty("groups")[0].GetProperty("routes").GetProperty("Global").GetProperty("routes")[0];
        string reached = route.GetProperty("reached").GetProperty("1.0").GetString()!;

        return ulong.Parse(reached.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    // Locates Global with the locator of the profile in the code of builder, mapped at the preferred base like the file.
    private async Task<Address> LocateAsync(string fileName, CodeBuilder builder, MemoryLayout layout, uint codeRva = CodeBuilder.CodeRva)
    {
        using var profile = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_profiles, fileName)));
        var locator = JsonSerializer.Deserialize<MemoryLocator>(profile.RootElement.GetProperty("globals").GetProperty("Global").GetRawText())!;

        byte[] image = new byte[ImageSize];
        builder.Section().Bytes.Span.CopyTo(image.AsSpan((int)codeRva));
        BufferMemory memory = new(new Address(CodeBuilder.DefaultBase), image, layout);

        return locator.Locate(memory, new MemoryModule("file.bin", "file.bin", memory.Range)).Unwrap();
    }
}
