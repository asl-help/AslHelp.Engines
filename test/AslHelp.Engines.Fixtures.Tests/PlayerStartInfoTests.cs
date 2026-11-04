using NUnit.Framework;
using System;

namespace AslHelp.Engines.Fixtures.Tests;

public sealed class PlayerStartInfoTests
{
    // An empty argument string means a player started without arguments.
    [Test]
    public void Constructor_EmptyArguments_Allowed()
    {
        Assert.That(new PlayerStartInfo("player.exe", "", "folder").Arguments, Is.Empty);
    }

    // A player starts on the visible desktop and waits up to 10 minutes for the machine lock unless set otherwise.
    [Test]
    public void Constructor_Defaults_AreVisibleDesktopAndTenMinutes()
    {
        PlayerStartInfo info = new("player.exe", "-a", "folder");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.HiddenDesktop, Is.False);
            Assert.That(info.LockTimeout, Is.EqualTo(TimeSpan.FromMinutes(10)));
        }
    }

    // A negative timeout throws when it is set, so the lock wait never gets it.
    [Test]
    public void LockTimeout_Negative_ThrowsArgumentOutOfRangeException()
    {
        PlayerStartInfo info = new("player.exe", "", "folder");

        Assert.That(() => info with { LockTimeout = TimeSpan.FromSeconds(-1) }, Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // 0 is the lowest allowed timeout.
    [Test]
    public void LockTimeout_Zero_Allowed()
    {
        PlayerStartInfo info = new("player.exe", "", "folder");

        Assert.That((info with { LockTimeout = TimeSpan.Zero }).LockTimeout, Is.EqualTo(TimeSpan.Zero));
    }

    // A timeout above int.MaxValue ms throws when it is set, because the lock wait would throw on its own thread.
    [Test]
    public void LockTimeout_AboveWaitLimit_ThrowsArgumentOutOfRangeException()
    {
        PlayerStartInfo info = new("player.exe", "", "folder");

        Assert.That(() => info with { LockTimeout = TimeSpan.MaxValue }, Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
