using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Fixtures.Tests.Unreal4;

public sealed class UnrealPlayerTests
{
    private const string Player = "player";

    // Each tag gives the platform and the configuration the build tools take, so a step never builds the wrong player.
    [TestCase("win-x64-shipping", "Win64", "Shipping")]
    [TestCase("win-x64-development", "Win64", "Development")]
    [TestCase("win-x86-shipping", "Win32", "Shipping")]
    [TestCase("win-x86-development", "Win32", "Development")]
    public void Parse_EveryTag_GivesPlatformAndConfiguration(string tag, string platform, string configuration)
    {
        var player = UnrealPlayer.Parse(tag);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(player.Tag, Is.EqualTo(tag));
            Assert.That(player.Platform, Is.EqualTo(platform));
            Assert.That(player.Configuration, Is.EqualTo(configuration));
        }
    }

    // A tag with a part too many or too few, another arch, another config or other casing throws with the tag.
    [TestCase("win-x64")]
    [TestCase("win-x64-shipping-x")]
    [TestCase("win-arm64-shipping")]
    [TestCase("win-x64-debug")]
    [TestCase("linux-x64-shipping")]
    [TestCase("win-X64-Shipping")]
    [TestCase("")]
    public void Parse_BadTag_ThrowsFormatExceptionWithTheTag(string tag)
    {
        Assert.That(() => UnrealPlayer.Parse(tag), Throws.TypeOf<FormatException>().With.Message.EqualTo(SR.FormatPlayerTagInvalid(tag)));
    }

    // The version and the tag come out of the id and put back together give the id, because the report and the group key come from them.
    [TestCase("4.19.2-win-x64-shipping", Architecture.X64, UnrealConfig.Shipping)]
    [TestCase("4.9.2-win-x86-development", Architecture.X86, UnrealConfig.Development)]
    [TestCase("5.8-win-x64-development", Architecture.X64, UnrealConfig.Development)]
    public void TryParseId_Id_ReturnsVersionAndPlayer(string id, Architecture arch, UnrealConfig config)
    {
        bool parsed = UnrealPlayer.TryParseId(id, out var version, out var player);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(player, Is.EqualTo(new UnrealPlayer(arch, config)));
            Assert.That(player!.IdOf(version!.ToString()), Is.EqualTo(id));
        }
    }

    // Anything else isn't a player id: a missing or bad part, a version before 4.0, another case or a number past the range of a version part.
    [TestCase("4.19.2")]
    [TestCase("4.19.2-win-x64")]
    [TestCase("4.19.2-linux-x64-shipping")]
    [TestCase("4.19.2-win-arm64-shipping")]
    [TestCase("4.19.2-win-x64-test")]
    [TestCase("x-win-x64-shipping")]
    [TestCase("4-win-x64-shipping")]
    [TestCase("4.19.2-win-x64-shipping-extra")]
    [TestCase("3.27.2-win-x64-shipping")]
    [TestCase("0.0-win-x64-shipping")]
    [TestCase("4.19.2-WIN-x64-shipping")]
    [TestCase("4.19.2-win-X64-shipping")]
    [TestCase("4.19.2-win-x64-Shipping")]
    [TestCase("-win-x64-shipping")]
    [TestCase("--win-x64-shipping")]
    [TestCase("4.19.2-win--shipping")]
    [TestCase("4.19.2-win-x64-")]
    [TestCase("99999999999.1-win-x64-shipping")]
    [TestCase("4.-1-win-x64-shipping")]
    [TestCase("")]
    [TestCase(null)]
    public void TryParseId_OtherShapes_ReturnsFalse(string? id)
    {
        bool parsed = UnrealPlayer.TryParseId(id, out var version, out var player);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.False);
            Assert.That(version, Is.Null);
            Assert.That(player, Is.Null);
        }
    }

    // A shipping build has the platform and the configuration in its file name, a development build doesn't.
    [TestCase("win-x64-shipping", "Win64", "Fixture-Win64-Shipping")]
    [TestCase("win-x86-shipping", "Win32", "Fixture-Win32-Shipping")]
    [TestCase("win-x64-development", "Win64", "Fixture")]
    [TestCase("win-x86-development", "Win32", "Fixture")]
    public void ExecutablePath_ShippingAndDevelopment_PicksTheName(string tag, string platform, string name)
    {
        var player = UnrealPlayer.Parse(tag);
        Version version = new(4, 19, 2);
        string folder = Path.Combine(Player, "Fixture", "Binaries", platform);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(player.ExecutablePath(Player, version), Is.EqualTo(Path.Combine(folder, $"{name}.exe")));
            Assert.That(player.PdbPath(Player, version), Is.EqualTo(Path.Combine(folder, $"{name}.pdb")));
        }
    }

    // A shipping build before 4.3 stages the executable and the PDB as Fixture.exe and Fixture.pdb.
    [TestCase("4.2.1", "Fixture")]
    [TestCase("4.3.1", "Fixture-Win32-Shipping")]
    [TestCase("5.8", "Fixture-Win32-Shipping")]
    public void ExecutablePath_ShippingAroundTheRename_PicksTheName(string version, string name)
    {
        var player = UnrealPlayer.Parse("win-x86-shipping");

        Assert.That(
            player.ExecutablePath(Player, Version.Parse(version)),
            Is.EqualTo(Path.Combine(Player, "Fixture", "Binaries", "Win32", $"{name}.exe")));
    }

    // The PDB only swaps the extension of the file, so a dot or an .exe in the folder stays.
    [Test]
    public void PdbPath_FolderWithDots_ChangesOnlyTheFile()
    {
        const string Folder = "player.exe.old";
        var player = UnrealPlayer.Parse("win-x64-shipping");

        Assert.That(
            player.PdbPath(Folder, new Version(4, 19, 2)),
            Is.EqualTo(Path.Combine(Folder, "Fixture", "Binaries", "Win64", "Fixture-Win64-Shipping.pdb")));
    }
}
