using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealEnginePinsTests
{
    // The legendary recipe runs the pins of the install, so `just legendary auth` logs in the legendary that installs.
    [Test]
    public void LegendaryRecipe_Pins_MatchTheInstall()
    {
        string run =
            $"uv run --no-project --python {UnrealEngine.PythonVersion} --with legendary-gl=={UnrealEngine.LegendaryVersion} legendary @args";

        Assert.That(ReadResource("justfile"), Does.Contain(run));
    }

    // The readme repeats the pins of the install, so a bumped pin without a changed readme fails here.
    [Test]
    public void Readme_Pins_MatchTheInstall()
    {
        string pins = $"uv gets Python {UnrealEngine.PythonVersion} and legendary {UnrealEngine.LegendaryVersion} on its own.";

        Assert.That(ReadResource("readme.md"), Does.Contain(pins));
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(UnrealEnginePinsTests).Assembly.GetManifestResourceStream(name)!;
        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }
}
