using AslHelp.Engines.Profiles;
using AslHelp.Memory;
using NUnit.Framework;
using System;
using System.Linq;
using System.Text.Json;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealTargetTests
{
    private const string ShippingName = "Player-Win64-Shipping.exe";

    // The fixture players stamp the engine version only in the numbers, with an empty text.
    [Test]
    public void FromFileVersion_NumbersOfUnreal4_ReadsMajorMinorPatch()
    {
        var target = FromFileVersion(null, new Version(4, 24, 3, 0));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Engine, Is.EqualTo(UnrealTarget.Unreal4));
            Assert.That(target.Version.Text, Is.EqualTo("4.24.3"));
            Assert.That(target.Key, Is.EqualTo("win-x64-shipping"));
        }
    }

    // A release version that matches the numbers keeps the patch of the numbers.
    [Test]
    public void FromFileVersion_ReleaseVersionAndMatchingNumbers_AddsThePatch()
    {
        Assert.That(FromFileVersion("++UE5+Release-5.8-CL-58902052", new Version(5, 8, 4, 0)).Version.Text, Is.EqualTo("5.8.4"));
    }

    // A game can stamp its own numbers, so a release version that doesn't match them wins and the patch stays unknown.
    [Test]
    public void FromFileVersion_ReleaseVersionAndOtherNumbers_KeepsTheReleaseVersion()
    {
        var target = FromFileVersion("++UE4+Release-4.27-CL-1", new Version(1, 3, 0, 0));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Engine, Is.EqualTo(UnrealTarget.Unreal4));
            Assert.That(target.Version.Text, Is.EqualTo("4.27"));
        }
    }

    // UDK stamps 1.0, the engine build and 0, so each measured build reads as its release. Releases that share a build read as the 1st of them.
    [TestCase(6829, "2010-06-BETA")]
    [TestCase(7258, "2010-09")]
    [TestCase(7977, "2011-03-BETA")]
    [TestCase(8710, "2011-07-BETA")]
    [TestCase(9375, "2012-01-BETA")]
    [TestCase(9953, "2012-05-BETA")]
    [TestCase(12791, "2015-02")]
    public void FromFileVersion_Unreal3Numbers_ReadsTheRelease(int build, string release)
    {
        var target = FromFileVersion($"1, 0, {build}, 0", new Version(1, 0, build, 0), "UDK.exe", 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Engine, Is.EqualTo(UnrealTarget.Unreal3));
            Assert.That(target.Version.Text, Is.EqualTo(release));
            Assert.That(target.Key, Is.EqualTo("win-x86-shipping"));
        }
    }

    // A build without a measured release, like a game on a licensed Unreal Engine 3, reads as 3 and the build.
    [Test]
    public void FromFileVersion_UnmeasuredUnreal3Build_ReadsAs3AndTheBuild()
    {
        var target = FromFileVersion("1, 0, 8225, 0", new Version(1, 0, 8225, 0), "Game.exe", 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Engine, Is.EqualTo(UnrealTarget.Unreal3));
            Assert.That(target.Version.Text, Is.EqualTo("3.8225"));
            Assert.That(target.Version.Parts, Is.EqualTo([3, 8225]));
        }
    }

    // Before 4.8 the players don't stamp anything, so detection fails with the module and the hint to pass the version.
    [Test]
    public void FromFileVersion_NoStamp_FailsWithTheModuleAndTheOverrideHint()
    {
        Assert.That(
            UnrealTarget.FromFileVersion(FileVersion(null, new Version(0, 0, 0, 0)), ShippingName, 8, null, null).UnwrapErr().Message,
            Is.EqualTo($"{ShippingName} doesn't have an engine version stamp. Pass the version, like 4.27.2."));
    }

    // A passed version wins over the stamp and picks the engine by its form, also for a file without a stamp.
    [TestCase("4.27.2", UnrealTarget.Unreal4)]
    [TestCase("2010-09", UnrealTarget.Unreal3)]
    [TestCase("3.8225", UnrealTarget.Unreal3)]
    public void FromFileVersion_VersionOverride_WinsOverTheStamp(string version, string engine)
    {
        var stamped = UnrealTarget.FromFileVersion(FileVersion("5.8.4", new Version(5, 8, 4, 0)), ShippingName, 8, version, null).Unwrap();
        var unstamped = UnrealTarget.FromFileVersion(null, ShippingName, 8, version, null).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That((stamped.Engine, stamped.Version.Text), Is.EqualTo((engine, version)));
            Assert.That((unstamped.Engine, unstamped.Version.Text), Is.EqualTo((engine, version)));
        }
    }

    // A passed version that isn't an Unreal Engine version fails with the text. A version needs a minor, because the pick compares minors.
    [TestCase("beta")]
    [TestCase("3")]
    [TestCase("4")]
    [TestCase("2010")]
    [TestCase("2.5")]
    public void FromFileVersion_VersionOverrideThatIsNotAnEngineVersion_FailsWithTheText(string version)
    {
        Assert.That(
            UnrealTarget.FromFileVersion(null, ShippingName, 8, version, null).UnwrapErr().Message,
            Is.EqualTo($"'{version}' isn't an Unreal Engine version, like 4.27.2, 2010-09 or 3.8225."));
    }

    // A passed UDK release without a measured player fails with the form to pass instead, because its build isn't known.
    [Test]
    public void FromFileVersion_UnmeasuredUdkRelease_FailsWithTheBuildForm()
    {
        Assert.That(
            UnrealTarget.FromFileVersion(null, ShippingName, 4, "2009-11-BETA", null).UnwrapErr().Message,
            Is.EqualTo("The build of UDK 2009-11-BETA isn't known. Pass 3 and the engine build, like 3.8225."));
    }

    // Every pinned UDK release with a player is measured, so its documents parse. The build refuses 2009-11-BETA, which needs PhysX.
    [Test]
    public void ParseVersion_PinnedUdkReleasesWithAPlayer_Read()
    {
        using var stream = typeof(UnrealTargetTests).Assembly.GetManifestResourceStream("unreal3-releases.json")!;
        using var document = JsonDocument.Parse(stream);

        var releases = document.RootElement.EnumerateArray()
            .Select(static row => row.GetProperty("version").GetString()!)
            .Where(static release => release != "2009-11-BETA");

        Assert.That(releases.Where(static release => UnrealTarget.ParseVersion(release) is not { Parts: [3, ..] }), Is.Empty);
    }

    // A name that ends with -Debug or -DebugGame is development and any other name is shipping unless the caller passes a configuration.
    [TestCase(8, "Player-Win64-Shipping.exe", "win-x64-shipping")]
    [TestCase(4, "Player.exe", "win-x86-shipping")]
    [TestCase(8, "Player-Win64-DebugGame.exe", "win-x64-development")]
    [TestCase(8, "Player-Win64-Debug.exe", "win-x64-development")]
    [TestCase(8, "Player-Win64-Test.exe", "win-x64-shipping")]
    [TestCase(4, "PLAYER-WIN32-SHIPPING.EXE", "win-x86-shipping")]
    public void FromFileVersion_PointerSizeAndName_BuildsTheKey(byte pointerSize, string moduleName, string key)
    {
        var target = UnrealTarget.FromFileVersion(FileVersion(null, new Version(4, 19, 2, 0)), moduleName, pointerSize, null, null).Unwrap();

        Assert.That(target.Key, Is.EqualTo(key));
    }

    // A passed configuration wins over the name.
    [Test]
    public void FromFileVersion_ConfigurationOverride_WinsOverTheName()
    {
        var target = UnrealTarget.FromFileVersion(FileVersion(null, new Version(4, 19, 2, 0)), ShippingName, 8, null, UnrealConfiguration.Development).Unwrap();

        Assert.That(target.Key, Is.EqualTo("win-x64-development"));
    }

    // The documents only cover x86 and x64, so another pointer size fails with that size.
    [Test]
    public void FromFileVersion_PointerSizeOtherThan4Or8_Fails()
    {
        Assert.That(
            UnrealTarget.FromFileVersion(FileVersion(null, new Version(4, 19, 2, 0)), ShippingName, 2, null, null).UnwrapErr().Message,
            Is.EqualTo("A pointer size of 2 bytes isn't x86 or x64."));
    }

    // Detect reads the file version of the module, so a module without a path fails with the module name.
    [Test]
    public void Detect_ModuleWithoutAPath_FailsWithTheModuleName()
    {
        MemoryModule module = new(ShippingName, null, new MemoryRange(new Address(0x1000), 0x1000));

        Assert.That(
            UnrealTarget.Detect(new FakeMemory { Layout = MemoryLayout.X64 }, module).UnwrapErr().Message,
            Is.EqualTo($"The path of module '{ShippingName}' isn't known, so its file can't be read."));
    }

    // A passed version doesn't need the stamp, so a module without a path still gets a target with the pointer size of the memory.
    [Test]
    public void Detect_VersionOverride_SkipsTheStamp()
    {
        MemoryModule module = new(ShippingName, null, new MemoryRange(new Address(0x1000), 0x1000));

        var target = UnrealTarget.Detect(new FakeMemory { Layout = MemoryLayout.X86 }, module, "4.19.2").Unwrap();

        Assert.That(target, Is.EqualTo(new ProfileTarget(UnrealTarget.Unreal4, UnrealTarget.ParseVersion("4.19.2")!, "win-x86-shipping")));
    }

    // Every run of digits is 1 part, so dots and dashes both split and the text stays as written.
    [TestCase("4.19.2", new[] { 4, 19, 2 })]
    [TestCase("3.8225", new[] { 3, 8225 })]
    [TestCase("5.8", new[] { 5, 8 })]
    public void ParseVersion_DottedAndDashedText_ReadsTheParts(string text, int[] parts)
    {
        var version = UnrealTarget.ParseVersion(text)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(version.Parts, Is.EqualTo(parts));
            Assert.That(version.Text, Is.EqualTo(text));
        }
    }

    private static ProfileTarget FromFileVersion(string? text, Version numbers, string moduleName = ShippingName, byte pointerSize = 8)
    {
        return UnrealTarget.FromFileVersion(FileVersion(text, numbers), moduleName, pointerSize, null, null).Unwrap();
    }

    private static ModuleFileVersion FileVersion(string? text, Version numbers)
    {
        return new ModuleFileVersion(text, numbers);
    }
}
