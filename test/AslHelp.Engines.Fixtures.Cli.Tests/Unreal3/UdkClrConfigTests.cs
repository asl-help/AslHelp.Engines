using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkClrConfigTests
{
    private const string ShippedConfig =
        "<configuration>\r\n  <runtime>\r\n    <generatePublisherEvidence enabled=\"false\"/>\r\n  </runtime>\r\n</configuration>\r\n";
    private const string Startup = "<supportedRuntime version=\"v4.0\"";

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

    // An install with the Visual C++ 2008 redist gets CLR 4 for UDK.exe, which keeps its other settings, and for UDK.com, which ships no config.
    [Test]
    public void Add_Vc2008Redist_SetsClr4ForBothExecutables()
    {
        string install = WriteInstall("vcredist_x86_vs2008sp1.exe");

        UdkClrConfig.Add(install);

        string executable = File.ReadAllText(Path.Combine(install, "Binaries", "Win32", "UDK.exe.config"));
        string compiler = File.ReadAllText(Path.Combine(install, "Binaries", "Win32", "UDK.com.config"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(executable, Does.Contain(Startup).And.Contain("generatePublisherEvidence"));
            Assert.That(compiler, Does.Contain(Startup));
        }
    }

    // A 2nd run changes nothing, so a build that starts again adds no 2nd startup section.
    [Test]
    public void Add_SecondRun_KeepsOneStartupSection()
    {
        string install = WriteInstall("vcredist_x86_vs2008sp1.exe");
        UdkClrConfig.Add(install);
        string path = Path.Combine(install, "Binaries", "Win32", "UDK.exe.config");
        string first = File.ReadAllText(path);

        UdkClrConfig.Add(install);

        Assert.That(File.ReadAllText(path), Is.EqualTo(first));
    }

    // An install with the Visual C++ 2010 redist runs on CLR 4 as shipped, so its files stay as they are.
    [Test]
    public void Add_Vc2010Redist_WritesNothing()
    {
        string install = WriteInstall("vcredist_x86_vs2010sp1.exe");

        UdkClrConfig.Add(install);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(install, "Binaries", "Win32", "UDK.exe.config")), Is.EqualTo(ShippedConfig));
            Assert.That(File.Exists(Path.Combine(install, "Binaries", "Win32", "UDK.com.config")), Is.False);
        }
    }

    private string WriteInstall(string redistEntry)
    {
        string install = _folder.NewFolder();
        TestFolder.WriteZip(Path.Combine(install, "Binaries", "Redist", "UE3Redist.exe"), (redistEntry, [1]));
        _ = Directory.CreateDirectory(Path.Combine(install, "Binaries", "Win32"));
        File.WriteAllText(Path.Combine(install, "Binaries", "Win32", "UDK.exe.config"), ShippedConfig);

        return install;
    }
}
