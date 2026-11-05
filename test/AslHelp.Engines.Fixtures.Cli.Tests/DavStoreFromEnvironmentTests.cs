using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

[NonParallelizable]
public sealed class DavStoreFromEnvironmentTests
{
    private const string Token = "app-secret-123";
    private const string Url = "https://dav.example/remote.php/dav/files/u/fixtures";
    private const string User = "u";

    private static readonly string[] _variables = [DavStore.UrlVariable, DavStore.UserVariable, DavStore.TokenVariable];

    private readonly Dictionary<string, string?> _saved = [];

    [SetUp]
    public void SetUp()
    {
        foreach (string variable in _variables)
        {
            _saved[variable] = Environment.GetEnvironmentVariable(variable);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach ((string variable, string? value) in _saved)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }

    // With all 3 variables set, the store is built.
    [Test]
    public void FromEnvironment_AllSet_ReturnsStore()
    {
        SetAll(Url, User, Token);

        using var store = DavStore.FromEnvironment();

        Assert.That(store, Is.Not.Null);
    }

    // The 3 variables reach the requests, the URL as the base of each path and the user and token as the Basic header.
    [Test]
    public async Task FromEnvironment_AllSet_SendsRequestsWithTheVariables()
    {
        SetAll(Url, User, Token);
        FakeDavHandler handler = new(_ => FakeDavHandler.Status(HttpStatusCode.NotFound));
        using var store = DavStore.FromEnvironment(handler);

        _ = await store.TryDownloadAsync("a/f.tar.zst", Path.Combine(Path.GetTempPath(), "never-written.bin"), null, CancellationToken.None);

        var request = handler.Requests[0];
        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Token}"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Uri.AbsoluteUri, Is.EqualTo(Url + "/a/f.tar.zst"));
            Assert.That(request.Authorization?.Scheme, Is.EqualTo("Basic"));
            Assert.That(request.Authorization?.Parameter, Is.EqualTo(expected));
        }
    }

    // A missing or empty variable is the only one in the message and the token value never shows.
    [TestCase(DavStore.UrlVariable, null)]
    [TestCase(DavStore.UrlVariable, "")]
    [TestCase(DavStore.UserVariable, null)]
    [TestCase(DavStore.UserVariable, "")]
    [TestCase(DavStore.TokenVariable, null)]
    [TestCase(DavStore.TokenVariable, "")]
    public void FromEnvironment_MissingVariable_ThrowsWithTheVariable(string variable, string? value)
    {
        SetAll(Url, User, Token);
        Environment.SetEnvironmentVariable(variable, value);

        var exception = Assert.Throws<InvalidOperationException>(() => DavStore.FromEnvironment());

        using (Assert.EnterMultipleScope())
        {
            foreach (string other in _variables)
            {
                Assert.That(exception!.Message.Contains(other, StringComparison.Ordinal), Is.EqualTo(other == variable), other);
            }

            Assert.That(exception!.Message, Does.Not.Contain(Token));
        }
    }

    // Every missing variable is listed at once, so the user fixes them in 1 go.
    [Test]
    public void FromEnvironment_AllMissing_ThrowsWithAllThree()
    {
        SetAll(null, null, null);

        Assert.That(
            () => DavStore.FromEnvironment(),
            Throws.TypeOf<InvalidOperationException>().With.Message
                .Contain(DavStore.UrlVariable)
                .And.Message.Contain(DavStore.UserVariable)
                .And.Message.Contain(DavStore.TokenVariable));
    }

    // A URL that is not an absolute http or https URL with no query or fragment cannot anchor requests, so the message lists its variable.
    [TestCase("fixtures/cache")]
    [TestCase("ftp://dav.example/fixtures")]
    [TestCase("https://dav.example/fixtures?x=1")]
    public void FromEnvironment_BadUrl_ThrowsWithTheUrlVariable(string value)
    {
        SetAll(value, User, Token);

        Assert.That(
            () => DavStore.FromEnvironment(),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contain(DavStore.UrlVariable));
    }

    // Basic auth puts the token in every request, so a plain http URL to another machine is refused with its variable in the message.
    [Test]
    public void FromEnvironment_HttpUrlToAnotherMachine_ThrowsWithTheUrlVariable()
    {
        SetAll("http://dav.example/fixtures", User, Token);

        Assert.That(
            () => DavStore.FromEnvironment(),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contain(DavStore.UrlVariable).And.Message.Contain("https"));
    }

    // A plain http URL to a loopback address never leaves the machine, so the store is built.
    [TestCase("http://localhost:8080/dav")]
    [TestCase("http://127.0.0.1/dav")]
    [TestCase("http://[::1]/dav")]
    public void FromEnvironment_HttpUrlOnThisMachine_ReturnsStore(string value)
    {
        SetAll(value, User, Token);

        using var store = DavStore.FromEnvironment();

        Assert.That(store, Is.Not.Null);
    }

    // With all 3 variables missing or empty there is no cache, so the call returns null and writes the line that lists the 3 variables.
    [TestCase(null)]
    [TestCase("")]
    public void FromEnvironmentIfSet_NoneSet_ReturnsNullAndWritesTheVariables(string? value)
    {
        using StringWriter output = new();
        SetAll(value, value, value);

        using var store = DavStore.FromEnvironmentIfSet(output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store, Is.Null);
            Assert.That(
                output.ToString(),
                Does.Contain(DavStore.UrlVariable).And.Contain(DavStore.UserVariable).And.Contain(DavStore.TokenVariable));
        }
    }

    // With all 3 variables set the call writes nothing and returns a store, as FromEnvironment does.
    [Test]
    public void FromEnvironmentIfSet_AllSet_ReturnsStoreAndWritesNothing()
    {
        using StringWriter output = new();
        SetAll(Url, User, Token);

        using var store = DavStore.FromEnvironmentIfSet(output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store, Is.Not.Null);
            Assert.That(output.ToString(), Is.Empty);
        }
    }

    // With exactly 1 variable set the call throws, the message lists only the other 2 with the way to build locally and the token value never shows.
    [TestCase(DavStore.UrlVariable, Url)]
    [TestCase(DavStore.UserVariable, User)]
    [TestCase(DavStore.TokenVariable, Token)]
    public void FromEnvironmentIfSet_OneSet_ThrowsWithTheOtherTwo(string variable, string value)
    {
        SetAll(null, null, null);
        Environment.SetEnvironmentVariable(variable, value);

        var exception = Assert.Throws<InvalidOperationException>(() => DavStore.FromEnvironmentIfSet(TextWriter.Null));

        using (Assert.EnterMultipleScope())
        {
            foreach (string other in _variables)
            {
                Assert.That(exception!.Message.Contains(other, StringComparison.Ordinal), Is.EqualTo(other != variable), other);
            }

            Assert.That(exception!.Message, Does.Not.Contain(Token).And.Contain("build locally"));
        }
    }

    private static void SetAll(string? url, string? user, string? token)
    {
        Environment.SetEnvironmentVariable(DavStore.UrlVariable, url);
        Environment.SetEnvironmentVariable(DavStore.UserVariable, user);
        Environment.SetEnvironmentVariable(DavStore.TokenVariable, token);
    }
}
