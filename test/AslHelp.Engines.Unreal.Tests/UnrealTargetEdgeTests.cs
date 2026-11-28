using AslHelp.Engines.Profiles;
using AslHelp.Memory;
using NUnit.Framework;
using System;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealTargetEdgeTests
{
    private const string ShippingName = "Player-Win64-Shipping.exe";

    // A release version with its own patch still takes the patch of matching numbers.
    [Test]
    public void FromFileVersion_ReleaseVersionWithAPatch_TakesThePatchOfTheNumbers()
    {
        Assert.That(FromFileVersion("++UE4+Release-4.27.2-CL-1", new Version(4, 27, 2, 0)).Version.Text, Is.EqualTo("4.27.2"));
    }

    // Release- without a version of major 4 or 5 after it falls back to the numbers.
    [TestCase("++UE4+Release-")]
    [TestCase("++UE4+Release-CL-1")]
    [TestCase("++UE3+Release-3.5-CL-1")]
    public void FromFileVersion_ReleaseMarkerWithoutAnEngineVersion_ReadsTheNumbers(string text)
    {
        Assert.That(FromFileVersion(text, new Version(4, 19, 2, 0)).Version.Text, Is.EqualTo("4.19.2"));
    }

    // Numbers outside major 4 or 5 that aren't 1.0.<build>.0 are the game's own version, so the file doesn't have an engine stamp.
    [TestCase(1, 0, 0, 0)]
    [TestCase(1, 0, 5, 1)]
    [TestCase(2, 1, 0, 0)]
    [TestCase(3, 9, 9, 9)]
    [TestCase(6, 0, 0, 0)]
    [TestCase(2023, 1, 0, 0)]
    public void FromFileVersion_GameNumbersOfAnotherMajor_FailsWithTheOverrideHint(int major, int minor, int build, int revision)
    {
        var fileVersion = new ModuleFileVersion("1.0", new Version(major, minor, build, revision));

        Assert.That(
            UnrealTarget.FromFileVersion(fileVersion, ShippingName, 8, null, null).UnwrapErr().Message,
            Is.EqualTo($"{ShippingName} doesn't have an engine version stamp. Pass the version, like 4.27.2."));
    }

    // An empty passed version isn't a version and fails with the text.
    [Test]
    public void FromFileVersion_EmptyVersionOverride_FailsWithTheText()
    {
        Assert.That(
            UnrealTarget.FromFileVersion(null, ShippingName, 8, "", null).UnwrapErr().Message,
            Is.EqualTo("'' isn't an Unreal Engine version, like 4.27.2, 2010-09 or 3.8225."));
    }

    // A full path as the module name still builds the key from the file name.
    [Test]
    public void FromFileVersion_ModuleNameWithFolders_BuildsTheKeyFromTheFileName()
    {
        string moduleName = @"C:\Games\Shipping-Win64\Player-Win64-DebugGame.exe";

        var target = UnrealTarget.FromFileVersion(new ModuleFileVersion(null, new Version(4, 19, 2, 0)), moduleName, 8, null, null).Unwrap();

        Assert.That(target.Key, Is.EqualTo("win-x64-development"));
    }

    // A configuration in the middle of the name isn't the configuration of the build.
    [TestCase("Debug-Win64-Shipping.exe")]
    [TestCase("Player-Debug.dll.exe")]
    public void FromFileVersion_DebugNotAtTheEnd_IsShipping(string moduleName)
    {
        var target = UnrealTarget.FromFileVersion(new ModuleFileVersion(null, new Version(4, 19, 2, 0)), moduleName, 8, null, null).Unwrap();

        Assert.That(target.Key, Is.EqualTo("win-x64-shipping"));
    }

    // A passed shipping configuration wins over a development name.
    [Test]
    public void FromFileVersion_ShippingOverrideOnDevelopmentName_IsShipping()
    {
        var target = UnrealTarget.FromFileVersion(
            new ModuleFileVersion(null, new Version(4, 19, 2, 0)),
            "Player-Win64-DebugGame.exe",
            8,
            null,
            UnrealConfiguration.Shipping).Unwrap();

        Assert.That(target.Key, Is.EqualTo("win-x64-shipping"));
    }

    // A bad pointer size fails with the size, also with a passed version.
    [Test]
    public void FromFileVersion_PointerSizeOtherThan4Or8WithVersionOverride_FailsWithTheSize()
    {
        Assert.That(
            UnrealTarget.FromFileVersion(null, ShippingName, 16, "4.27.2", null).UnwrapErr().Message,
            Is.EqualTo("A pointer size of 16 bytes isn't x86 or x64."));
    }

    // Detection of the 4.19.2 player's stamp picks the 4.18.3 group of the shipped documents, which lists 4.19.2.
    [Test]
    public void FromFileVersion_Version4192ThroughShippedCatalog_PicksAListedDocument()
    {
        var target = FromFileVersion(null, new Version(4, 19, 2, 0));

        var selection = UnrealProfile.CreateCatalog().Select(target, ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
            Assert.That(selection.Entry.Source, Is.EqualTo("Profiles/unreal4-4.18.3-win-x64-shipping.json"));
        }
    }

    // Text without an ASCII digit (Arabic-Indic and fullwidth digits aren't ASCII) or with a number past int.MaxValue doesn't read.
    [TestCase("")]
    [TestCase("BETA")]
    [TestCase("4.2147483648")]
    [TestCase("٤.٢٧")]
    [TestCase("４.２")]
    public void ParseVersion_TextWithoutAUsableNumber_ReturnsNull(string text)
    {
        Assert.That(UnrealTarget.ParseVersion(text), Is.Null);
    }

    // int.MaxValue is the largest number that reads.
    [Test]
    public void ParseVersion_NumberAtIntMax_Reads()
    {
        Assert.That(UnrealTarget.ParseVersion("2147483647")!.Parts, Is.EqualTo([int.MaxValue]));
    }

    // Leading zeros, other separators and text around the numbers don't change the version, so UDK-2010-09-BETA equals 2010.9.
    [TestCase("2010-09")]
    [TestCase("UDK-2010-09-BETA")]
    [TestCase("2010.009")]
    public void ParseVersion_LeadingZerosAndOtherSeparators_AreEqual(string text)
    {
        var version = UnrealTarget.ParseVersion(text)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(version, Is.EqualTo(UnrealTarget.ParseVersion("2010.9")));
            Assert.That(version.Text, Is.EqualTo(text));
        }
    }

    // A measured UDK release reads as 3 and the build it stamps, then the parts after the month.
    [TestCase("2010-09", new[] { 3, 7258 })]
    [TestCase("2013-02-BETA", new[] { 3, 10897 })]
    [TestCase("2013-02-BETA2", new[] { 3, 10900, 2 })]
    [TestCase("2011-03-BETA2", new[] { 3, 7977, 2 })]
    public void ParseVersion_UdkRelease_ReadsAs3AndItsBuild(string release, int[] parts)
    {
        Assert.That(UnrealTarget.ParseVersion(release)!.Parts, Is.EqualTo(parts));
    }

    private static ProfileTarget FromFileVersion(string? text, Version numbers)
    {
        return UnrealTarget.FromFileVersion(new ModuleFileVersion(text, numbers), ShippingName, 8, null, null).Unwrap();
    }
}
