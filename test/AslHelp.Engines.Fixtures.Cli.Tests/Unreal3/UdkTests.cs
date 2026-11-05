using AslHelp.Engines.Fixtures.Cli.Unreal3;
using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeHttpHandler;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;
using static AslHelp.Engines.Fixtures.Cli.Tests.Unreal3.FakeUdkInstall;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal3;

public sealed class UdkTests
{
    // Set FIXTURES_UDK_INSTALL to an extracted UDK install to compile the fixture package in it. The test changes that
    // install.
    private const string InstallVariable = "FIXTURES_UDK_INSTALL";

    private const string PhysXEntry = "PhysX/PhysX_8.10.29_SystemSoftware.exe";
    private const string Vc2010Entry = "vcredist_x86_vs2010sp1.exe";

    private Downloader? _downloader;
    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _downloader?.Dispose();
        _downloader = null;
        _folder.Delete();
    }

    // An existing install sends no request, so a cached install works offline.
    [Test]
    public async Task InstallAsync_ExistingInstall_SendsNoRequest()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "udk-2015-02"));
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);

        string install = await new Udk(downloader, new LocalPlayerHost()).InstallAsync("2015-02", _folder.Root, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(install, Is.EqualTo(Path.Combine(_folder.Root, "udk-2015-02")));
            Assert.That(handler.Requests, Is.Empty);
        }
    }

    // A missing install sends only the pinned download and reads no listing.
    [Test]
    public void InstallAsync_NoInstall_RequestsOnlyThePinnedInstaller()
    {
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);

        Assert.That(
            async () => await new Udk(downloader, new LocalPlayerHost()).InstallAsync("2015-02", _folder.Root, null, CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo("https://archive.org/download/udkinstaller/UDKInstall-2015-02.exe"));
            Assert.That(handler.Requests.ConvertAll(request => request.Uri.AbsolutePath), Has.None.StartsWith("/metadata/"));
            Assert.That(Directory.Exists(Path.Combine(_folder.Root, "udk-2015-02")), Is.False);
        }
    }

    // An unpinned version throws before the download and the folder, so a typo costs no network and leaves no folder behind.
    [Test]
    public void InstallAsync_UnpinnedVersion_ThrowsBeforeAnyRequest()
    {
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await new Udk(downloader, new LocalPlayerHost()).InstallAsync("1999-01", _folder.Root, null, CancellationToken.None),
                Throws.InvalidOperationException);
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(_folder.Root, "udk-1999-01")), Is.False);
        }
    }

    // A casing variant returns the folder with the pinned spelling, so 2 spellings share 1 install.
    [Test]
    public async Task InstallAsync_CasingVariant_ReturnsTheFolderWithThePinnedSpelling()
    {
        _ = Directory.CreateDirectory(Path.Combine(_folder.Root, "udk-2011-03-BETA2"));
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);

        string install = await new Udk(downloader, new LocalPlayerHost()).InstallAsync("2011-03-beta2", _folder.Root, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(install, Is.EqualTo(Path.Combine(_folder.Root, "udk-2011-03-BETA2")));
            Assert.That(handler.Requests, Is.Empty);
        }
    }

    // An install whose redist has the PhysX installer is refused with the version, before any request.
    [Test]
    public void InstallAsync_ExistingInstallWithPhysXInstaller_ThrowsBeforeAnyRequest()
    {
        WriteZip(Path.Combine(_folder.Root, "udk-2009-11-BETA", "Binaries", "Windows", "UE3Redist.exe"), (PhysXEntry, [1]));
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await new Udk(downloader, new LocalPlayerHost()).InstallAsync("2009-11-BETA", _folder.Root, null, CancellationToken.None),
                Throws.InvalidOperationException
                    .With.Message.Contains("UDK 2009-11-BETA")
                    .And.Message.Contains("PhysX"));
            Assert.That(handler.Requests, Is.Empty);
        }
    }

    // An install whose redist has only the Visual C++ 2010 installer passes the check.
    [Test]
    public async Task InstallAsync_ExistingInstallWithVc2010Redist_ReturnsIt()
    {
        WriteZip(Path.Combine(_folder.Root, "udk-2013-07", "Binaries", "Redist", "UE3Redist.exe"), (Vc2010Entry, [1]));
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);

        string install = await new Udk(downloader, new LocalPlayerHost()).InstallAsync("2013-07", _folder.Root, null, CancellationToken.None);

        Assert.That(install, Is.EqualTo(Path.Combine(_folder.Root, "udk-2013-07")));
    }

    // A casing variant adds the pinned spelling, so 2011-03-beta2 and 2011-03-BETA2 share 1 player.
    [Test]
    public void AddKeyInputs_CasingVariant_GivesSameKey()
    {
        Assert.That(KeyOf("2011-03-beta2"), Is.EqualTo(KeyOf("2011-03-BETA2")));
    }

    // The player folder and the store path use the version AddKeyInputs returns, so a casing variant gets the pinned spelling.
    [Test]
    public void AddKeyInputs_CasingVariant_ReturnsThePinnedSpelling()
    {
        CacheKeyBuilder builder = new("player");

        string version = NewOfflineUdk().AddKeyInputs(builder, "2011-03-beta2");
        _ = builder.Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(version, Is.EqualTo("2011-03-BETA2"));
            Assert.That(builder.Inputs, Does.Contain(new CacheKeyInput("version", "2011-03-BETA2")));
        }
    }

    // Another version adds another pinned spelling and sha1, so 2 versions never share a player.
    [Test]
    public void AddKeyInputs_OtherVersion_GivesOtherKey()
    {
        Assert.That(KeyOf("2015-02"), Is.Not.EqualTo(KeyOf("2014-02")));
    }

    // An unpinned version is refused with the install's message before any add, so the key fails before the store opens and
    // the builder stays as it was.
    [Test]
    public void AddKeyInputs_UnpinnedVersion_ThrowsAndAddsNothing()
    {
        CacheKeyBuilder builder = new("player");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => NewOfflineUdk().AddKeyInputs(builder, "1999-01"),
                Throws.InvalidOperationException
                    .With.Message.Contains("1999-01")
                    .And.Message.Contains("installers.json")
                    .And.Message.Not.Contains("(Parameter"));
            Assert.That(builder.Build(), Is.EqualTo(new CacheKeyBuilder("player").Build()));
        }
    }

    // The key has the version, installer-sha1, make-command, server-command, config-edit, config-delete and build-version 2
    // and nothing else, so a change to any of them shows here first.
    [Test]
    public void AddKeyInputs_PinnedVersion_AddsEveryBuildInput()
    {
        var expected = new CacheKeyBuilder("player")
            .Add("version", "2015-02")
            .Add("installer-sha1", "71f79806a4f6dab673ba85b540ea12629b8bc41e")
            .Add("make-command", @"Binaries\Win32\UDK.com make -unattended -nullrhi")
            .Add("server-command", @"Binaries\Win32\UDK.exe server {0}?game=Fixture.FixtureGame -unattended -nullrhi -FORCELOGFLUSH")
            .Add("config-edit", @"UDKGame\Config\DefaultEngineUDK.ini: ;ModEditPackages=MyMod -> ModEditPackages=Fixture")
            .Add("config-delete", @"UDKGame\Config\UDKEngine.ini")
            .Add("build-version", "2")
            .Build();

        Assert.That(KeyOf("2015-02"), Is.EqualTo(expected));
    }

    // An unpinned version fails at the key with the message that lists the pinned versions, before the store opens and before any request.
    [Test]
    public async Task RunAsync_UnpinnedVersion_FailsBeforeTheStoreAndTheInstall()
    {
        FakeHttpHandler handler = new(_ => Status(HttpStatusCode.NotFound));
        using Downloader downloader = new(handler);
        using StringWriter output = new();
        using StringWriter error = new();
        string folder = _folder.NewFolder();
        bool opened = false;

        int code = await CommandLine.RunAsync(
            ["build", "unreal3", "1999-01", "--output", folder],
            output,
            error,
            [new Udk(downloader, new LocalPlayerHost())],
            _ =>
            {
                opened = true;
                return null;
            },
            Path.Combine(AppContext.BaseDirectory, "projects"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("1999-01").And.Contains("2015-02"));
            Assert.That(opened, Is.False);
            Assert.That(handler.Requests, Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(folder, "udk-1999-01")), Is.False);
        }
    }

    // The CLI project copies projects/unreal3/Classes to its output and the copy reaches this test project, so a build finds the classes there.
    [Test]
    public void Classes_CopiedToOutput_HasEveryFixtureClass()
    {
        string classes = OutputClasses();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(classes, "FixtureGame.uc")), Is.True);
            Assert.That(File.Exists(Path.Combine(classes, "FixtureData.uc")), Is.True);
            Assert.That(File.Exists(Path.Combine(classes, "FixtureItem.uc")), Is.True);
        }
    }

    // A missing UDK.com throws with its path before any write, so the classes, the ini and an earlier Fixture.u stay as they were.
    [Test]
    public void BuildAsync_MissingCompiler_ThrowsFileNotFoundExceptionAndWritesNothing()
    {
        string install = WriteInstall(_folder, DefaultIni);
        WriteBytes(install, "UDKGame/Script/Fixture.u", [9]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await NewOfflineUdk().BuildAsync(install, WriteProject(), CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>().With.Message.Contains(Path.Combine(install, "Binaries", "Win32", "UDK.com")));
            Assert.That(Directory.Exists(Path.Combine(install, "Development")), Is.False);
            Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(DefaultIni)));
            Assert.That(File.Exists(Path.Combine(install, "UDKGame", "Script", "Fixture.u")), Is.True);
        }
    }

    // A missing UDK.exe throws with its path before the classes or the ini change, because the runtime step reads the server's imports first.
    [Test]
    public void BuildAsync_MissingServer_ThrowsFileNotFoundExceptionAndWritesNothing()
    {
        string install = WriteInstall(_folder, DefaultIni);
        WriteBytes(install, "Binaries/Win32/UDK.com", [1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await NewOfflineUdk().BuildAsync(install, WriteProject(), CancellationToken.None),
                Throws.TypeOf<FileNotFoundException>().With.Message.Contains(Path.Combine(Binaries(install), "UDK.exe")));
            Assert.That(Directory.Exists(Path.Combine(install, "Development")), Is.False);
            Assert.That(File.ReadAllBytes(IniPath(install)), Is.EqualTo(Encoding.ASCII.GetBytes(DefaultIni)));
        }
    }

    // An offline install runs every step up to make, so the classes are copied and the missing Fixture.u fails with its path.
    // tree.com takes the make arguments and exits with 0.
    [Test]
    [Platform("Win")]
    public void BuildAsync_CompilerExitsZeroWithoutPackage_ThrowsWithThePackagePath()
    {
        string install = WriteInstall(_folder, DefaultIni);
        File.Copy(Path.Combine(Environment.SystemDirectory, "tree.com"), Path.Combine(Binaries(install), "UDK.com"));

        // Give the install a UDK.exe with no imports, so the runtime step needs no redist.
        WriteServer(install);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await NewOfflineUdk().BuildAsync(install, WriteProject(), CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains(Path.Combine(install, "UDKGame", "Script", "Fixture.u")));
            Assert.That(File.Exists(Path.Combine(install, "Development", "Src", "Fixture", "Classes", "FixtureGame.uc")), Is.True);
        }
    }

    // A real install compiles the package into UDKGame/Script/Fixture.u.
    [Test]
    public async Task BuildAsync_RealInstall_WritesFixturePackage()
    {
        string? install = Environment.GetEnvironmentVariable(InstallVariable);
        Assume.That(install is not null && Directory.Exists(install), $"Set {InstallVariable} to an extracted install to run this test.");

        await NewOfflineUdk().BuildAsync(install!, OutputProject(), CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(install!, "UDKGame", "Script", "Fixture.u")), Is.True);
    }

    // A real install with the built package starts the server, which logs the ready line once the fixture is spawned.
    [Test]
    public async Task CheckAsync_RealInstall_FindsTheReadyLine()
    {
        string? install = Environment.GetEnvironmentVariable(InstallVariable);
        Assume.That(install is not null && Directory.Exists(install), $"Set {InstallVariable} to an extracted install to run this test.");

        var udk = NewOfflineUdk();
        await udk.BuildAsync(install!, OutputProject(), CancellationToken.None);

        await Assert.ThatAsync(() => udk.CheckAsync(install!, CancellationToken.None), Throws.Nothing);
    }

    private CacheKey KeyOf(string version)
    {
        CacheKeyBuilder builder = new("player");
        _ = NewOfflineUdk().AddKeyInputs(builder, version);

        return builder.Build();
    }

    private static string OutputClasses()
    {
        return Path.Combine(OutputProject(), "Classes");
    }

    private static string OutputProject()
    {
        return Path.Combine(AppContext.BaseDirectory, "projects", "unreal3");
    }

    private string WriteProject()
    {
        string project = _folder.NewFolder();
        WriteBytes(project, "Classes/FixtureGame.uc", [1, 2, 3]);

        return project;
    }

    // Return 404 for every request, so a build that reaches the network fails.
    private Udk NewOfflineUdk()
    {
        _downloader ??= new Downloader(new FakeHttpHandler(_ => Status(HttpStatusCode.NotFound)));

        return new Udk(_downloader, new LocalPlayerHost());
    }
}
