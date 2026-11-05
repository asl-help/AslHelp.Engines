using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkProjectTests
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

    // The classes go to Development/Src/Fixture/Classes of the install, byte for byte.
    [Test]
    public void AddFixturePackage_FakeInstall_CopiesTheClasses()
    {
        string install = WriteInstall(_folder, DefaultIni);

        UdkProject.AddFixturePackage(install, WriteClasses());

        string copied = Path.Combine(install, "Development", "Src", "Fixture", "Classes", "FixtureGame.uc");
        Assert.That(File.ReadAllBytes(copied), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    // A class left from an earlier copy is removed, because the compiler would still build it.
    [Test]
    public void AddFixturePackage_StaleClassInTarget_RemovesIt()
    {
        string install = WriteInstall(_folder, DefaultIni);
        string target = Path.Combine(install, "Development", "Src", "Fixture", "Classes");
        WriteBytes(target, "Old.uc", [4]);

        UdkProject.AddFixturePackage(install, WriteClasses());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(target, "Old.uc")), Is.False);
            Assert.That(File.Exists(Path.Combine(target, "FixtureGame.uc")), Is.True);
        }
    }

    // Only the ;ModEditPackages=MyMod line changes, so every other line of the shipped ini stays as it was.
    [Test]
    public void AddFixturePackage_DisabledPackageLine_EnablesItAndKeepsTheOtherLines()
    {
        string install = WriteInstall(_folder, DefaultIni);

        UdkProject.AddFixturePackage(install, WriteClasses());

        string expected = DefaultIni.Replace(DisabledLine, EnabledLine, StringComparison.Ordinal);
        Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(expected)));
    }

    // A 2nd call finds ModEditPackages=Fixture and leaves the file alone.
    [Test]
    public void AddFixturePackage_PackageAlreadyEnabled_DoesNotWriteTheIni()
    {
        string enabled = DefaultIni.Replace(DisabledLine, EnabledLine, StringComparison.Ordinal);
        string install = WriteInstall(_folder, enabled);
        DateTime written = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(IniPath(install), written);

        UdkProject.AddFixturePackage(install, WriteClasses());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(enabled)));
            Assert.That(File.GetLastWriteTimeUtc(IniPath(install)), Is.EqualTo(written));
        }
    }

    // A longer DefaultEngineUDK.ini.partial from a crashed run is written over and moved onto the ini, so only the full ini is left.
    [Test]
    public void AddFixturePackage_LeftoverPartialIni_LeavesOnlyTheFullIni()
    {
        string install = WriteInstall(_folder, DefaultIni);
        WriteBytes(install, "UDKGame/Config/DefaultEngineUDK.ini.partial", Noise(4096));

        UdkProject.AddFixturePackage(install, WriteClasses());

        string expected = DefaultIni.Replace(DisabledLine, EnabledLine, StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(expected)));
            Assert.That(File.Exists(IniPath(install) + ".partial"), Is.False);
        }
    }

    // An ini without either line throws with the file path in the message, so the user can add the line by hand.
    [Test]
    public void AddFixturePackage_NoModEditPackagesLine_ThrowsInvalidDataException()
    {
        string install = WriteInstall(_folder, DefaultIni.Replace($"{DisabledLine}\r\n", "", StringComparison.Ordinal));

        Assert.That(
            () => UdkProject.AddFixturePackage(install, WriteClasses()),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(IniPath(install)));
    }

    // An install without UDKGame/Config/DefaultEngineUDK.ini is refused before any copy, with the UDKGame layout in the message.
    [Test]
    public void AddFixturePackage_MissingIni_ThrowsFileNotFoundExceptionAndWritesNothing()
    {
        string install = _folder.NewFolder();
        _ = Directory.CreateDirectory(Path.Combine(install, "Binaries"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => UdkProject.AddFixturePackage(install, WriteClasses()),
                Throws.TypeOf<FileNotFoundException>()
                    .With.Property(nameof(FileNotFoundException.FileName)).EqualTo(IniPath(install))
                    .And.Message.Contains("UDKGame"));
            Assert.That(Directory.Exists(Path.Combine(install, "Development", "Src", "Fixture")), Is.False);
        }
    }

    // A missing classes folder throws with its path before the install is touched.
    [Test]
    public void AddFixturePackage_MissingClassesDirectory_ThrowsDirectoryNotFoundException()
    {
        string install = WriteInstall(_folder, DefaultIni);
        string classes = Path.Combine(_folder.Root, "missing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => UdkProject.AddFixturePackage(install, classes),
                Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(classes));
            Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(DefaultIni)));
        }
    }

    // UDKEngine.ini is deleted, because the engine writes it again from the defaults and would keep the old package list otherwise.
    [Test]
    public void AddFixturePackage_EngineIniPresent_DeletesIt()
    {
        string install = WriteInstall(_folder, DefaultIni);
        WriteBytes(install, "UDKGame/Config/UDKEngine.ini", [5]);

        UdkProject.AddFixturePackage(install, WriteClasses());

        Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Config", "UDKEngine.ini")), Is.False);
    }

    // An install with the package already on still gets UDKEngine.ini deleted, so the engine reads the defaults again.
    [Test]
    public void AddFixturePackage_PackageAlreadyEnabled_DeletesTheEngineIni()
    {
        string install = WriteInstall(_folder, $"A=1\r\n{EnabledLine}\r\n");
        WriteBytes(install, "UDKGame/Config/UDKEngine.ini", [5]);

        UdkProject.AddFixturePackage(install, WriteClasses());

        Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Config", "UDKEngine.ini")), Is.False);
    }

    // A refused ini leaves UDKEngine.ini and earlier classes in place, because the call writes nothing before its checks.
    [Test]
    public void AddFixturePackage_NoPackageLine_KeepsTheEngineIniAndEarlierClasses()
    {
        string install = WriteInstall(_folder, "A=1\r\n");
        WriteBytes(install, "UDKGame/Config/UDKEngine.ini", [5]);
        WriteBytes(install, "Development/Src/Fixture/Classes/Old.uc", [4]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => UdkProject.AddFixturePackage(install, WriteClasses()), Throws.TypeOf<InvalidDataException>());
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Config", "UDKEngine.ini")), Is.True);
            Assert.That(File.Exists(Path.Combine(install, "Development", "Src", "Fixture", "Classes", "Old.uc")), Is.True);
        }
    }

    // The log of an earlier run is deleted before the start, so a compiler that fails before it writes a log gets the start
    // hint and not the old log. ping.exe rejects the make arguments with code 1 and writes no log.
    [Test]
    [Platform("Win")]
    public void MakeAsync_LogOfEarlierRun_DeletesItAndThrowsWithoutIt()
    {
        string install = WriteFakeInstallWithCompiler("PING.EXE");
        WriteBytes(install, "UDKGame/Logs/Launch.log", [1]);

        Assert.That(
            async () => await UdkProject.MakeAsync(new LocalPlayerHost(), install, UdkProject.DefaultMakeTimeout, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("code 1 (0x00000001)").And.Message.Not.Contains("Launch.log"));
        Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Logs", "Launch.log")), Is.False);
    }

    // A compiler that runs past the timeout is ended and throws TimeoutException with the start hint while there is no log.
    // winver.exe ignores the arguments and runs until it is stopped.
    [Test]
    [Platform("Win")]
    public void MakeAsync_CompilerNeverExits_ThrowsTimeoutException()
    {
        string install = WriteFakeInstallWithNeverEndingCompiler();

        Assert.That(
            async () => await UdkProject.MakeAsync(new LocalPlayerHost(), install, TimeSpan.FromSeconds(1), CancellationToken.None),
            Throws.TypeOf<TimeoutException>().With.Message.Contains("did not start").And.Message.Not.Contains("Launch.log"));
    }

    // A timeout after the compiler wrote its log points at the log. MakeAsync starts the compiler before it returns its
    // task, so the log written after the call is the log of this run.
    [Test]
    [Platform("Win")]
    public async Task MakeAsync_CompilerNeverExitsAfterItsLog_ThrowsTimeoutExceptionWithLogPath()
    {
        string install = WriteFakeInstallWithNeverEndingCompiler();

        var make = UdkProject.MakeAsync(new LocalPlayerHost(), install, TimeSpan.FromSeconds(1), CancellationToken.None);
        WriteBytes(install, "UDKGame/Logs/Launch.log", [1]);

        await Assert.ThatAsync(
            () => make,
            Throws.TypeOf<TimeoutException>().With.Message.Contains(Path.Combine(install, "UDKGame", "Logs", "Launch.log")));
    }

    // A non-zero exit without a log leaves Launch.log out of the message, because the compiler did not get as far as
    // writing it.
    [Test]
    [Platform("Win")]
    public void MakeAsync_CompilerExitsNonZeroWithoutLog_ThrowsWithoutTheLogPath()
    {
        string install = WriteFakeInstallWithCompiler("PING.EXE");

        Assert.That(
            async () => await UdkProject.MakeAsync(new LocalPlayerHost(), install, UdkProject.DefaultMakeTimeout, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("did not start").And.Message.Not.Contains("Launch.log"));
    }

    // An exit code of 0 without Fixture.u throws with the package path. tree.com takes the make arguments and exits with 0.
    [Test]
    [Platform("Win")]
    public void MakeAsync_CompilerExitsZeroWithoutPackage_ThrowsWithThePackagePath()
    {
        string install = WriteFakeInstallWithCompiler("tree.com");

        Assert.That(
            async () => await UdkProject.MakeAsync(new LocalPlayerHost(), install, UdkProject.DefaultMakeTimeout, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains(Path.Combine(install, "UDKGame", "Script", "Fixture.u")));
    }

    // A Fixture.u from an earlier build does not count, because make skips a package that looks up to date and a removed class would stay in it.
    [Test]
    [Platform("Win")]
    public void MakeAsync_StalePackageOnly_ThrowsWithThePackagePath()
    {
        string install = WriteFakeInstallWithCompiler("tree.com");
        WriteBytes(install, "UDKGame/Script/Fixture.u", [9]);

        Assert.That(
            async () => await UdkProject.MakeAsync(new LocalPlayerHost(), install, UdkProject.DefaultMakeTimeout, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains(Path.Combine(install, "UDKGame", "Script", "Fixture.u")));
    }

    // A canceled token throws before the deletes, so the package and the log of the last run stay and no compiler starts.
    [Test]
    [Platform("Win")]
    public void MakeAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        string install = WriteFakeInstallWithCompiler("tree.com");
        WriteBytes(install, "UDKGame/Script/Fixture.u", [9]);
        WriteBytes(install, "UDKGame/Logs/Launch.log", [1]);

        Assert.That(
            async () => await UdkProject.MakeAsync(
                new LocalPlayerHost(),
                install,
                UdkProject.DefaultMakeTimeout,
                new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Script", "Fixture.u")), Is.True);
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Logs", "Launch.log")), Is.True);
        }
    }

    private string WriteClasses()
    {
        string classes = _folder.NewFolder();
        WriteBytes(classes, "FixtureGame.uc", [1, 2, 3]);

        return classes;
    }

    private string WriteFakeInstallWithNeverEndingCompiler()
    {
        string install = WriteFakeInstallWithCompiler("winver.exe");

        // Copy the language files of winver.exe too, because a copy without them exits at once.
        foreach (string language in Directory.EnumerateDirectories(Environment.SystemDirectory))
        {
            string mui = Path.Combine(language, "winver.exe.mui");

            if (File.Exists(mui))
            {
                WriteBytes(install, $"Binaries/Win32/{Path.GetFileName(language)}/UDK.com.mui", File.ReadAllBytes(mui));
            }
        }

        return install;
    }

    private string WriteFakeInstallWithCompiler(string systemExecutable)
    {
        string install = WriteInstall(_folder, DefaultIni);
        File.Copy(Path.Combine(Environment.SystemDirectory, systemExecutable), Path.Combine(install, "Binaries", "Win32", "UDK.com"));

        return install;
    }
}
