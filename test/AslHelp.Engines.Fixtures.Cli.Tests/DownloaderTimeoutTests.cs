using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DownloaderTimeoutTests
{
    // sha1 of "abc" from FIPS 180.
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";

    private const string Url = "https://files.example/file.bin";

    private ManualTimeProvider _clock = null!;
    private string _destination = null!;
    private Downloader? _downloader;
    private TestFolder _folder = null!;
    private string _partial = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _clock = new ManualTimeProvider();
        _destination = Path.Combine(_folder.Root, "file.bin");
        _partial = _destination + ".partial";
    }

    [TearDown]
    public void TearDown()
    {
        _downloader?.Dispose();
        _folder.Delete();
    }

    // A server that sends no headers fails with the response message after the stall limit and leaves no partial to resume from.
    [Test]
    public async Task DownloadAsync_ServerSendsNoHeaders_ThrowsTimeoutExceptionWithNoResponse()
    {
        var downloader = NewDownloader(new CallbackHandler(async (_, token) =>
        {
            await _clock.StallAsync(TransferTimer.StallLimit, token);

            throw new UnreachableException();
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain($"GET '{Url}' got no response").And.Contain(Seconds(TransferTimer.StallLimit)));
            Assert.That(exception.Message, Does.Contain("run again").And.Not.Contain("resumes"));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A body that goes silent after 1000 bytes fails with the stall limit and keeps the 1000 bytes in the partial for the next run to resume from.
    [Test]
    public async Task DownloadAsync_BodyStallsAfter1000Bytes_ThrowsTimeoutExceptionAndKeepsPartial()
    {
        var downloader = NewDownloader(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_clock, TransferTimer.StallLimit, 1000)),
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain($"GET '{Url}' moved no bytes").And.Contain(Seconds(TransferTimer.StallLimit)));
            Assert.That(exception.Message, Does.Contain("resumes from the 1000 bytes"));
            Assert.That(new FileInfo(_partial).Length, Is.EqualTo(1000));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A body whose every read takes 3/4 of the stall limit completes, because the limit starts over before each read.
    [Test]
    public async Task DownloadAsync_EveryReadTakesMostOfTheStallLimit_Completes()
    {
        byte[] abc = "abc"u8.ToArray();
        var downloader = NewDownloader(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new SlowStream(_clock, TransferTimer.StallLimit * 0.75, abc)),
        }));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(abc));
    }

    // A cancel by the caller reaches a stalled read and stays a cancel, so only the limits of the timer become timeouts.
    [Test]
    public async Task DownloadAsync_CanceledDuringStalledRead_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource caller = new();
        var downloader = NewDownloader(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_clock, TimeSpan.Zero, 0)),
        }));

        var download = downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, caller.Token);
        await caller.CancelAsync();

        await Assert.ThatAsync(() => download, Throws.InstanceOf<OperationCanceledException>());
    }

    // A cancel by the caller during the wait for the response headers stays a cancel, so only a passed limit becomes a timeout.
    [Test]
    public async Task DownloadAsync_CanceledWhileWaitingForHeaders_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource caller = new();
        var downloader = NewDownloader(new CallbackHandler(async (_, token) =>
        {
            await _clock.StallAsync(TimeSpan.Zero, token);

            throw new UnreachableException();
        }));

        var download = downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, caller.Token);
        await caller.CancelAsync();

        await Assert.ThatAsync(() => download, Throws.InstanceOf<OperationCanceledException>());
    }

    // A real handler stops a TLS handshake that never ends at its connect timeout, which fails the download with the connect message.
    [Test]
    public async Task DownloadAsync_TlsHandshakeNeverEnds_ThrowsTimeoutExceptionWithConnectMessage()
    {
        var handler = Downloader.CreateHandler();
        handler.ConnectTimeout = TimeSpan.FromMilliseconds(100);

        // Turn the proxy off, so the handler connects to the server through the callback and not to a proxy of the machine.
        handler.UseProxy = false;
        handler.ConnectCallback = (_, _) => ValueTask.FromResult<Stream>(new StallingStream(_clock, TimeSpan.Zero, 0));

        var downloader = NewDownloader(handler);

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain($"GET '{Url}' could not connect").And.Contain("run again"));
            Assert.That(exception.InnerException, Is.TypeOf<TimeoutException>());
        }
    }

    // The default handler follows redirects, because file hosts redirect a download to a mirror.
    [Test]
    public void CreateHandler_Always_FollowsRedirects()
    {
        using var handler = Downloader.CreateHandler();

        Assert.That(handler.AllowAutoRedirect, Is.True);
    }

    private Downloader NewDownloader(HttpMessageHandler handler)
    {
        _downloader = new Downloader(handler, _clock);

        return _downloader;
    }

    private static string Seconds(TimeSpan limit)
    {
        return $"{limit.TotalSeconds} seconds";
    }
}
