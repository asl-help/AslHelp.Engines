using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class VisualCppRuntimesTests
{
    private const string Vc2008Entry = "vcredist_x86_vs2008sp1.exe";

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

    // msvcr or msvcp with 2 or 3 digits and .dll is a Visual C++ runtime, in either case.
    [TestCase("msvcr100.dll")]
    [TestCase("MSVCP120.DLL")]
    public void IsRuntime_Msvcr100AndMsvcp120_ReturnsTrue(string fileName)
    {
        Assert.That(VisualCppRuntimes.IsRuntime(fileName), Is.True);
    }

    // msvcrt.dll and MSVCP60.dll are Windows CRTs, so they never count.
    [TestCase("msvcrt.dll")]
    [TestCase("MSVCP60.dll")]
    public void IsRuntime_SystemCrt_ReturnsFalse(string fileName)
    {
        Assert.That(VisualCppRuntimes.IsRuntime(fileName), Is.False);
    }

    // msvcr9.dll and msvcr100.lib fall outside the rule, so a near-miss name is no runtime.
    [TestCase("msvcr9.dll")]
    [TestCase("msvcr100.lib")]
    public void IsRuntime_OneDigitOrOtherExtension_ReturnsFalse(string fileName)
    {
        Assert.That(VisualCppRuntimes.IsRuntime(fileName), Is.False);
    }

    // A Binaries/Windows/UE3Redist.exe with the Visual C++ 2008 installer throws with the version and the versions that build.
    [Test]
    public void ThrowIfVc2008_RedistWithVc2008Entry_ThrowsWithTheVersion()
    {
        string install = _folder.NewFolder();
        WriteZip(Path.Combine(install, "Binaries", "Windows", "UE3Redist.exe"), (Vc2008Entry, [1]));

        Assert.That(
            () => VisualCppRuntimes.ThrowIfVc2008(install, "1.0"),
            Throws.InvalidOperationException
                .With.Message.Contains("UDK 1.0")
                .And.Message.Contains("Visual C++ 2008")
                .And.Message.Contains("2011-10"));
    }

    // An install without a redist passes, because the runtime step reports a missing redist only if it needs one.
    [Test]
    public void ThrowIfVc2008_NoRedist_Passes()
    {
        string install = _folder.NewFolder();

        Assert.That(() => VisualCppRuntimes.ThrowIfVc2008(install, "1.0"), Throws.Nothing);
    }

    // A vc_red.cab from makecab with F_CENTRAL_msvcr100_x86 and F_CENTRAL_msvcp100_x86 gives msvcr100.dll and msvcp100.dll byte for byte.
    [Test]
    [Platform("Win")]
    public async Task ExpandCabinetAsync_MakecabCabinet_RenamesBothDlls()
    {
        byte[] cabinet = FakeCabinet.Make(_folder, ("F_CENTRAL_msvcr100_x86", [1, 2, 3]), ("F_CENTRAL_msvcp100_x86", [4, 5]));
        string temp = _folder.NewFolder();
        WriteBytes(temp, "vc_red.cab", cabinet);

        await VisualCppRuntimes.ExpandCabinetAsync(temp, CancellationToken.None);

        string[] expected = [Path.Combine(temp, "msvcp100.dll"), Path.Combine(temp, "msvcr100.dll"), Path.Combine(temp, "vc_red.cab")];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.GetFiles(temp), Is.EquivalentTo(expected));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(temp, "msvcr100.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(temp, "msvcp100.dll")), Is.EqualTo(new byte[] { 4, 5 }));
        }
    }

    // A temp folder without vc_red.cab fails with the expand.exe command line, so the message shows the missing cabinet.
    [Test]
    [Platform("Win")]
    public void ExpandCabinetAsync_NoCabinet_ThrowsInvalidOperationException()
    {
        string temp = _folder.NewFolder();

        Assert.That(
            async () => await VisualCppRuntimes.ExpandCabinetAsync(temp, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains(Path.Combine(temp, "vc_red.cab")));
    }

    // The installer reads /x only with the quote after the colon, so a temp folder with a space gets /x:"<folder>" and no install starts.
    [Test]
    public void NewInstallerStartInfo_FolderWithSpace_QuotesOnlyThePath()
    {
        string temp = Path.Combine(_folder.Root, "a b", "Runtimes.partial");

        var info = VisualCppRuntimes.NewInstallerStartInfo(temp);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.FileName, Is.EqualTo(Path.Combine(temp, "vcredist_x86_vs2010sp1.exe")));
            Assert.That(info.Arguments, Is.EqualTo($"/q /x:\"{temp}\""));
            Assert.That(info.ArgumentList, Is.Empty);
        }
    }
}
