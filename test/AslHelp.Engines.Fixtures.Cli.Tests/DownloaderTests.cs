using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeHttpHandler;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DownloaderTests
{
    // sha1 of "abc" from FIPS 180.
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";

    // sha1 of "abcde".
    private const string AbcdeSha1 = "03de6c570bfe24bfc328ccd7ca46b76eadaf4334";

    // sha1 of "abd", 1 byte off "abc".
    private const string AbdSha1Hex = "cb4cc28df0fdbe0ecf9d9662e294b118092a5735";

    private const string Url = "https://files.example/file.bin";

    private static readonly byte[] _abc = "abc"u8.ToArray();

    private string _destination = null!;
    private Downloader? _downloader;
    private TestFolder _folder = null!;
    private FakeHttpHandler _handler = null!;
    private string _partial = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _destination = Path.Combine(_folder.Root, "file.bin");
        _partial = _destination + ".partial";
    }

    [TearDown]
    public void TearDown()
    {
        _downloader?.Dispose();
        _folder.Delete();
    }

    // A fresh download sends 1 plain GET and leaves only the destination.
    [Test]
    public async Task DownloadAsync_FreshDownload_WritesBytesAndLeavesNoPartial()
    {
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
            Assert.That(_handler.Requests[0].Method, Is.EqualTo("GET"));
            Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(Url));
            Assert.That(_handler.Requests[0].Range, Is.Null);
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Has.Length.EqualTo(1).And.Contain(_destination));
        }
    }

    // A destination whose hash matches the pin is kept and nothing is sent.
    [Test]
    public async Task DownloadAsync_ExistingDestinationWithMatchingHash_SendsNoRequest()
    {
        await File.WriteAllBytesAsync(_destination, _abc);
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
        }
    }

    // A stale destination is downloaded again and replaced.
    [Test]
    public async Task DownloadAsync_ExistingDestinationWithWrongHash_DownloadsAndReplacesIt()
    {
        await File.WriteAllBytesAsync(_destination, "old"u8.ToArray());
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // A body with the wrong hash leaves nothing on disk and the message gives the URL and both hashes.
    [Test]
    public async Task DownloadAsync_WrongHash_DeletesPartialAndThrowsInvalidDataExceptionWithBothHashes()
    {
        byte[] abd = "abd"u8.ToArray();
        var pin = PinnedHash.Sha1(AbcSha1);
        var downloader = NewDownloader(_ => Ok(abd));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, pin, null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain(Url).And.Contain(AbcSha1).And.Contain(AbdSha1Hex));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A resume requests the bytes after the partial and hashes the partial before the tail.
    [Test]
    public async Task DownloadAsync_PartialPresentAndServer206_SendsRangeFromPartialLengthAndCompletesFile()
    {
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        var downloader = NewDownloader(_ => Range206(_abc, 2));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[0].Range, Is.EqualTo("bytes=2-"));
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // A server that ignores the range sends the whole file, which replaces the partial instead of following it.
    [Test]
    public async Task DownloadAsync_PartialPresentAndServer200_RestartsWithoutDuplicatingPrefix()
    {
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // A 206 that starts somewhere other than the partial length is refused and the partial is kept.
    [Test]
    public async Task DownloadAsync_PartialPresentAndServer206WrongStart_ThrowsHttpRequestExceptionAndKeepsPartial()
    {
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        var downloader = NewDownloader(_ => Range206(_abc, 0));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(exception.Message, Does.Contain(_partial));
            Assert.That(await File.ReadAllBytesAsync(_partial), Is.EqualTo("ab"u8.ToArray()));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A 206 that starts at the partial length but ends before the file is refused and the partial is kept, because the
    // part it leaves out would read as a wrong hash.
    [Test]
    public async Task DownloadAsync_PartialPresentAndServer206EndsEarly_ThrowsHttpRequestExceptionAndKeepsPartial()
    {
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        var downloader = NewDownloader(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("cd"u8.ToArray()) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 3, 5);

            return response;
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcdeSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(exception.Message, Does.Contain("bytes 2-3/5").And.Contain(_partial));
            Assert.That(await File.ReadAllBytesAsync(_partial), Is.EqualTo("ab"u8.ToArray()));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A partial that already has the pinned hash is moved without a request, so a run that stopped after its last write
    // finishes offline.
    [Test]
    public async Task DownloadAsync_CompletePartial_MovesItWithoutRequest()
    {
        await File.WriteAllBytesAsync(_partial, _abc);
        var downloader = NewDownloader(_ => throw new HttpRequestException("offline"));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // 416 with a wrong partial deletes it, so the next run starts fresh.
    [Test]
    public async Task DownloadAsync_Status416WithWrongPartial_DeletesPartialAndThrowsInvalidDataException()
    {
        byte[] abd = "abd"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, abd);
        var pin = PinnedHash.Sha1(AbcSha1);
        var downloader = NewDownloader(_ => Status(HttpStatusCode.RequestedRangeNotSatisfiable));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, pin, null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain(AbcSha1).And.Contain(AbdSha1Hex));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // 416 is only a resume response, so without a partial it fails like any other status.
    [Test]
    public async Task DownloadAsync_Status416WithoutPartial_ThrowsHttpRequestException()
    {
        var downloader = NewDownloader(_ => Status(HttpStatusCode.RequestedRangeNotSatisfiable));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.RequestedRangeNotSatisfiable));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // An empty partial sends a plain GET, because 'bytes=0-' covers the whole file anyway.
    [Test]
    public async Task DownloadAsync_EmptyPartial_SendsNoRangeAndDownloadsWhole()
    {
        await File.WriteAllBytesAsync(_partial, []);
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[0].Range, Is.Null);
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // A dropped connection keeps the bytes so far in the partial for the next resume.
    [Test]
    public async Task DownloadAsync_BodyFailsMidStream_KeepsPartialWithBytesSoFar()
    {
        var downloader = NewDownloader(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingStream(1000, () => { })),
        });

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(_destination), Is.False);
            Assert.That(new FileInfo(_partial).Length, Is.EqualTo(1000));
        }
    }

    // A failed download resumed from its partial ends with every byte once and the pin matches.
    [Test]
    public async Task DownloadAsync_FailedThenResumed_CompletesFile()
    {
        byte[] body = Pattern(5000);
        var pin = Sha1Pin(body);
        var downloader = NewDownloader(request => request.Range is null
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ThrowingStream(1000, () => { })) }
            : Range206(body, int.Parse(request.Range["bytes=".Length..^1], CultureInfo.InvariantCulture)));

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, pin, null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        await downloader.DownloadAsync(new Uri(Url), _destination, pin, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[1].Range, Is.EqualTo("bytes=1000-"));
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(body));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // A cancel during the body keeps the partial for the next resume.
    [Test]
    public async Task DownloadAsync_CanceledMidBody_ThrowsOperationCanceledAndKeepsPartial()
    {
        using CancellationTokenSource cancellation = new();
        var downloader = NewDownloader(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new PatternStream(4 * 1024 * 1024, cancellation.Cancel)),
        });

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(_partial), Is.True);
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // Any other status throws with the URL and the status and writes nothing (the fake handler follows no redirect).
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.Found)]
    public async Task DownloadAsync_NonSuccessStatus_ThrowsHttpRequestExceptionWithUrlAndStatusAndLeavesNoFile(HttpStatusCode status)
    {
        var downloader = NewDownloader(_ => Status(status));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(status));
            Assert.That(exception.Message, Does.Contain(Url).And.Contain(((int)status).ToString(CultureInfo.InvariantCulture)));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A body of more than 2 copy buffers plus a tail is hashed across every chunk in order.
    [Test]
    public async Task DownloadAsync_LargeBody_HashesEveryChunkInOrder()
    {
        byte[] body = Pattern(200_001);
        var downloader = NewDownloader(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(body)) });

        await downloader.DownloadAsync(new Uri(Url), _destination, Sha1Pin(body), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new FileInfo(_destination).Length, Is.EqualTo(200_001));
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(body));
        }
    }

    // Progress ends at the body length and never goes down.
    [Test]
    public async Task DownloadAsync_FreshDownload_ReportsFinalTotalToProgress()
    {
        byte[] body = "abcde"u8.ToArray();
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Ok(body));

        await downloader.DownloadAsync(new Uri(Url), _destination, Sha1Pin(body), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.Bytes[^1], Is.EqualTo(5));
            Assert.That(progress.Bytes, Is.Ordered);
        }
    }

    // A resumed download counts the partial bytes before the tail.
    [Test]
    public async Task DownloadAsync_ResumedDownload_ReportsTotalIncludingPrefix()
    {
        byte[] body = "abcde"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Range206(body, 2));

        await downloader.DownloadAsync(new Uri(Url), _destination, Sha1Pin(body), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.Bytes[0], Is.EqualTo(2));
            Assert.That(progress.Bytes[^1], Is.EqualTo(5));
        }
    }

    // A fresh download sends the Content-Length with every report, so a bar shows the share done.
    [Test]
    public async Task DownloadAsync_FreshDownload_ReportsTheContentLength()
    {
        byte[] body = "abcde"u8.ToArray();
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Ok(body));

        await downloader.DownloadAsync(new Uri(Url), _destination, Sha1Pin(body), progress, CancellationToken.None);

        Assert.That(progress.Values.ConvertAll(value => value.Length), Has.All.EqualTo(5));
    }

    // A resume whose Content-Range has no total reports the prefix plus the Content-Length, which is the length of the whole file.
    [Test]
    public async Task DownloadAsync_ResumeWithUnknownTotal_ReportsThePrefixPlusTheRest()
    {
        byte[] body = "abcde"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(body, 2, 3) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(2, 4);

            return response;
        });

        await downloader.DownloadAsync(new Uri(Url), _destination, Sha1Pin(body), progress, CancellationToken.None);

        Assert.That(progress.Values.ConvertAll(value => value.Length), Has.All.EqualTo(5));
    }

    // A resume reports the length of the whole file from Content-Range, because Content-Length then counts only the rest.
    [Test]
    public async Task DownloadAsync_ResumedDownload_ReportsTheLengthOfTheWholeFile()
    {
        byte[] body = "abcde"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Range206(body, 2));

        await downloader.DownloadAsync(new Uri(Url), _destination, Sha1Pin(body), progress, CancellationToken.None);

        Assert.That(progress.Values.ConvertAll(value => value.Length), Has.All.EqualTo(5));
    }

    // A missing destination folder is created.
    [Test]
    public async Task DownloadAsync_MissingDestinationFolder_CreatesIt()
    {
        string destination = Path.Combine(_folder.Root, "x", "y", "file.bin");
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(_abc));
    }

    // A relative URL is refused before any request with a message to pass an absolute one.
    [Test]
    public async Task DownloadAsync_RelativeUrl_ThrowsArgumentExceptionAndSendsNothing()
    {
        var downloader = NewDownloader(_ => Ok(_abc));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => downloader.DownloadAsync(
                new Uri("file.bin", UriKind.Relative),
                _destination,
                PinnedHash.Sha1(AbcSha1),
                null,
                CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("url"));
            Assert.That(_handler.Requests, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    private Downloader NewDownloader(Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _handler = new FakeHttpHandler(respond);
        _downloader = new Downloader(_handler);

        return _downloader;
    }

    // Hash through IncrementalHash with the pinned algorithm, as the downloader does.
    private static PinnedHash Sha1Pin(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(PinnedHash.Algorithm);
        hash.AppendData(bytes);

        return PinnedHash.Sha1(Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static byte[] Pattern(int length)
    {
        byte[] bytes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i & 0xFF);
        }

        return bytes;
    }
}
