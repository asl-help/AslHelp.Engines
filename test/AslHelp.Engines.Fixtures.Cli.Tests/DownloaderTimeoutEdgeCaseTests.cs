using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeHttpHandler;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DownloaderTimeoutEdgeCaseTests
{
    // sha1 of "abc" from FIPS 180.
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";

    // sha1 of "abcdefg".
    private const string AbcdefgSha1 = "2fb5e13419fc89246865e7a324f476ec624e8740";

    private const string Url = "https://files.example/file.bin";

    private static readonly byte[] _abc = "abc"u8.ToArray();

    private ManualTimeProvider _clock = null!;
    private string _destination = null!;
    private Downloader? _downloader;
    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _clock = new ManualTimeProvider();
        _destination = Path.Combine(_folder.Root, "file.bin");
    }

    [TearDown]
    public void TearDown()
    {
        _downloader?.Dispose();
        _folder.Delete();
    }

    // A progress report that blocks for longer than the stall limit, like a console write during a text selection, does not fail the download.
    [Test]
    public async Task DownloadAsync_ProgressReportBlocksLongerThanTheStallLimit_Completes()
    {
        var downloader = NewDownloader(new FakeHttpHandler(_ => Ok(_abc)));
        BlockingProgress progress = new(_clock, TransferTimer.StallLimit * 2);

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), progress, CancellationToken.None);

        Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
    }

    // A body whose reads take longer than every limit in total completes, because no limit covers the whole transfer.
    [Test]
    public async Task DownloadAsync_ReadsTakeLongerThanEveryLimitInTotal_Completes()
    {
        byte[] abcdefg = "abcdefg"u8.ToArray();
        var downloader = NewDownloader(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new SlowStream(_clock, TransferTimer.StallLimit * 0.75, abcdefg)),
        }));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcdefgSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_clock.GetElapsedTime(0), Is.GreaterThan(TransferTimer.StoreLimit));
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(abcdefg));
        }
    }

    // Headers that arrive as the stall limit passes fail with the response message, because the read after them keeps the limit that passed.
    [Test]
    public async Task DownloadAsync_HeadersArriveAsTheStallLimitPasses_ThrowsTimeoutExceptionWithNoResponse()
    {
        var downloader = NewDownloader(new CallbackHandler((_, _) =>
        {
            _clock.Advance(TransferTimer.StallLimit);

            return Task.FromResult(Ok(_abc));
        }));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("got no response").And.Not.Contain("moved no bytes"));
    }

    // A handler that cancels on its own with no TimeoutException inside keeps its cancel, so the message never reports a limit that did not pass.
    [Test]
    public async Task DownloadAsync_HandlerCancelsWithoutTimeout_ThrowsTaskCanceledException()
    {
        var downloader = NewDownloader(new FakeHttpHandler(_ => throw new TaskCanceledException()));

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None),
            Throws.TypeOf<TaskCanceledException>());
    }

    private Downloader NewDownloader(HttpMessageHandler handler)
    {
        _downloader = new Downloader(handler, _clock);

        return _downloader;
    }
}

/// <summary>
///     Moves the clock forward by a fixed time on each report, like a console write that blocks during a text selection.
/// </summary>
/// <param name="clock">The clock that each report moves forward.</param>
/// <param name="block">The time each report takes on <paramref name="clock"/>.</param>
file sealed class BlockingProgress(
    ManualTimeProvider clock,
    TimeSpan block) : IProgress<TransferProgress>
{
    /// <inheritdoc/>
    public void Report(TransferProgress value)
    {
        clock.Advance(block);
    }
}
