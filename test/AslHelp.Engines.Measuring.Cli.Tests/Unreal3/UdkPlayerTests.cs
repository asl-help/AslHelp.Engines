using AslHelp.Engines.Measuring.Cli.Unreal3;
using NUnit.Framework;
using System.IO;
using System.Threading;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkPlayerTests
{
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

    // A log the server still has open stays quiet, because a throw here would replace the failure that ended the run.
    [Test]
    public void DeleteLog_LogStillOpen_DoesNotThrow()
    {
        string log = Path.Combine(_folder.Root, "Launch.log");
        using FileStream server = new(log, FileMode.Create, FileAccess.Write, FileShare.None);

        Assert.That(() => UdkPlayer.DeleteLog(log), Throws.Nothing);
    }

    // A log from an earlier run goes before the start, because its ready line would count as the ready line of this run.
    [Test]
    public void StartAsync_LogOfAnEarlierRun_DeletesItBeforeTheStart()
    {
        string log = Path.Combine(_folder.Root, @"UDKGame\Logs\Launch.log");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        File.WriteAllText(log, "ScriptLog: Fixture ready");

        string maps = Directory.CreateDirectory(Path.Combine(_folder.Root, @"UDKGame\Content\Maps")).FullName;
        File.WriteAllText(Path.Combine(maps, "A.udk"), "");
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, @"Binaries\Win32"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await UdkPlayer.StartAsync(_folder.Root, CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>());
            Assert.That(File.Exists(log), Is.False);
        }
    }
}
