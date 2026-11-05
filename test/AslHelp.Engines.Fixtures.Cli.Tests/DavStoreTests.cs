using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AslHelp.Engines.Fixtures.Cli.Tests.FakeDavHandler;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DavStoreTests
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

    // The request is a GET with Basic auth of user:token.
    [Test]
    public async Task TryDownloadAsync_AnyPath_SendsGetWithBasicAuth()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        _ = await store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        var request = _handler.Requests[0];
        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Token}"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Uri.AbsoluteUri, Is.EqualTo(Base + "a/f.tar.zst"));
            Assert.That(request.Authorization?.Scheme, Is.EqualTo("Basic"));
            Assert.That(request.Authorization?.Parameter, Is.EqualTo(expected));
        }
    }

    // An unexpected status throws with the method, the path and the status code.
    [Test]
    public async Task TryDownloadAsync_Status500_ThrowsWithMethodPathAndStatus()
    {
        var store = NewStore(_ => Status(HttpStatusCode.InternalServerError));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("GET").And.Contain("'a/f.tar.zst'").And.Contain("500"));
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        }
    }

    // A redirect is not followed, so its message points at the URL variable instead of at the server.
    [Test]
    public async Task TryDownloadAsync_Status301_ThrowsWithTheUrlVariable()
    {
        var store = NewStore(_ => Status(HttpStatusCode.MovedPermanently));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("301").And.Contain(DavStore.UrlVariable).And.Not.Contain("is up"));
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.MovedPermanently));
        }
    }

    // 403 gets the same message on any call and nothing is sent after it.
    [Test]
    public async Task UploadAsync_Status403OnMkcol_ThrowsWithTheVariablesAndNoToken()
    {
        string source = WriteSource([1, 2, 3]);
        var store = NewStore(_ => Status(HttpStatusCode.Forbidden));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.UploadAsync(source, "a/b/f.tar.zst", null, CancellationToken.None));

        AssertCredentialsMessage(exception!);
        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
    }

    // Parent collections are made from the top down before the file is sent whole with its length.
    [Test]
    public async Task UploadAsync_NestedPath_SendsMkcolTopDownThenPutWithBytesAndContentLength()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        string source = WriteSource(bytes);
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "a/b/f.tar.zst", null, CancellationToken.None);

        var requests = _handler.Requests;

        Assert.That(requests, Has.Count.EqualTo(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests[0].Method, Is.EqualTo("MKCOL"));
            Assert.That(requests[0].Uri.AbsoluteUri, Is.EqualTo(Base + "a"));
            Assert.That(requests[1].Method, Is.EqualTo("MKCOL"));
            Assert.That(requests[1].Uri.AbsoluteUri, Is.EqualTo(Base + "a/b"));
            Assert.That(requests[2].Method, Is.EqualTo("PUT"));
            Assert.That(requests[2].Uri.AbsoluteUri, Is.EqualTo(Base + "a/b/f.tar.zst"));
            Assert.That(requests[2].Body, Is.EqualTo(bytes));
            Assert.That(requests[2].ContentLength, Is.EqualTo(bytes.Length));
        }
    }

    // 405 on MKCOL means the collection exists, so the upload goes on to PUT.
    [Test]
    public async Task UploadAsync_Mkcol405_ContinuesToPut()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "MKCOL" ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.Created));

        await store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None);

        string[] expected = ["MKCOL", "PUT"];

        Assert.That(_handler.Requests.ConvertAll(request => request.Method), Is.EqualTo(expected));
    }

    // A failed MKCOL throws with the collection path and no PUT follows.
    [Test]
    public async Task UploadAsync_Mkcol500_ThrowsBeforePut()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "MKCOL" ? HttpStatusCode.InternalServerError : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.UploadAsync(source, "a/b/f.tar.zst", null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("MKCOL").And.Contain("'a'").And.Contain("500"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        }
    }

    // 200 and 204 on PUT mean the file was replaced, which RFC 9110 lists as success next to 201 for a new file.
    [TestCase(HttpStatusCode.OK)]
    [TestCase(HttpStatusCode.NoContent)]
    public async Task UploadAsync_PutReplaced_Succeeds(HttpStatusCode status)
    {
        string source = WriteSource([1]);
        var store = NewStore(_ => Status(status));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
    }

    // A failed PUT throws with the method, the path and the status code.
    [Test]
    public async Task UploadAsync_Put500_ThrowsHttpRequestException()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "PUT" ? HttpStatusCode.InternalServerError : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("PUT").And.Contain("'a/f.tar.zst'").And.Contain("500"));
    }

    // A redirect message gives the URL the server redirects to, so the user can see the host to set.
    [Test]
    public async Task TryDownloadAsync_Status301WithLocation_ThrowsWithTheLocation()
    {
        var store = NewStore(_ =>
        {
            var response = Status(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://other.example/dav/a/f.tar.zst");

            return response;
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("https://other.example/dav/a/f.tar.zst").And.Contain(DavStore.UrlVariable));
    }

    // MKCOL returns 409 when the folder above is missing, which for the 1st collection is the folder of the URL variable.
    [Test]
    public async Task UploadAsync_MkcolConflict_ThrowsWithTheUrlVariable()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "MKCOL" ? HttpStatusCode.Conflict : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("409").And.Contain(DavStore.UrlVariable).And.Contain("does not exist"));
    }

    // A PUT the server refuses for its size or for lack of space gets a fix for that and not the hint that the server is down.
    [TestCase(HttpStatusCode.RequestEntityTooLarge, "upload limit")]
    [TestCase(HttpStatusCode.InsufficientStorage, "no space")]
    public async Task UploadAsync_PutRefusedForSize_ThrowsWithTheMatchingFix(HttpStatusCode status, string fix)
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "PUT" ? status : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain(fix).And.Not.Contain("is up"));
    }

    // Another 4xx means the request does not fit the folder, so the message points at the URL variable and not at the server.
    [Test]
    public async Task UploadAsync_Put400_ThrowsWithTheUrlVariable()
    {
        string source = WriteSource([1]);
        var store = NewStore(request => Status(request.Method == "PUT" ? HttpStatusCode.BadRequest : HttpStatusCode.Created));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain(DavStore.UrlVariable).And.Not.Contain("is up"));
    }

    // A path with 1 segment needs no collection, so the only request is the PUT.
    [Test]
    public async Task UploadAsync_TopLevelPath_SendsNoMkcol()
    {
        string source = WriteSource([1]);
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "f.tar.zst", null, CancellationToken.None);

        string[] expected = ["PUT"];

        Assert.That(_handler.Requests.ConvertAll(request => request.Method), Is.EqualTo(expected));
    }

    // The source is opened before the 1st request, so a missing file sends nothing.
    [Test]
    public async Task UploadAsync_MissingSourceFile_ThrowsFileNotFoundExceptionAndSendsNothing()
    {
        string source = Path.Combine(_folder.Root, "missing.tar.zst");
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await Assert.ThatAsync(
            () => store.UploadAsync(source, "a/f.tar.zst", null, CancellationToken.None),
            Throws.TypeOf<FileNotFoundException>());

        Assert.That(_handler.Requests, Is.Empty);
    }

    // The body is written to the destination and no partial file is left.
    [Test]
    public async Task TryDownloadAsync_Status200_WritesFileBytesAndReturnsTrue()
    {
        byte[] bytes = [1, 2, 3, 4];
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        bool found = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(bytes));
            Assert.That(File.Exists(destination + ".partial"), Is.False);
            Assert.That(_handler.Requests[0].Method, Is.EqualTo("GET"));
        }
    }

    // The partial file exists while the body is still read, so the body is streamed and not buffered in memory first.
    [Test]
    public async Task TryDownloadAsync_Status200_StreamsBodyToDiskWhileReading()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        bool partialWhileReading = false;
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingStream(4, () => partialWhileReading = File.Exists(destination + ".partial"))),
        });

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        Assert.That(partialWhileReading, Is.True);
    }

    // 404 returns false and leaves neither the destination nor a partial file.
    [Test]
    public async Task TryDownloadAsync_Status404_ReturnsFalseAndLeavesNoFile()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        bool found = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // An unexpected status throws before any file is opened.
    [Test]
    public async Task TryDownloadAsync_Status500_ThrowsAndLeavesNoFile()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => Status(HttpStatusCode.InternalServerError));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("GET").And.Contain("'a/f.tar.zst'").And.Contain("500"));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
        }
    }

    // A body that fails after its 1st bytes deletes the partial file and leaves no destination.
    [Test]
    public async Task TryDownloadAsync_BodyFailsMidStream_ThrowsAndLeavesNoPartial()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingStream(100_000, () => { })),
        });

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None),
            Throws.InstanceOf<IOException>());

        Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
    }

    // The complete partial file moves over an existing destination.
    [Test]
    public async Task TryDownloadAsync_ExistingDestination_ReplacesIt()
    {
        byte[] bytes = [1, 2, 3];
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        File.WriteAllBytes(destination, [9, 9, 9, 9, 9]);
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        _ = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        string[] expected = [destination];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(bytes));
            Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.EqualTo(expected));
        }
    }

    // The destination's missing parent folders are created, the same way Archive.Pack creates them.
    [Test]
    public async Task TryDownloadAsync_MissingDestinationFolder_CreatesIt()
    {
        byte[] bytes = [1, 2, 3];
        string destination = Path.Combine(_folder.Root, "x", "y", "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        _ = await store.TryDownloadAsync("a/f.tar.zst", destination, null, CancellationToken.None);

        Assert.That(await File.ReadAllBytesAsync(destination), Is.EqualTo(bytes));
    }

    // A canceled token stops the call and no file is left.
    [Test]
    public async Task TryDownloadAsync_CanceledToken_ThrowsOperationCanceledAndLeavesNoFile()
    {
        string destination = Path.Combine(_folder.Root, "f.tar.zst");
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThatAsync(
            () => store.TryDownloadAsync("a/f.tar.zst", destination, null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(Directory.GetFileSystemEntries(_folder.Root), Is.Empty);
    }

    // Each segment is escaped on its own, so spaces and non-ASCII letters are encoded and '/' stays a separator.
    [Test]
    public async Task TryDownloadAsync_SpacesAndUnicodePath_EscapesEachSegment()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound));

        _ = await store.TryDownloadAsync("ä b/ü c.tar.zst", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(Base + "%C3%A4%20b/%C3%BC%20c.tar.zst"));
    }

    // A base URL without a trailing '/' keeps its last segment, so paths go inside it and not next to it.
    [Test]
    public async Task Constructor_BaseUrlWithoutTrailingSlash_KeepsLastSegment()
    {
        var store = NewStore(_ => Status(HttpStatusCode.NotFound), "https://h/dav/files/u/fixtures");

        _ = await store.TryDownloadAsync("a", Path.Combine(_folder.Root, "f.bin"), null, CancellationToken.None);

        Assert.That(_handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo("https://h/dav/files/u/fixtures/a"));
    }

    // A download reports the bytes on disk after each chunk with the Content-Length, so a bar shows the share done.
    [Test]
    public async Task TryDownloadAsync_Status200_ReportsBytesWithTheContentLength()
    {
        byte[] body = [1, 2, 3, 4, 5];
        RecordingProgress progress = new();
        var store = NewStore(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

        _ = await store.TryDownloadAsync("a/f.tar.zst", Path.Combine(_folder.Root, "f.bin"), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.Values[0], Is.EqualTo(new TransferProgress(0, 5)));
            Assert.That(progress.Values[^1], Is.EqualTo(new TransferProgress(5, 5)));
        }
    }

    // An upload reports the bytes sent after each chunk with the file length, so a bar shows the share done.
    [Test]
    public async Task UploadAsync_File_ReportsBytesWithTheFileLength()
    {
        string source = WriteSource([1, 2, 3, 4, 5]);
        RecordingProgress progress = new();
        var store = NewStore(_ => Status(HttpStatusCode.Created));

        await store.UploadAsync(source, "f.tar.zst", progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(progress.Values[0], Is.EqualTo(new TransferProgress(0, 5)));
            Assert.That(progress.Values[^1], Is.EqualTo(new TransferProgress(5, 5)));
        }
    }

    // A relative base URI cannot anchor requests, so the constructor refuses it.
    [Test]
    public void Constructor_RelativeBaseUri_ThrowsArgumentException()
    {
        using FakeDavHandler handler = new(_ => Status(HttpStatusCode.MultiStatus));

        Assert.That(
            () => new DavStore(new Uri("dav/fixtures", UriKind.Relative), User, Token, handler),
            Throws.TypeOf<ArgumentException>().With.Property(nameof(ArgumentException.ParamName)).EqualTo("baseUri"));
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

    private static void AssertCredentialsMessage(HttpRequestException exception)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("rejected the credentials"));
            Assert.That(exception.Message, Does.Contain("FIXTURES_DAV_URL"));
            Assert.That(exception.Message, Does.Contain("FIXTURES_DAV_USER"));
            Assert.That(exception.Message, Does.Contain("FIXTURES_DAV_TOKEN"));
            Assert.That(exception.ToString(), Does.Not.Contain(Token));
        }
    }
}
