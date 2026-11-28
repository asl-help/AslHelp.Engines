using AslHelp.Engines.Profiles;
using AslHelp.Memory;
using NUnit.Framework;
using System;

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

    // UDK stamps 1.0, the engine build and 0, so the build 7258 is the 2010-09 release.
    [Test]
    public void FromFileVersion_Unreal3Numbers_ReadsTheRelease()
    {
        var target = FromFileVersion("1, 0, 7258, 0", new Version(1, 0, 7258, 0), "UDK.exe", 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Engine, Is.EqualTo(UnrealTarget.Unreal3));
            Assert.That(target.Version.Text, Is.EqualTo("2010-09"));
            Assert.That(target.Key, Is.EqualTo("win-x86-shipping"));
        }
    }

    // A build without a known release fails with the build and the form of the version to pass.
    [Test]
    public void FromFileVersion_UnknownUnreal3Build_FailsWithTheBuild()
    {
        Assert.That(
            UnrealTarget.FromFileVersion(FileVersion(null, new Version(1, 0, 8225, 0)), "UDK.exe", 4, null, null).UnwrapErr().Message,
            Is.EqualTo("The stamp 1.0.8225.0 isn't a known UDK build. Pass the version, like 2010-09 or 4.27.2."));
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

    // A passed version that isn't an Unreal Engine version fails with the text.
    [TestCase("beta")]
    [TestCase("3.5")]
    public void FromFileVersion_VersionOverrideThatIsNotAnEngineVersion_FailsWithTheText(string version)
    {
        Assert.That(
            UnrealTarget.FromFileVersion(null, ShippingName, 8, version, null).UnwrapErr().Message,
            Is.EqualTo($"'{version}' isn't an Unreal Engine version, like 4.27.2 or 2010-09."));
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
    [TestCase("2010-09", new[] { 2010, 9 })]
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
