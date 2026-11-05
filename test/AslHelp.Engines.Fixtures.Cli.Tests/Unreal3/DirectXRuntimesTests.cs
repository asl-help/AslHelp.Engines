using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class DirectXRuntimesTests
{
    // D3DCOMPILER_47.dll and XInput1_3.dll match by prefix without case, so the version suffix never matters.
    [TestCase("D3DCOMPILER_47.dll")]
    [TestCase("XInput1_3.dll")]
    public void IsRuntime_DirectXPrefixInOtherCase_ReturnsTrue(string fileName)
    {
        Assert.That(DirectXRuntimes.IsRuntime(fileName), Is.True);
    }

    // KERNEL32.dll and d3d9.dll are system DLLs that no redist ships, so they are no runtime.
    [TestCase("KERNEL32.dll")]
    [TestCase("d3d9.dll")]
    public void IsRuntime_SystemDll_ReturnsFalse(string fileName)
    {
        Assert.That(DirectXRuntimes.IsRuntime(fileName), Is.False);
    }

    // msvcr100.dll is no DirectX runtime, so the 2 families stay apart and the runtime step checks both.
    [Test]
    public void IsRuntime_VisualCppDll_ReturnsFalse()
    {
        Assert.That(DirectXRuntimes.IsRuntime("msvcr100.dll"), Is.False);
    }
}
