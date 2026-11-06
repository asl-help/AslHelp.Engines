using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Reads and writes cached results on a WebDAV folder with GET, PUT and MKCOL from RFC 4918.
/// </summary>
internal sealed class DavStore : ICacheStore
{
    /// <summary>
    ///     Represents the environment variable for the WebDAV app password.
    /// </summary>
    public const string TokenVariable = "FIXTURES_DAV_TOKEN";

    /// <summary>
    ///     Represents the environment variable for the URL of the WebDAV cache folder.
    /// </summary>
    public const string UrlVariable = "FIXTURES_DAV_URL";

    /// <summary>
    ///     Represents the environment variable for the WebDAV user.
    /// </summary>
    public const string UserVariable = "FIXTURES_DAV_USER";

    /// <summary>
    ///     Sizes the copy buffer at 80 KB, the default of <see cref="Stream.CopyToAsync(Stream)"/>.
    /// </summary>
    private const int CopyBufferSize = 81920;

    private static readonly HttpMethod _mkcol = new("MKCOL");

    private readonly Uri _baseUri;
    private readonly HttpClient _client;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DavStore"/> class.
    /// </summary>
    /// <param name="baseUri">
    ///     The absolute URL of the WebDAV folder with no query or fragment. A missing trailing '/' is added, so paths go inside it.
    /// </param>
    /// <param name="user">The WebDAV user.</param>
    /// <param name="token">The WebDAV app password. It is sent in the Basic auth header of every request and never printed.</param>
    /// <param name="handler">
    ///     The handler that sends the requests or <see langword="null"/> for the one from <see cref="CreateHandler"/>.
    ///     The store disposes it.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="baseUri"/> is relative or has a query or fragment, <paramref name="user"/> is empty or <paramref name="token"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="baseUri"/>, <paramref name="user"/> or <paramref name="token"/> is <see langword="null"/>.
    /// </exception>
    public DavStore(Uri baseUri, string user, string token, HttpMessageHandler? handler = null)
        : this(baseUri, user, token, handler, TimeProvider.System)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DavStore"/> class that measures the time limits with <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="baseUri">
    ///     The absolute URL of the WebDAV folder with no query or fragment. A missing trailing '/' is added, so paths go inside it.
    /// </param>
    /// <param name="user">The WebDAV user.</param>
    /// <param name="token">The WebDAV app password. It is sent in the Basic auth header of every request and never printed.</param>
    /// <param name="handler">
    ///     The handler that sends the requests or <see langword="null"/> for the one from <see cref="CreateHandler"/>.
    ///     The store disposes it.
    /// </param>
    /// <param name="timeProvider">The clock that measures the limits of <see cref="TransferTimer"/>.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="baseUri"/> is relative or has a query or fragment, <paramref name="user"/> is empty or <paramref name="token"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="baseUri"/>, <paramref name="user"/> or <paramref name="token"/> is <see langword="null"/>.
    /// </exception>
    internal DavStore(Uri baseUri, string user, string token, HttpMessageHandler? handler, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrEmpty(user);
        ArgumentException.ThrowIfNullOrEmpty(token);

        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException(SR.FormatBaseUrlRelative(baseUri), nameof(baseUri));
        }

        // Refuse a query or fragment, because the '/' added below would go after it and not after the path.
        if (baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
        {
            throw new ArgumentException(SR.FormatBaseUrlHasQuery(baseUri), nameof(baseUri));
        }

        string text = baseUri.AbsoluteUri;
        _baseUri = text[^1] == '/' ? baseUri : new Uri(text + "/");

        _client = new HttpClient(handler ?? CreateHandler(), disposeHandler: true)
        {
            // Turn the client timeout off, because it covers a whole PUT body and the transfer timer limits each wait instead.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{token}"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates the handler the store uses when the caller passes none.
    /// </summary>
    /// <remarks>
    ///     Automatic redirects are off, because a followed redirect drops the Authorization header
    ///     and the server then returns 401, which reads as wrong credentials.
    ///     A 3xx fails with its status instead and a message that points at the URL variable.
    /// </remarks>
    /// <returns>
    ///     The handler from <see cref="HttpHandlers.Create"/>. The caller disposes it.
    /// </returns>
    internal static SocketsHttpHandler CreateHandler()
    {
        return HttpHandlers.Create(followRedirects: false);
    }

    /// <summary>
    ///     Creates a store from the variables <see cref="UrlVariable"/>, <see cref="UserVariable"/> and <see cref="TokenVariable"/>.
    /// </summary>
    /// <returns>
    ///     The store. The caller disposes it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     A variable is missing, a variable is empty or <see cref="UrlVariable"/> is not an absolute http or https URL
    ///     with no query or fragment.
    /// </exception>
    public static DavStore FromEnvironment()
    {
        return FromEnvironment(null);
    }

    /// <summary>
    ///     Creates a store from the variables <see cref="UrlVariable"/>, <see cref="UserVariable"/> and <see cref="TokenVariable"/>
    ///     that sends its requests through <paramref name="handler"/>.
    /// </summary>
    /// <param name="handler">The handler that sends the requests or <see langword="null"/> for <see cref="CreateHandler"/>.</param>
    /// <returns>
    ///     The store. The caller disposes it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     A variable is missing, a variable is empty or <see cref="UrlVariable"/> is not an absolute http or https URL
    ///     with no query or fragment.
    /// </exception>
    internal static DavStore FromEnvironment(HttpMessageHandler? handler)
    {
        string? url = Environment.GetEnvironmentVariable(UrlVariable);
        string? user = Environment.GetEnvironmentVariable(UserVariable);
        string? token = Environment.GetEnvironmentVariable(TokenVariable);

        List<string> missing = [];

        if (string.IsNullOrEmpty(url))
        {
            missing.Add(UrlVariable);
        }

        if (string.IsNullOrEmpty(user))
        {
            missing.Add(UserVariable);
        }

        if (string.IsNullOrEmpty(token))
        {
            missing.Add(TokenVariable);
        }

        // Leave the values out of the message, because they are secret.
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(SR.FormatDavVariablesMissing(string.Join(", ", missing)));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
            || baseUri.Query.Length > 0
            || baseUri.Fragment.Length > 0)
        {
            throw new InvalidOperationException(SR.FormatDavUrlInvalid(UrlVariable));
        }

        // Refuse plain http to another machine, because the Basic header puts the app password into every request.
        if (baseUri.Scheme == Uri.UriSchemeHttp && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException(SR.FormatDavUrlNotHttps(UrlVariable));
        }

        return new DavStore(baseUri, user!, token!, handler);
    }

    /// <summary>
    ///     Creates a store from the variables <see cref="UrlVariable"/>, <see cref="UserVariable"/> and <see cref="TokenVariable"/>
    ///     if any of them is set. With none of them set, the method writes the line that the build stays local to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The writer for the line, which lists the 3 variables.</param>
    /// <returns>
    ///     <see langword="null"/> when all 3 variables are missing or empty, otherwise the store from <see cref="FromEnvironment()"/>.
    ///     The caller disposes it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Only some of the variables are set or <see cref="UrlVariable"/> is not an absolute http or https URL
    ///     with no query or fragment.
    /// </exception>
    public static DavStore? FromEnvironmentIfSet(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UrlVariable))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(UserVariable))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(TokenVariable)))
        {
            output.WriteLine(SR.FormatLocalBuild(UrlVariable, UserVariable, TokenVariable));
            return null;
        }

        return FromEnvironment();
    }

    /// <summary>
    ///     Downloads <paramref name="path"/> to <paramref name="destinationFile"/> if it exists.
    /// </summary>
    /// <remarks>
    ///     The body is streamed to a sibling <c>.partial</c> file first and then moved over <paramref name="destinationFile"/>,
    ///     so a failed download never leaves a half file under the real name.
    /// </remarks>
    /// <param name="path">The relative, '/' separated path under the base URL.</param>
    /// <param name="destinationFile">The file to write. An existing file is replaced and a missing folder is created.</param>
    /// <param name="progress">
    ///     The receiver of the bytes on disk and the Content-Length after each chunk or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="cancellationToken">The token that stops the download.</param>
    /// <returns>
    ///     <see langword="true"/> when the file was downloaded, <see langword="false"/> on status 404.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="destinationFile"/> is empty or <paramref name="path"/> is empty, absolute or has an empty, '.' or '..' segment.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="destinationFile"/> is <see langword="null"/>.</exception>
    /// <exception cref="HttpRequestException">The server rejected the credentials or returned another status.</exception>
    /// <exception cref="IOException">The connection dropped, the file cannot be written or another download has the partial file open.</exception>
    /// <exception cref="TimeoutException">
    ///     Connecting took longer than <see cref="HttpHandlers.ConnectLimit"/>, the response headers took longer than
    ///     <see cref="TransferTimer.StallLimit"/> or a body read moved no bytes for <see cref="TransferTimer.StallLimit"/>.
    ///     The partial file is deleted.
    /// </exception>
    public async Task<bool> TryDownloadAsync(
        string path,
        string destinationFile,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        string[] segments = SplitPath(path);
        ArgumentException.ThrowIfNullOrEmpty(destinationFile);

        using TransferTimer timer = new(_timeProvider, cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Get, ResolveUri(segments, segments.Length));

        // Read only the headers, so the body is streamed to disk and not buffered in memory.
        using var response = await SendAsync(request, path, HttpCompletionOption.ResponseHeadersRead, timer).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw UnexpectedStatus(request, path, response);
        }

        string partialFile = Path.GetFullPath(destinationFile + TempPaths.PartialSuffix);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(partialFile)!);

        // Open the partial file before the try, so a file another download has open is never deleted here.
        FileStream file = new(
            partialFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            await using (file.ConfigureAwait(false))
            {
                await WriteBodyAsync(response, file, path, progress, timer, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialFile, destinationFile, overwrite: true);
        }
        catch
        {
            TempPaths.DeleteFileQuietly(partialFile);
            throw;
        }

        return true;
    }

    /// <summary>
    ///     Uploads <paramref name="sourceFile"/> to <paramref name="path"/> and creates its parent collections first.
    /// </summary>
    /// <param name="sourceFile">The file to upload. It is streamed, not read into memory.</param>
    /// <param name="path">The relative, '/' separated path under the base URL. An existing file is replaced.</param>
    /// <param name="progress">
    ///     The receiver of the bytes sent and the file length after each chunk or <see langword="null"/> for no reports.
    ///     A PUT that the handler sends again on a new connection reports from 0 again.
    /// </param>
    /// <param name="cancellationToken">The token that stops the upload.</param>
    /// <returns>
    ///     A task that completes once the server stored the file.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, absolute or has an empty, '.' or '..' segment.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="sourceFile"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="sourceFile"/> does not exist.</exception>
    /// <exception cref="HttpRequestException">The server rejected the credentials or returned another status to a MKCOL or the PUT.</exception>
    /// <exception cref="TimeoutException">
    ///     Connecting took longer than <see cref="HttpHandlers.ConnectLimit"/>, the response headers to a MKCOL took longer than
    ///     <see cref="TransferTimer.StallLimit"/>, a body write moved no bytes for <see cref="TransferTimer.StallLimit"/>
    ///     or the response headers after the last byte took longer than <see cref="TransferTimer.StoreLimit"/>.
    /// </exception>
    public async Task UploadAsync(string sourceFile, string path, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        string[] segments = SplitPath(path);

        using TransferTimer timer = new(_timeProvider, cancellationToken);

        // Open the source before the 1st request, so a missing file sends nothing.
        FileContent content = new(sourceFile, progress, timer);

        using HttpRequestMessage put = new(HttpMethod.Put, ResolveUri(segments, segments.Length)) { Content = content };

        // Wait for 100 Continue, so a server that refuses the PUT responds before tens of GB are sent.
        put.Headers.ExpectContinue = true;

        // Create each parent from the top down, because MKCOL fails when the parent is missing.
        for (int count = 1; count < segments.Length; count++)
        {
            string collection = string.Join('/', segments, 0, count);

            using HttpRequestMessage mkcol = new(_mkcol, ResolveUri(segments, count));
            using var response = await SendAsync(mkcol, collection, HttpCompletionOption.ResponseContentRead, timer).ConfigureAwait(false);

            // Accept 405, because it means the collection exists already.
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed))
            {
                throw UnexpectedStatus(mkcol, collection, response);
            }
        }

        using var putResponse = await SendAsync(put, path, HttpCompletionOption.ResponseContentRead, timer).ConfigureAwait(false);

        // Accept 200 and 204 next to 201, because RFC 9110 allows either for a replaced file.
        if (putResponse.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.NoContent))
        {
            throw UnexpectedStatus(put, path, putResponse);
        }
    }

    /// <summary>
    ///     Disposes the <see cref="HttpClient"/> and the handler it uses.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    ///     Splits <paramref name="path"/> into its segments and checks each one.
    /// </summary>
    /// <param name="path">The relative, '/' separated path.</param>
    /// <returns>
    ///     The segments, at least 1.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, absolute or has an empty, '.' or '..' segment.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    private static string[] SplitPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] segments = path.Split('/');

        foreach (string segment in segments)
        {
            // Refuse empty and dot segments, because they could point outside the base URL.
            if (segment is "" or "." or "..")
            {
                throw new ArgumentException(SR.FormatStorePathInvalid(path), nameof(path));
            }
        }

        return segments;
    }

    /// <summary>
    ///     Builds the URL of the first <paramref name="count"/> segments under the base URL.
    /// </summary>
    /// <param name="segments">The checked path segments.</param>
    /// <param name="count">The number of segments to use.</param>
    /// <returns>
    ///     The absolute URL.
    /// </returns>
    private Uri ResolveUri(string[] segments, int count)
    {
        // Escape each segment on its own, so '/' stays a separator and every other character is encoded.
        StringBuilder relative = new();

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                _ = relative.Append('/');
            }

            _ = relative.Append(Uri.EscapeDataString(segments[i]));
        }

        return new Uri(_baseUri, relative.ToString());
    }

    /// <summary>
    ///     Sends <paramref name="request"/>, turns status 401 and 403 into an exception and turns a passed time limit into a
    ///     <see cref="TimeoutException"/>.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="path">The path for the message.</param>
    /// <param name="completion">When the call returns, after the headers or after the whole body.</param>
    /// <param name="timer">
    ///     The timer of the call, which sends the request with the token of the caller linked in. A cancel of the caller stays an
    ///     <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    ///     The response. The caller disposes it.
    /// </returns>
    /// <exception cref="HttpRequestException">The server rejected the credentials.</exception>
    /// <exception cref="TimeoutException">Connecting, a body write or the wait for the response took longer than its limit.</exception>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string path, HttpCompletionOption completion, TransferTimer timer)
    {
        HttpResponseMessage response;

        try
        {
            response = await timer.SendAsync(_client, request, completion).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timer.IsTimeout(exception))
        {
            throw timer.TimedOut(exception, request.Method, path, SR.DavServerFix);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var status = response.StatusCode;
            response.Dispose();

            throw new HttpRequestException(
                SR.FormatDavCredentialsRejected(request.Method, path, UrlVariable, UserVariable, TokenVariable),
                null,
                status);
        }

        return response;
    }

    /// <summary>
    ///     Writes the body of <paramref name="response"/> to <paramref name="file"/> in chunks.
    /// </summary>
    /// <param name="response">The response with the body to write.</param>
    /// <param name="file">The partial file.</param>
    /// <param name="path">The path of the request, for the message.</param>
    /// <param name="progress">
    ///     The receiver of the bytes on disk and the Content-Length after each chunk or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="timer">The timer of the request, which limits each read to <see cref="TransferTimer.StallLimit"/>.</param>
    /// <param name="cancellationToken">The token that stops the copy.</param>
    /// <returns>
    ///     A task that completes once the whole body is in <paramref name="file"/>.
    /// </returns>
    /// <exception cref="IOException">The connection dropped or the file cannot be written.</exception>
    /// <exception cref="TimeoutException">A read moved no bytes for <see cref="TransferTimer.StallLimit"/>.</exception>
    private static async Task WriteBodyAsync(
        HttpResponseMessage response,
        FileStream file,
        string path,
        IProgress<TransferProgress>? progress,
        TransferTimer timer,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[CopyBufferSize];
        long? length = response.Content.Headers.ContentLength;
        long total = 0;

        progress?.Report(new TransferProgress(total, length));

        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (body.ConfigureAwait(false))
        {
            try
            {
                int read;

                while ((read = await timer.ReadAsync(body, buffer).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                    total += read;
                    progress?.Report(new TransferProgress(total, length));
                }
            }
            catch (OperationCanceledException exception) when (timer.IsTimeout(exception))
            {
                throw timer.TimedOut(exception, HttpMethod.Get, path, SR.DavServerFix);
            }
        }
    }

    /// <summary>
    ///     Creates the exception for a status the call does not accept, with the fix that fits the status.
    /// </summary>
    /// <remarks>
    ///     A redirect and any 4xx other than 413 point at the URL variable, because the server is up and the URL is the part
    ///     to change. A 409 to MKCOL means the folder above is missing, which for the 1st collection is the folder of the URL
    ///     variable. 413 and 507 mean the file is too large for the upload limit or for the free space of the server. Any
    ///     other status points at the server.
    /// </remarks>
    /// <param name="request">The request.</param>
    /// <param name="path">The path of the request.</param>
    /// <param name="response">The response with the status the call does not accept.</param>
    /// <returns>
    ///     The exception to throw.
    /// </returns>
    private static HttpRequestException UnexpectedStatus(HttpRequestMessage request, string path, HttpResponseMessage response)
    {
        var status = response.StatusCode;

        string fix = (int)status switch
        {
            >= 300 and < 400 when response.Headers.Location is { } location
                => SR.FormatDavRedirectToFix(UrlVariable, new Uri(request.RequestUri!, location)),
            >= 300 and < 400 => SR.FormatDavRedirectFix(UrlVariable),
            409 when request.Method == _mkcol => SR.FormatDavFolderMissingFix(UrlVariable),
            413 => SR.DavTooLargeFix,
            507 => SR.DavNoSpaceFix,
            >= 400 and < 500 => SR.FormatDavRequestRefusedFix(UrlVariable),
            _ => SR.DavServerFix,
        };

        return new HttpRequestException(SR.FormatDavStatusFailed(request.Method, path, (int)status, status, fix), null, status);
    }

    /// <summary>
    ///     Sends a file as the body of a request in chunks and limits each write to the stall limit of the timer.
    /// </summary>
    /// <param name="sourceFile">The file to send. It is opened at once and closed when the content is disposed.</param>
    /// <param name="progress">The receiver of the bytes sent and the file length after each chunk or <see langword="null"/> for no reports.</param>
    /// <param name="timer">The timer that sends the request with <see cref="TransferTimer.SendAsync"/>.</param>
    private sealed class FileContent(
        string sourceFile,
        IProgress<TransferProgress>? progress,
        TransferTimer timer) : HttpContent
    {
        private readonly FileStream _file = new(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        /// <inheritdoc/>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        /// <inheritdoc/>
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            // Start at the 1st byte, because the handler sends the content again when it retries the request on a new connection.
            _file.Position = 0;

            byte[] buffer = new byte[CopyBufferSize];
            long length = _file.Length;
            long total = 0;
            int read;

            progress?.Report(new TransferProgress(total, length));

            while ((read = await _file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await timer.WriteAsync(stream, buffer.AsMemory(0, read)).ConfigureAwait(false);

                total += read;
                progress?.Report(new TransferProgress(total, length));
            }

            // Give the server the store limit for its response, because it stores the whole file before it sends the response headers.
            timer.StartResponseWait(TransferTimer.StoreLimit);
        }

        /// <inheritdoc/>
        protected override bool TryComputeLength(out long length)
        {
            length = _file.Length;

            return true;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
