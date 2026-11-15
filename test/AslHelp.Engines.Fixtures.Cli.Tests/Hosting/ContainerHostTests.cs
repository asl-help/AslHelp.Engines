using AslHelp.Engines.Fixtures.Cli.Hosting;
using Docker.DotNet;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Hosting;

public sealed class ContainerHostTests
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

    // The container mounts the install, starts in the folder of the executable and gets each argument on its own.
    [Test]
    public void NewContainerParameters_InstallAndArguments_MountsTheInstallAndRunsTheExecutable()
    {
        string install = Path.Combine(_folder.NewFolder(), "a b");

        var parameters = ContainerHost.NewContainerParameters(install, @"Binaries\Win32\Game.exe", "server map?x=1 -flag");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters.Image, Is.EqualTo(ContainerHost.Image));
            Assert.That(parameters.HostConfig?.Binds?[0], Is.EqualTo($@"{install}:C:\player"));
            Assert.That(parameters.WorkingDir, Is.EqualTo(@"C:\player\Binaries\Win32"));
            Assert.That(string.Join('|', parameters.Cmd), Is.EqualTo(@"C:\player\Binaries\Win32\Game.exe|server|map?x=1|-flag"));
        }
    }

    // A file that a later step creates in the install gets a rule for this user, so the build can delete and move what the container writes.
    [Test]
    [Platform("Win")]
    public void GrantCurrentUser_NewFileInSubfolder_InheritsTheRule()
    {
        string install = _folder.NewFolder();
        string subfolder = Directory.CreateDirectory(Path.Combine(install, "sub")).FullName;

        using ContainerHost host = new();
        host.GrantCurrentUser(install);

        string file = Path.Combine(subfolder, "new.txt");
        File.WriteAllText(file, "x");
        var rules = new FileInfo(file).GetAccessControl().GetAccessRules(includeExplicit: false, includeInherited: true, typeof(SecurityIdentifier));

        Assert.That(
            rules.Cast<FileSystemAccessRule>().Any(rule =>
                rule.IdentityReference == WindowsIdentity.GetCurrent().User && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl)),
            Is.True);
    }

    // A folder that has the rule isn't set again, because each set propagates the rule through the whole tree. A deny on changing
    // the rules makes a 2nd set throw.
    [Test]
    [Platform("Win")]
    public void GrantCurrentUser_RuleThere_DoesNotSetTheRulesAgain()
    {
        DirectoryInfo install = new(_folder.NewFolder());
        using ContainerHost host = new();
        host.GrantCurrentUser(install.FullName);

        // Deny the owner too, because an owner can always change the rules unless an OWNER RIGHTS entry replaces that right.
        var security = install.GetAccessControl();
        SecurityIdentifier ownerRights = new(WellKnownSidType.WinCreatorOwnerRightsSid, null);

        foreach (var identity in (SecurityIdentifier[])[WindowsIdentity.GetCurrent().User!, ownerRights])
        {
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.ChangePermissions, AccessControlType.Deny));
        }

        install.SetAccessControl(security);

        // Check the deny first, because without a deny that works the test passes when the rules are set again.
        var changed = install.GetAccessControl();
        changed.AddAccessRule(new FileSystemAccessRule(ownerRights, FileSystemRights.Read, AccessControlType.Allow));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => install.SetAccessControl(changed), Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(() => host.GrantCurrentUser(install.FullName), Throws.Nothing);
        }
    }

    // A step runs its own image and command, gets its variables as NAME=value, mounts a read-only folder with :ro and has its owner as a label.
    [Test]
    public void NewStepParameters_Step_MapsImageCommandVariablesAndMounts()
    {
        string tools = Path.Combine(_folder.NewFolder(), "a b");
        string scripts = _folder.NewFolder();
        BuildStep step = new(
            "build:0123456789abcdef",
            ["cmd", "/c", @"C:\r\step.bat x > C:\l\step.log 2>&1"],
            [new(tools, @"C:\e", ReadOnly: false), new(scripts, @"C:\r", ReadOnly: true)],
            new Dictionary<string, string> { ["A"] = "b c", ["EMPTY"] = "" },
            @"D:\out\build");

        var parameters = ContainerHost.NewStepParameters(step);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters.Image, Is.EqualTo("build:0123456789abcdef"));
            Assert.That(parameters.Cmd, Is.EqualTo(["cmd", "/c", @"C:\r\step.bat x > C:\l\step.log 2>&1"]));
            Assert.That(parameters.HostConfig?.Binds, Is.EqualTo([$@"{tools}:C:\e", $@"{scripts}:C:\r:ro"]));
            Assert.That(parameters.Env, Is.EquivalentTo(["A=b c", "EMPTY="]));
            Assert.That(parameters.Labels, Is.EquivalentTo(new Dictionary<string, string> { ["asl-help.fixtures.owner"] = @"D:\out\build" }));
        }
    }

    // A failed removal after a failed start keeps the start failure, because it has the reason the user needs.
    [Test]
    public void StartAsync_StartAndRemovalFail_ThrowsTheStartFailure()
    {
        int removals = 0;
        var engine = FakeDocker.Engine(
            start: () => FakeDocker.Json(HttpStatusCode.InternalServerError, """{"message":"start broke"}"""),
            wait: () => Task.FromResult(FakeDocker.Json(HttpStatusCode.OK, """{"StatusCode":0}""")),
            remove: () =>
            {
                removals++;

                return FakeDocker.Json(HttpStatusCode.InternalServerError, """{"message":"remove broke"}""");
            });

        using ContainerHost host = new(FakeDocker.NewClient(engine));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                async () => await host.StartAsync(NewStep(), CancellationToken.None),
                Throws.InvalidOperationException
                    .With.Message.Contains("start broke")
                    .And.Message.Not.Contains("remove broke")
                    .And.InnerException.InstanceOf<DockerApiException>());
            Assert.That(removals, Is.EqualTo(1));
        }
    }

    // A wait that loses the engine fails the wait and the exit code with the wrapped Docker failure, so the caller prints a
    // Docker message with the reason.
    [Test]
    public async Task WaitForExitAsync_EngineLost_ThrowsTheWrappedFailure()
    {
        var engine = FakeDocker.Engine(
            start: () => new HttpResponseMessage(HttpStatusCode.NoContent),
            wait: () => Task.FromException<HttpResponseMessage>(new HttpRequestException("engine gone")),
            remove: () => new HttpResponseMessage(HttpStatusCode.NoContent));

        using ContainerHost host = new(FakeDocker.NewClient(engine));
        var run = await host.StartAsync(NewStep(), CancellationToken.None);

        await using (run)
        {
            var waitFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitForExitAsync(CancellationToken.None));
            var exitCodeFailure = Assert.Throws<InvalidOperationException>(() => _ = run.ExitCode);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(run.HasExited, Is.True);

                foreach (var failure in (Exception?[])[waitFailure, exitCodeFailure])
                {
                    Assert.That(failure?.Message, Does.Contain("build:0123456789abcdef").And.Contain("engine gone"));
                    Assert.That(failure?.InnerException, Is.InstanceOf<HttpRequestException>());
                }
            }
        }
    }

    // A removal that fails while the container runs, such as after a timeout, keeps the failure of the run,
    // and the wait that the removal ends leaves no failed task behind.
    [Test]
    [NonParallelizable]
    public async Task DisposeAsync_RunningContainerAndRemovalFails_ThrowsNothingAndObservesTheWait()
    {
        TaskCompletionSource<HttpResponseMessage> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Exception> unobserved = [];

        void addUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            // Keep only the failure of this wait, because the event reports the tasks of every test in the process.
            if (!args.Exception.ToString().Contains("container gone", StringComparison.Ordinal))
            {
                return;
            }

            lock (unobserved)
            {
                unobserved.Add(args.Exception);
            }
        }

        var engine = FakeDocker.Engine(
            start: () => new HttpResponseMessage(HttpStatusCode.NoContent),
            wait: () => waiting.Task,
            remove: () =>
            {
                _ = waiting.TrySetException(new HttpRequestException("container gone"));

                return FakeDocker.Json(HttpStatusCode.InternalServerError, """{"message":"remove broke"}""");
            });

        TaskScheduler.UnobservedTaskException += addUnobserved;

        try
        {
            await StartAndDisposeAsync(engine);

            // Collect twice, because only the finalizer of a failed task that nobody observed reports it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= addUnobserved;
        }

        lock (unobserved)
        {
            Assert.That(unobserved, Is.Empty);
        }
    }

    // An empty argument string adds nothing after the executable, because the engine would pass an empty argument on.
    [Test]
    public void NewContainerParameters_NoArguments_RunsOnlyTheExecutable()
    {
        var parameters = ContainerHost.NewContainerParameters(_folder.NewFolder(), "Game.exe", "");

        Assert.That(string.Join('|', parameters.Cmd), Is.EqualTo(@"C:\player\Game.exe"));
    }

    // An image on the machine is found and a missing one isn't, each with 1 request that pulls nothing.
    [TestCase(HttpStatusCode.OK, """{"Id":"sha256:1"}""", true)]
    [TestCase(HttpStatusCode.NotFound, """{"message":"no such image"}""", false)]
    public async Task HasImageAsync_InspectAnswer_ReturnsWhetherTheImageIsThere(HttpStatusCode code, string body, bool expected)
    {
        List<string> requests = [];
        CallbackHandler engine = new((request, _) =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");

            return Task.FromResult(FakeDocker.Json(code, body));
        });

        using ContainerHost host = new(FakeDocker.NewClient(engine));
        bool found = await host.HasImageAsync("build:0123456789abcdef", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.EqualTo(expected));
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0], Does.StartWith("GET ").And.EndWith("/json"));
        }
    }

    // A lookup that loses the engine throws the wrapped Docker failure with the image.
    [Test]
    public void HasImageAsync_EngineLost_ThrowsTheWrappedFailure()
    {
        CallbackHandler engine = new((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("engine gone")));

        using ContainerHost host = new(FakeDocker.NewClient(engine));

        Assert.That(
            async () => await host.HasImageAsync("build:0123456789abcdef", CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("look up")
                .And.Message.Contains("build:0123456789abcdef")
                .And.Message.Contains("engine gone")
                .And.InnerException.InstanceOf<HttpRequestException>());
    }

    // The list asks for the running and the stopped containers with the label of the owner and each one is removed. A
    // container that is gone at its removal doesn't stop the removal of the next one.
    [TestCase(HttpStatusCode.NoContent, "")]
    [TestCase(HttpStatusCode.NotFound, """{"message":"no such container"}""")]
    public async Task RemoveStepsAsync_TwoContainers_RemovesBoth(HttpStatusCode firstRemoval, string firstBody)
    {
        string query = "";
        List<string> removed = [];
        CallbackHandler engine = new((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.EndsWith("/containers/json", StringComparison.Ordinal))
            {
                query = Uri.UnescapeDataString(request.RequestUri.Query);

                return Task.FromResult(FakeDocker.Json(HttpStatusCode.OK, """[{"Id":"a1"},{"Id":"b2"}]"""));
            }

            if (request.Method == HttpMethod.Delete)
            {
                removed.Add(request.RequestUri.Segments[^1]);

                return Task.FromResult(FakeDocker.Json(removed.Count == 1 ? firstRemoval : HttpStatusCode.NoContent, firstBody));
            }

            return Task.FromResult(FakeDocker.Json(HttpStatusCode.InternalServerError, """{"message":"unexpected request"}"""));
        });

        using ContainerHost host = new(FakeDocker.NewClient(engine));
        await host.RemoveStepsAsync("build-1", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Does.Contain("all=1").And.Contain("asl-help.fixtures.owner=build-1"));
            Assert.That(removed, Is.EqualTo(["a1", "b2"]));
        }
    }

    // A list that fails throws the wrapped Docker failure with the owner.
    [Test]
    public void RemoveStepsAsync_ListFails_ThrowsTheWrappedFailure()
    {
        CallbackHandler engine = new((_, _) =>
            Task.FromResult(FakeDocker.Json(HttpStatusCode.InternalServerError, """{"message":"list broke"}""")));

        using ContainerHost host = new(FakeDocker.NewClient(engine));

        Assert.That(
            async () => await host.RemoveStepsAsync("build-1", CancellationToken.None),
            Throws.InvalidOperationException
                .With.Message.Contains("remove")
                .And.Message.Contains("build-1")
                .And.Message.Contains("list broke")
                .And.InnerException.InstanceOf<DockerApiException>());
    }

    private async Task StartAndDisposeAsync(HttpMessageHandler engine)
    {
        using ContainerHost host = new(FakeDocker.NewClient(engine));
        var run = await host.StartAsync(NewStep(), CancellationToken.None);

        await Assert.ThatAsync(async () => await run.DisposeAsync(), Throws.Nothing);

        // Wait for the wait to fail, because the removal ends it on another thread.
        Assert.That(SpinWait.SpinUntil(() => run.HasExited, TimeSpan.FromSeconds(10)), Is.True);
    }

    private BuildStep NewStep()
    {
        return new BuildStep("build:0123456789abcdef", ["cmd", "/c", "exit 0"], [], new Dictionary<string, string>(), _folder.NewFolder());
    }
}
