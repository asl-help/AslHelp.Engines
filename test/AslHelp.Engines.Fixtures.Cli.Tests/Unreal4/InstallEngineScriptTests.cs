using AslHelp.Engines.Fixtures.Cli.Hosting;
using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class InstallEngineScriptTests
{
    private const string LegendaryConfigVariable = "LEGENDARY_CONFIG_PATH";

    // Set FIXTURES_UV_TESTS to 1 to run the script through uv. uv needs the legendary wheel in its cache or a network to get it.
    private const string UvTestsVariable = "FIXTURES_UV_TESTS";

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

    // The script exits with the codes that the UnrealEngine tests map to their messages, so a renumbered code fails without uv too.
    [TestCase("EXIT_NOT_LOGGED_IN", 10)]
    [TestCase("EXIT_NOT_PINNED", 11)]
    [TestCase("EXIT_LISTED_AS_INSTALLED", 12)]
    [TestCase("EXIT_FILES_BAD", 13)]
    public void WriteScript_ExitCode_MatchesTheCodeOfTheEngine(string name, int code)
    {
        string script = Path.Combine(_folder.Root, "install_engine.py");
        UnrealEngine.WriteScript(script);

        Assert.That(File.ReadAllLines(script), Does.Contain($"{name} = {code}"));
    }

    // The pinned legendary reads a chunk list without the size and sha lists through the patch, as Epic's 4.0 to 4.2 manifests have it.
    [Test]
    [Platform("Win")]
    public async Task CheckReader_PatchedJsonCdl_ReadsManifestsWithoutShaOrSizeLists()
    {
        Assume.That(Environment.GetEnvironmentVariable(UvTestsVariable), Is.EqualTo("1"), $"Set {UvTestsVariable} to 1 to run this test.");

        string script = Path.Combine(_folder.Root, "install_engine.py");
        UnrealEngine.WriteScript(script);
        var info = ProcessRunner.NewStartInfo("uv", UnrealEngine.UvArguments(script, "--check-reader"));

        await Assert.ThatAsync(() => ProcessRunner.RunAsync(info, CancellationToken.None), Throws.Nothing);
    }

    // The real uv and script fail with the missing login, with an empty legendary config so the login of the machine stays out of it.
    [Test]
    [Platform("Win")]
    public void InstallAsync_EmptyLegendaryConfig_ThrowsNotLoggedIn()
    {
        Assume.That(Environment.GetEnvironmentVariable(UvTestsVariable), Is.EqualTo("1"), $"Set {UvTestsVariable} to 1 to run this test.");

        using EnvironmentVariableScope environment = new(LegendaryConfigVariable);
        Environment.SetEnvironmentVariable(LegendaryConfigVariable, _folder.NewFolder());

        UnrealEngine engine = new(TextWriter.Null);
        string output = _folder.NewFolder();

        Assert.That(
            async () => await engine.InstallAsync(EmbeddedReleases<UnrealRelease>.Find("4.19"), output, new DriveRoom(output, _ => long.MaxValue, TextWriter.Null), CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("just legendary auth")
                .And.Message.Contains("No saved credentials"));
    }
}
