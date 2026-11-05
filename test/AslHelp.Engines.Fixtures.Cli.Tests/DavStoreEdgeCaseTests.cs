using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeDavHandler;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DavStoreEdgeCaseTests
{
    private const string Base = "https://dav.example/remote.php/dav/files/u/fixtures/";
    private const string Token = "app-secret-123";
    private const string User = "u";

    private TestFolder _folder = null!;
    private FakeDavHandler _handler = null!;
    private DavStore? _store;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _store?.Dispose();
        _folder.Delete();
    }

    // An empty destination is refused before the GET, so no body of tens of GB is fetched for nothing.
    [Test]
    public async Task TryDownloadAsync_EmptyDestination_ThrowsArgumentExceptionAndSendsNothing()
    {
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", "", null, CancellationToken.None),
            Throws.TypeOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("destinationFile"));

        Assert.That(_handler.Requests, Is.Empty);
    }

    // A body of many copy buffers plus an odd tail is written whole and in order.
    [Test]
    public async Task TryDownloadAsync_LargeBody_WritesEveryByteInOrder()
    {
        const int BodyLength = (8 * 1024 * 1024) + 12_345;
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new PatternStream(BodyLength, () => { })),
        });

        bool found = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(FirstPatternMismatch(destination), Is.EqualTo(-1));
            Assert.That(new FileInfo(destination).Length, Is.EqualTo(BodyLength));
        }
    }

    // A 200 with an empty body still creates the destination as a 0-byte file.
    [Test]
    public async Task TryDownloadAsync_EmptyBody_WritesEmptyFile()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) });

        bool found = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        string[] expected = [destination];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(new FileInfo(destination).Length, Is.Zero);
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.EqualTo(expected));
        }
    }

    // A longer partial file left by a crashed run is truncated, so its old tail never ends up in the destination.
    [Test]
    public async Task TryDownloadAsync_StalePartialFile_ReplacesItNotAppends()
    {
        byte[] bytes = [1, 2, 3];
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        await File.WriteAllBytesAsync(destination + ".partial", [9, 9, 9, 9, 9, 9, 9, 9]);
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        _ = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(bytes));
            Assert.That(File.Exists(destination + ".partial"), Is.False);
        }
    }

    // A failed download keeps the old destination as it was, because only a complete partial file is moved over it.
    [Test]
    public async Task TryDownloadAsync_BodyFailsWithExistingDestination_KeepsOldBytes()
    {
        byte[] old = [7, 7, 7];
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        await File.WriteAllBytesAsync(destination, old);
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ThrowingStream(1000, () => { })) });

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        string[] expected = [destination];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(old));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.EqualTo(expected));
        }
    }

    // A token canceled after the headers stops the copy and deletes the partial file.
    [Test]
    public async Task TryDownloadAsync_CanceledMidBody_ThrowsOperationCanceledAndLeavesNoFile()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        using CancellationTokenSource cancellation = new();
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new PatternStream(4 * 1024 * 1024, cancellation.Cancel)),
        });

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
    }

    // A rejected login on GET lists the 3 variables to check and leaves no file.
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task TryDownloadAsync_CredentialsRejected_ThrowsWithTheVariablesAndLeavesNoFile(HttpStatusCode status)
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => Status(status));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None));

        AssertCredentialsMessage(exception!, status);
        Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
    }

    // A 2xx other than 200, such as 206 for a range, is not a whole file, so it throws and leaves no file.
    [TestCase(HttpStatusCode.NoContent)]
    [TestCase(HttpStatusCode.PartialContent)]
    public async Task TryDownloadAsync_Other2xx_ThrowsAndLeavesNoFile(HttpStatusCode status)
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(status) { Content = new ByteArrayContent([1, 2]) });

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None),
            Throws.TypeOf<HttpRequestException>().With.Property(nameof(HttpRequestException.StatusCode)).EqualTo(status));

        Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
    }

    // A destination another process has open makes the move fail, so the partial file is deleted and the old file stays.
    [Test]
    [Platform("Win")]
    public async Task TryDownloadAsync_DestinationLocked_ThrowsAndDeletesPartial()
    {
        byte[] old = [7, 7, 7];
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        await File.WriteAllBytesAsync(destination, old);
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });

        using (new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThatAsync(
                () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None),
                Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        }

        string[] expected = [destination];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(old));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.EqualTo(expected));
        }
    }

    // A rejected login on PUT lists the 3 variables to check and never shows the token.
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task UploadAsync_CredentialsRejectedOnPut_ThrowsWithTheVariables(HttpStatusCode status)
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(_ => Status(status));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None));

        AssertCredentialsMessage(exception!, status);
    }

    // An empty source is sent as a PUT with Content-Length 0 and no body.
    [Test]
    public async Task UploadAsync_EmptyFile_SendsPutWithContentLengthZero()
    {
        string source = WriteSource([]);
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        var put = _handler.Requests[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(put.Method, Is.EqualTo("PUT"));
            Assert.That(put.ContentLength, Is.Zero);
            Assert.That(put.Body, Is.Empty);
        }
    }

    // A body of many copy buffers is sent whole and in order with its exact length.
    [Test]
    public async Task UploadAsync_LargeFile_SendsEveryByteWithContentLength()
    {
        const int BodyLength = (4 * 1024 * 1024) + 777;
        string source = Path.Combine(_folder.Root, "source.tar.zst");

        await using (var file = File.Create(source))
        {
            await new PatternStream(BodyLength, () => { }).CopyToAsync(file);
        }

        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        var put = _handler.Requests[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(put.ContentLength, Is.EqualTo(BodyLength));
            Assert.That(put.Body, Is.EqualTo(await File.ReadAllBytesAsync(source)));
        }
    }

    // A deep path gets 1 MKCOL per parent from the top down before the PUT.
    [Test]
    public async Task UploadAsync_FourLevelPath_SendsMkcolForEveryParentInOrder()
    {
        string source = WriteSource([1]);
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "a/b/c/d/f.tar.zst", null, CancellationToken.None);

        string[] expected =
        [
            "MKCOL " + Base + "a",
            "MKCOL " + Base + "a/b",
            "MKCOL " + Base + "a/b/c",
            "MKCOL " + Base + "a/b/c/d",
            "PUT " + Base + "a/b/c/d/f.tar.zst",
        ];

        Assert.That(_handler.Requests.ConvertAll(request => request.Method + " " + request.Uri.AbsoluteUri), Is.EqualTo(expected));
    }

    // A MKCOL that fails in the middle stops the upload there, so no deeper MKCOL and no PUT follow.
    [Test]
    public async Task UploadAsync_SecondMkcolFails_StopsBeforeDeeperRequests()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Uri.AbsoluteUri == Base + "a/b" ? HttpStatusCode.Conflict : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.UploadAsync(source, "a/b/c/f.tar.zst", null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("MKCOL").And.Contain("'a/b'").And.Contain("409"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
        }
    }

    // MKCOL and PUT escape each segment the same way, while the error message shows the plain path.
    [Test]
    public async Task UploadAsync_UnicodeAndSpacePath_EscapesMkcolAndPutSegments()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "PUT" ? HttpStatusCode.BadGateway : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.UploadAsync(source, "ä b/ü c.tar.zst", null, CancellationToken.None));

        string[] expected =
        [
            Base + "%C3%A4%20b",
            Base + "%C3%A4%20b/%C3%BC%20c.tar.zst",
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests.ConvertAll(request => request.Uri.AbsoluteUri), Is.EqualTo(expected));
            Assert.That(exception!.Message, Does.Contain("'ä b/ü c.tar.zst'"));
        }
    }

    // The source file is closed after a failed upload, so the caller can delete or rewrite it.
    [Test]
    [Platform("Win")]
    public async Task UploadAsync_MkcolFails_ReleasesSourceFile()
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(_ => Status(HttpStatusCode.InternalServerError));

        _ = await Assert.ThrowsAsync<HttpRequestException>(() => store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None));

        Assert.That(() => new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose(), Throws.Nothing);
    }

    // The source file is closed after a successful upload too.
    [Test]
    [Platform("Win")]
    public async Task UploadAsync_PutCreated_ReleasesSourceFile()
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None);

        Assert.That(() => new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose(), Throws.Nothing);
    }

    // A 207 to a PUT is not a status the store accepts, so it throws.
    [Test]
    public async Task UploadAsync_PutMultiStatus_ThrowsWithStatus()
    {
        string source = WriteSource([1]);
        var store = NewStore(_ => Status(HttpStatusCode.MultiStatus));

        await Assert.ThatAsync(
            () => store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None),
            Throws.TypeOf<HttpRequestException>().With.Property(nameof(HttpRequestException.StatusCode)).EqualTo(HttpStatusCode.MultiStatus));
    }

    // A bad path is refused before the source is opened, so a missing source still gives ArgumentException.
    [TestCase("")]
    [TestCase("/a")]
    [TestCase("a/b/")]
    [TestCase("a/./b")]
    [TestCase("..")]
    [TestCase("a/b/..")]
    public async Task UploadAsync_BadPath_ThrowsArgumentExceptionAndSendsNothing(string path)
    {
        string source = Path.Combine(_folder.Root, "missing.tar.zst");
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await Assert.ThatAsync(
            () => store.UploadAsync(source, path, null, CancellationToken.None),
            Throws.TypeOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("path"));

        Assert.That(_handler.Requests, Is.Empty);
    }

    // A bad path is refused before the GET and leaves no file.
    [TestCase("")]
    [TestCase("/a")]
    [TestCase("a//b")]
    [TestCase("a/../b")]
    public async Task TryDownloadAsync_BadPath_ThrowsArgumentExceptionAndSendsNothing(string path)
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => Status(HttpStatusCode.OK));

        await Assert.ThatAsync(
            () => store.TryDownloadAsync(path, destination, null, CancellationToken.None),
            Throws.TypeOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("path"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // Characters with a meaning in a URL are escaped, so a segment can never become a scheme, query, fragment or parent step.
    [TestCase("c:/x", "c%3A/x")]
    [TestCase("a?b=1", "a%3Fb%3D1")]
    [TestCase("a#b", "a%23b")]
    [TestCase("100%", "100%25")]
    [TestCase("%2e%2e/x", "%252e%252e/x")]
    [TestCase(@"a\..\b", "a%5C..%5Cb")]
    [TestCase("a+b&c", "a%2Bb%26c")]
    public async Task TryDownloadAsync_ReservedCharactersInSegment_KeepsRequestInsideBase(string path, string expected)
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        _ = await store.TryDownloadAsync(path, Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(Base + expected));
    }

    // A segment with dots inside it or of 3 dots is a plain file name and not a parent step, so it is allowed.
    [TestCase(".hidden")]
    [TestCase("...")]
    [TestCase("a/..b/c..")]
    public async Task TryDownloadAsync_DotsInsideSegment_SendsRequest(string path)
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        _ = await store.TryDownloadAsync(path, Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(Base + path));
    }

    // A non-BMP letter is escaped as its 4 UTF-8 bytes.
    [Test]
    public async Task TryDownloadAsync_NonBmpCharacter_EscapesUtf8Bytes()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        _ = await store.TryDownloadAsync("\U0001F600.tar.zst", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(Base + "%F0%9F%98%80.tar.zst"));
    }

    // A base URL with an escaped segment and no trailing '/' keeps the segment escaped.
    [Test]
    public async Task Constructor_BaseUrlWithEscapedSegment_KeepsItEscaped()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound), "https://h/my%20dav");

        _ = await store.TryDownloadAsync("a", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo("https://h/my%20dav/a"));
    }

    // A host-only base URL puts paths at the root.
    [Test]
    public async Task Constructor_HostOnlyBaseUrl_PutsPathsAtRoot()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound), "https://h");

        _ = await store.TryDownloadAsync("a/b", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo("https://h/a/b"));
    }

    // Each call sends its own Basic auth header, so the 2nd call is signed like the 1st.
    [Test]
    public async Task TryDownloadAsync_TwoCalls_BothSendBasicAuth()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        _ = await store.TryDownloadAsync("a", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);
        _ = await store.TryDownloadAsync("b", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[0].Authorization?.Scheme, Is.EqualTo("Basic"));
            Assert.That(_handler.Requests[1].Authorization?.Parameter, Is.EqualTo(_handler.Requests[0].Authorization?.Parameter));
        }
    }

    // The default handler does not follow redirects, because a followed redirect drops Basic auth and the 401 after it blames the credentials.
    [Test]
    public void CreateHandler_Always_TurnsOffRedirects()
    {
        using var handler = DavStore.CreateHandler();

        Assert.That(handler.AllowAutoRedirect, Is.False);
    }

    // A base URL with a query or fragment would get its '/' after them, so the constructor refuses it.
    [TestCase("?x=1")]
    [TestCase("#top")]
    public void Constructor_BaseUrlWithQueryOrFragment_ThrowsArgumentException(string query)
    {
        using FakeDavHandler handler = new(_ => Status(HttpStatusCode.MultiStatus));

        Assert.That(
            () => new DavStore(new Uri("https://h/dav/fixtures" + query), User, Token, handler),
            Throws.TypeOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("baseUri"));
    }

    // The PUT sends 'Expect: 100-continue', so a server that refuses it responds before the body is sent.
    [Test]
    public async Task UploadAsync_AnyPath_SendsPutWithExpectContinue()
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests[0].ExpectContinue, Is.Not.True);
            Assert.That(_handler.Requests[1].ExpectContinue, Is.True);
        }
    }

    // A partial file another download has open makes the call fail and stays, so that download can still finish.
    [Test]
    public async Task TryDownloadAsync_PartialFileOpenElsewhere_ThrowsAndKeepsIt()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        string partial = destination + ".partial";
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        using FileStream other = new(partial, FileMode.Create, FileAccess.Write, FileShare.None);
        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        Assert.That(File.Exists(partial), Is.True);
    }

    private DavStore NewStore(Func<RecordedRequest, HttpResponseMessage> respond, string baseUrl = Base)
    {
        _handler = new FakeDavHandler(respond);
        _store = new DavStore(new Uri(baseUrl), User, Token, _handler);

        return _store;
    }

    private string WriteSource(byte[] bytes)
    {
        string source = Path.Combine(_folder.Root, "source.tar.zst");
        File.WriteAllBytes(source, bytes);

        return source;
    }

    private static long FirstPatternMismatch(string file)
    {
        using var stream = File.OpenRead(file);
        byte[] buffer = new byte[81920];
        long position = 0;
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++, position++)
            {
                if (buffer[i] != (byte)(position & 0xFF))
                {
                    return position;
                }
            }
        }

        return -1;
    }

    private static void AssertCredentialsMessage(HttpRequestException exception, HttpStatusCode status)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(exception.Message, Does.Contain("rejected the credentials"));
            Assert.That(exception.Message, Does.Contain(DavStore.UrlVariable));
            Assert.That(exception.Message, Does.Contain(DavStore.UserVariable));
            Assert.That(exception.Message, Does.Contain(DavStore.TokenVariable));
            Assert.That(exception.ToString(), Does.Not.Contain(Token));
        }
    }
}

/// <summary>
///     Returns <c>position &amp; 0xFF</c> for each byte up to a fixed length, like a large file body.
/// </summary>
/// <param name="length">The number of bytes to return.</param>
/// <param name="afterFirstRead">Runs once after the 1st read.</param>
internal sealed class PatternStream(
    long length,
    Action afterFirstRead) : Stream
{
    private long _position;
    private bool _readOnce;

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = (int)Math.Min(count, length - _position);

        for (int i = 0; i < read; i++)
        {
            buffer[offset + i] = (byte)(_position++ & 0xFF);
        }

        if (!_readOnce)
        {
            _readOnce = true;
            afterFirstRead();
        }

        return read;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
