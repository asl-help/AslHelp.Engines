using AslHelp.Engines.Measuring.Cli.Unreal3;
using NUnit.Framework;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkErrorTests
{
    // A server that exited after a passed measurement fails with the exit code alone.
    [Test]
    public void PlayerExitedDuringMeasurement_NoCause_WritesTheExitCode()
    {
        Assert.That(UdkError.PlayerExitedDuringMeasurement(3, null).Message, Is.EqualTo(UdkSR.FormatPlayerExitedDuringMeasurement(3)));
    }

    // The exit comes first, because a dead server makes the search fail and the exit is the reason.
    [Test]
    public void PlayerExitedDuringMeasurement_Cause_WritesTheExitBeforeTheCause()
    {
        Assert.That(
            UdkError.PlayerExitedDuringMeasurement(3, new TextError("The search failed.")).Message,
            Is.EqualTo($"{UdkSR.FormatPlayerExitedDuringMeasurement(3)} The search failed."));
    }

    // A cause with braces goes through the format as a value, so its text stays as it was and nothing throws.
    [Test]
    public void ModuleNotLoaded_CauseWithPlaceholders_KeepsTheCause()
    {
        Assert.That(UdkError.ModuleNotLoaded("file.bin", new TextError("Can't open {0} or {x}.")).Message, Does.EndWith("Can't open {0} or {x}."));
    }

    // A cause with braces stays as it was in an error that appends it.
    [Test]
    public void ObjectArrayUnreadable_CauseWithPlaceholders_KeepsTheCause()
    {
        Assert.That(UdkError.ObjectArrayUnreadable("3200", new TextError("Can't read {0}.")).Message, Does.EndWith(" Can't read {0}."));
    }
}
