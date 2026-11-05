using NUnit.Framework;
using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkInstallTests
{
    // Set FIXTURES_UDK_INSTALLER to a downloaded UDK installer to check a real one too.
    private const string InstallerVariable = "FIXTURES_UDK_INSTALLER";

    // A real installer has UDK.com in the zip past its stub, so SelfExtractingZip.Open finds the tree that Extract writes without running UnSetup.
    [Test]
    public void Open_RealInstaller_HasUdkCom()
    {
        string? installer = Environment.GetEnvironmentVariable(InstallerVariable);
        Assume.That(installer is not null && File.Exists(installer), $"Set {InstallerVariable} to the installer to run this test.");

        using var archive = SelfExtractingZip.Open(installer!);

        Assert.That(archive.GetEntry("Binaries/Win32/UDK.com"), Is.Not.Null);
    }
}
