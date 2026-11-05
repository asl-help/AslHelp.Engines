using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DavStoreTimeoutEdgeCaseTests
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

    // A stalled GET with spaces and non-ASCII chars in both paths has the plain store path in the message and deletes the partial file.
    [Test]
    public async Task TryDownloadAsync_StallOnUnicodePaths_ThrowsWithThePlainPathAndDeletesPartial()
    {
        string destination = Path.Combine(_folder.Root, "Ünïcode 名前", "Fïle ß.tar.zst");
        var store = NewStore(new FakeDavHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_clock, TransferTimer.StallLimit, 1000)),
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => store.TryDownloadAsync("ä b/ü c.tar.zst", destination, null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("GET 'ä b/ü c.tar.zst'").And.Contain(Seconds(TransferTimer.StallLimit)));
            Assert.That(Directory.GetFileSystemEntries(Path.GetDirectoryName(destination)!), Is.Empty);
        }
    }

    // A server that sends no headers to a MKCOL fails the upload with the collection path after the stall limit, so the PUT is never sent.
    [Test]
    public async Task UploadAsync_ServerSendsNoHeadersToMkcol_ThrowsTimeoutExceptionAndSendsNoPut()
    {
        string source = WriteSource([1, 2, 3]);
        List<string> requests = [];
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            string uri = request.RequestUri!.AbsoluteUri;
            requests.Add(request.Method + " " + uri);

            if (uri != Base + "a/b")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            await _clock.StallAsync(TransferTimer.StallLimit, token);

            throw new UnreachableException();
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => store.UploadAsync(source, "a/b/f.tar.zst", null, CancellationToken.None));

        string[] expected = ["MKCOL " + Base + "a", "MKCOL " + Base + "a/b"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("MKCOL 'a/b' got no response").And.Contain(Seconds(TransferTimer.StallLimit)));
            Assert.That(requests, Is.EqualTo(expected));
        }
    }

    // A cancel by the caller during the wait for the response to a MKCOL stays a cancel, so only a passed limit becomes a timeout.
    [Test]
    public async Task UploadAsync_CanceledWhileWaitingForMkcol_ThrowsOperationCanceledException()
    {
        string source = WriteSource([1, 2, 3]);
        using CancellationTokenSource caller = new();
        var store = NewStore(new CallbackHandler(async (_, token) =>
        {
            await _clock.StallAsync(TimeSpan.Zero, token);

            throw new UnreachableException();
        }));

        var upload = store.UploadAsync(source, "a/f.tar.zst", null, caller.Token);
        await caller.CancelAsync();

        await Assert.ThatAsync(() => upload, Throws.InstanceOf<OperationCanceledException>());
    }

    // A cancel by the caller during a PUT body write that moves no bytes stays a cancel, so only the limits become timeouts.
    [Test]
    public async Task UploadAsync_CanceledDuringStalledWrite_ThrowsOperationCanceledException()
    {
        string source = WriteSource([1, 2, 3]);
        using CancellationTokenSource caller = new();
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            using StallingStream sink = new(_clock, TimeSpan.Zero, 0);
            await request.Content!.CopyToAsync(sink, token);

            throw new UnreachableException();
        }));

        var upload = store.UploadAsync(source, "f.tar.zst", null, caller.Token);
        await caller.CancelAsync();

        await Assert.ThatAsync(() => upload, Throws.InstanceOf<OperationCanceledException>());
    }

    // A PUT body whose writes take longer than every limit in total is sent whole, because no limit covers the whole body.
    [Test]
    public async Task UploadAsync_WritesTakeLongerThanEveryLimitInTotal_SendsTheWholeFile()
    {
        string source = WriteSource(TestFolder.Noise((7 * 81_920) + 1));
        using SlowStream sink = new(_clock, TransferTimer.StallLimit * 0.75, []);
        var store = NewStore(new CallbackHandler(async (request, token) =>
        {
            await request.Content!.CopyToAsync(sink, token);

            return new HttpResponseMessage(HttpStatusCode.Created);
        }));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_clock.GetElapsedTime(0), Is.GreaterThan(TransferTimer.StoreLimit));
            Assert.That(sink.Written, Is.EqualTo(await File.ReadAllBytesAsync(source)));
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
