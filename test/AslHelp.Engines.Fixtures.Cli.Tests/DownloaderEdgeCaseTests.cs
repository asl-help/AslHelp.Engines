using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeHttpHandler;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DownloaderEdgeCaseTests
{
    // sha1 of "abc" from FIPS 180.
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";

    // sha1 of 0 bytes.
    private const string EmptySha1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";

    private const string Url = "https://files.example/file.bin";

    // sha1 of "xyc", a wrong prefix plus the right tail of "abc".
    private const string XycSha1Hex = "19287cc3222510e213117369b40189803cfdeb2d";

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

    // An empty body still ends with a 0-byte destination and a final progress total of 0.
    [Test]
    public async Task DownloadAsync_EmptyBody_WritesEmptyFileAndReportsZero()
    {
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Ok([]));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(EmptySha1), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new FileInfo(_destination).Length, Is.Zero);
            Assert.That(File.Exists(_partial), Is.False);
            Assert.That(progress.Bytes, Is.Not.Empty);
            Assert.That(progress.Bytes[^1], Is.Zero);
        }
    }

    // A 0-byte destination with the empty-file hash counts as done, so nothing is sent.
    [Test]
    public async Task DownloadAsync_ExistingEmptyDestinationWithEmptyHash_SendsNoRequest()
    {
        await File.WriteAllBytesAsync(_destination, []);
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(EmptySha1), null, CancellationToken.None);

        Assert.That(_handler.Requests, Is.Empty);
    }

    // A 416 that finishes the file from the partial still reports the final total.
    [Test]
    public async Task DownloadAsync_Status416WithCompletePartial_ReportsFinalTotal()
    {
        await File.WriteAllBytesAsync(_partial, _abc);
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Status(HttpStatusCode.RequestedRangeNotSatisfiable));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.Bytes, Is.Not.Empty);
            Assert.That(progress.Bytes[^1], Is.EqualTo(3));
        }
    }

    // A restart after a 200 counts from 0, so the stale partial length never shows up as progress.
    [Test]
    public async Task DownloadAsync_PartialLongerThanBodyAndServer200_ProgressNeverCountsStalePrefix()
    {
        await File.WriteAllBytesAsync(_partial, "abcdefgh"u8.ToArray());
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.Bytes, Has.All.LessThanOrEqualTo(3));
            Assert.That(progress.Bytes[^1], Is.EqualTo(3));
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
        }
    }

    // A path with spaces and non-ASCII chars is downloaded and moved like any other.
    [Test]
    public async Task DownloadAsync_UnicodePathWithSpaces_WritesFileAndLeavesNoPartial()
    {
        string destination = Path.Combine(_folder.Root, "Ünïcode 名前", "Fïle ß.bin");
        var downloader = NewDownloader(_ => Ok(_abc));

        await downloader.DownloadAsync(new Uri(Url), destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(_abc));
            Assert.That(Directory.GetFileSystemEntries(Path.GetDirectoryName(destination)!), Has.Length.EqualTo(1));
        }
    }

    // A resumed file whose prefix does not match the pin is deleted whole, new tail included.
    [Test]
    public async Task DownloadAsync_ResumeWithWrongPrefix_DeletesPartialAndThrowsInvalidDataException()
    {
        await File.WriteAllBytesAsync(_partial, "xy"u8.ToArray());
        var pin = PinnedHash.Sha1(AbcSha1);
        var downloader = NewDownloader(_ => Range206(_abc, 2));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, pin, null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[0].Range, Is.EqualTo("bytes=2-"));
            Assert.That(exception!.Message, Does.Contain(AbcSha1).And.Contain(XycSha1Hex).And.Contain("run again"));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A wrong hash leaves an older destination as it was instead of deleting it.
    [Test]
    public async Task DownloadAsync_WrongHashWithOldDestination_KeepsOldDestinationUnchanged()
    {
        byte[] old = "old"u8.ToArray();
        await File.WriteAllBytesAsync(_destination, old);
        var downloader = NewDownloader(_ => Ok("abd"u8.ToArray()));

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(old));
            Assert.That(File.Exists(_partial), Is.False);
        }
    }

    // A drop during a resumed tail keeps the old prefix plus the new bytes for the next resume.
    [Test]
    public async Task DownloadAsync_ResumedBodyFailsMidStream_KeepsPrefixAndNewBytes()
    {
        byte[] prefix = new byte[500];
        await File.WriteAllBytesAsync(_partial, prefix);
        var downloader = NewDownloader(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.PartialContent) { Content = new StreamContent(new ThrowingStream(700, () => { })) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(500, 4999, 5000);

            return response;
        });

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new FileInfo(_partial).Length, Is.EqualTo(1200));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A request that fails before any response leaves the partial untouched.
    [Test]
    public async Task DownloadAsync_SendFails_KeepsPartialUnchanged()
    {
        byte[] prefix = "ab"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, prefix);
        var downloader = NewDownloader(_ => throw new HttpRequestException("The test connection is down."));

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(_partial), Is.EqualTo(prefix));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A 404 to a resume keeps the partial, because the next run may reach a working mirror.
    [Test]
    public async Task DownloadAsync_PartialPresentAndServer404_ThrowsHttpRequestExceptionAndKeepsPartial()
    {
        byte[] prefix = "ab"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, prefix);
        var downloader = NewDownloader(_ => Status(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await File.ReadAllBytesAsync(_partial), Is.EqualTo(prefix));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A 206 without a Content-Range cannot be placed in the file, so it is refused and the partial is kept.
    [Test]
    public async Task DownloadAsync_Status206WithoutContentRange_ThrowsHttpRequestExceptionAndKeepsPartial()
    {
        byte[] prefix = "ab"u8.ToArray();
        await File.WriteAllBytesAsync(_partial, prefix);
        var downloader = NewDownloader(_ => new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent("c"u8.ToArray()),
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(exception.Message, Does.Contain(Url).And.Contain("(none)").And.Contain(_partial));
            Assert.That(await File.ReadAllBytesAsync(_partial), Is.EqualTo(prefix));
            Assert.That(File.Exists(_destination), Is.False);
        }
    }

    // A 206 whose Content-Range has no start is refused with the header in the message.
    [Test]
    public async Task DownloadAsync_Status206WithUnsatisfiedContentRange_MessageQuotesHeader()
    {
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        var downloader = NewDownloader(_ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("c"u8.ToArray()) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(5000);

            return response;
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("'bytes */5000'").And.Contain("byte 2"));
    }

    // A 206 to a plain GET is an unexpected status, so the message never points at a partial that does not exist.
    [Test]
    public async Task DownloadAsync_NoPartialAndServer206_ThrowsUnexpectedStatusAndWritesNothing()
    {
        var downloader = NewDownloader(_ => Range206(_abc, 0));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[0].Range, Is.Null);
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(exception.Message, Does.Contain("206").And.Not.Contain(_partial));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // The partial is hashed and locked before the request, so the server never idles while a large prefix is read from disk.
    [Test]
    [Platform("Win")]
    public async Task DownloadAsync_PartialPresent_LocksPartialBeforeRequest()
    {
        await File.WriteAllBytesAsync(_partial, "ab"u8.ToArray());
        bool lockedAtRequest = false;
        var downloader = NewDownloader(_ =>
        {
            try
            {
                using var reader = File.OpenRead(_partial);
            }
            catch (IOException)
            {
                lockedAtRequest = true;
            }

            return Range206(_abc, 2);
        });

        await downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lockedAtRequest, Is.True);
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(_abc));
        }
    }

    // A token canceled up front stops before any request and creates no partial.
    [Test]
    public async Task DownloadAsync_CanceledBeforeStart_ThrowsOperationCanceledAndWritesNothing()
    {
        var downloader = NewDownloader(_ => Ok(_abc));

        await Assert.ThatAsync(
            () => downloader.DownloadAsync(new Uri(Url), _destination, PinnedHash.Sha1(AbcSha1), null, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A resume over many copy buffers hashes the prefix and the tail in order.
    [Test]
    public async Task DownloadAsync_LargeResume_HashesPrefixAndTailInOrder()
    {
        byte[] body = new byte[300_007];

        for (int i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(((i * 31) + (i >> 8)) & 0xFF);
        }

        await File.WriteAllBytesAsync(_partial, body.AsSpan(0, 170_003).ToArray());
        RecordingProgress progress = new();
        var downloader = NewDownloader(_ => Range206(body, 170_003));
        using var hash = IncrementalHash.CreateHash(PinnedHash.Algorithm);
        hash.AppendData(body);

        await downloader.DownloadAsync(
            new Uri(Url),
            _destination,
            PinnedHash.Sha1(Convert.ToHexStringLower(hash.GetHashAndReset())),
            progress,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(_destination), Is.EqualTo(body));
            Assert.That(progress.Bytes, Is.Ordered);
            Assert.That(progress.Bytes[^1], Is.EqualTo(300_007));
        }
    }

    private Downloader NewDownloader(Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _handler = new FakeHttpHandler(respond);
        _downloader = new Downloader(_handler);

        return _downloader;
    }
}
