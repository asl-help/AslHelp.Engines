using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DavStoreTimeoutTests
{
    private const string Base = "https://dav.example/remote.php/dav/files/u/fixtures/";
    private const string Token = "app-secret-123";
    private const string User = "u";

    private ManualTimeProvider _clock = null!;
    private TestFolder _folder = null!;
    private DavStore? _store;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _clock = new ManualTimeProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _store?.Dispose();
        _folder.Delete();
    }

    // A server that sends no headers fails the GET with the response message after the stall limit and the fix that points at the server.
    [Test]
    public async Task TryDownloadAsync_ServerSendsNoHeaders_ThrowsTimeoutExceptionWithNoResponse()
    {
        var store = NewStore(new CallbackHandler(async (_, token) =>
        {
            await _clock.StallAsync(TransferTimer.StallLimit, token);

            throw new UnreachableException();
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.tar.zst"), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("GET 'a/f.tar.zst' got no response").And.Contain(Seconds(TransferTimer.StallLimit)));
            Assert.That(exception.Message, Does.Contain("is up"));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A body that goes silent after 1000 bytes fails with the stall limit and deletes the partial file.
    [Test]
    public async Task TryDownloadAsync_BodyStallsAfter1000Bytes_ThrowsTimeoutExceptionAndDeletesPartial()
    {
        var store = NewStore(new FakeDavHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_clock, TransferTimer.StallLimit, 1000)),
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.tar.zst"), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("GET 'a/f.tar.zst' moved no bytes").And.Contain(Seconds(TransferTimer.StallLimit)));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A body whose every read takes 3/4 of the stall limit is written whole, because the limit starts over before each read.
    [Test]
    public async Task TryDownloadAsync_EveryReadTakesMostOfTheStallLimit_WritesTheFile()
    {
        byte[] bytes = [1, 2, 3, 4];
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(new FakeDavHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new SlowStream(_clock, TransferTimer.StallLimit * 0.75, bytes)),
        }));

        bool found = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(bytes));
        }
    }

    // A cancel by the caller reaches a stalled read, stays a cancel and deletes the partial file.
    [Test]
    public async Task TryDownloadAsync_CanceledDuringStalledRead_ThrowsOperationCanceledExceptionAndLeavesNoFile()
    {
        using CancellationTokenSource caller = new();
        var store = NewStore(new FakeDavHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_clock, TimeSpan.Zero, 0)),
        }));

        var download = store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.tar.zst"), null, caller.Token);
        await caller.CancelAsync();

        await Assert.ThatAsync(() => download, Throws.InstanceOf<OperationCanceledException>());

        Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
    }

    // A real handler stops a TLS handshake that never ends at its connect timeout, which fails the GET with the connect message and leaves no file.
    [Test]
    public async Task TryDownloadAsync_TlsHandshakeNeverEnds_ThrowsTimeoutExceptionWithConnectMessage()
    {
        var handler = DavStore.CreateHandler();
        handler.ConnectTimeout = TimeSpan.FromMilliseconds(100);

        // Turn the proxy off, so the handler connects to the server through the callback and not to a proxy of the machine.
        handler.UseProxy = false;
        handler.ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(new StallingStream(_clock, TimeSpan.Zero, 0));

        var store = NewStore(handler);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.tar.zst"), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("GET 'a/f.tar.zst' could not connect").And.Contain("is up"));
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A server that takes no bytes of the PUT body fails the upload with the stall limit, because the content restarts the limit before each write.
    [Test]
    public async Task UploadAsync_ServerTakesNoBytes_ThrowsTimeoutExceptionWithStallLimit()
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            using StallingStream sink = new(_clock, TransferTimer.StallLimit, 0);
            await request.Content!.CopyToAsync(sink, token);

            throw new UnreachableException();
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("PUT 'f.tar.zst' moved no bytes").And.Contain(Seconds(TransferTimer.StallLimit)));
    }

    // A server that takes the whole PUT body and sends no headers fails with the store limit, which starts after the last byte.
    [Test]
    public async Task UploadAsync_NoHeadersAfterTheLastByte_ThrowsTimeoutExceptionWithStoreLimit()
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            await request.Content!.CopyToAsync(Stream.Null, token);
            await _clock.StallAsync(TransferTimer.StoreLimit, token);

            throw new UnreachableException();
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("PUT 'f.tar.zst' got no response").And.Contain(Seconds(TransferTimer.StoreLimit)));
    }

    // A PUT body whose every write takes 3/4 of the stall limit is sent whole, because the limit starts over before each write.
    [Test]
    public async Task UploadAsync_EveryWriteTakesMostOfTheStallLimit_SendsTheWholeFile()
    {
        string source = WriteSource(TestFolder.Noise((3 * 81_920) + 1));
        using SlowStream sink = new(_clock, TransferTimer.StallLimit * 0.75, []);
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            await request.Content!.CopyToAsync(sink, token);

            return new HttpResponseMessage(HttpStatusCode.Created);
        }));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        Assert.That(sink.Written, Is.EqualTo(await File.ReadAllBytesAsync(source)));
    }

    // A PUT body sent twice, as the handler does when it retries a request on a new connection, is the whole file both times.
    [Test]
    public async Task UploadAsync_BodySentTwice_SendsTheWholeFileEachTime()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        string source = WriteSource(bytes);
        using MemoryStream first = new();
        using MemoryStream second = new();
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            await request.Content!.CopyToAsync(first, token);
            await request.Content.CopyToAsync(second, token);

            return new HttpResponseMessage(HttpStatusCode.Created);
        }));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ToArray(), Is.EqualTo(bytes));
            Assert.That(second.ToArray(), Is.EqualTo(bytes));
        }
    }

    private DavStore NewStore(HttpMessageHandler handler)
    {
        _store = new DavStore(new Uri(Base), User, Token, handler, _clock);

        return _store;
    }

    private string WriteSource(byte[] bytes)
    {
        string source = Path.Combine(_folder.Root, "source.tar.zst");
        File.WriteAllBytes(source, bytes);

        return source;
    }

    private static string Seconds(TimeSpan limit)
    {
        return $"{limit.TotalSeconds} seconds";
    }
}
