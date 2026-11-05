using NUnit.Framework;
using System;
using System.Threading;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class TransferTimerTests
{
    // Dispose removes the link to the caller's token, so a cancel after the transfer, such as Ctrl+C, never reaches the disposed timer.
    [Test]
    public void Dispose_CallerCancelsAfterwards_DoesNotThrow()
    {
        using CancellationTokenSource caller = new();
        TransferTimer timer = new(TimeProvider.System, caller.Token);

        timer.Dispose();

        Assert.That(caller.Cancel, Throws.Nothing);
    }
}
