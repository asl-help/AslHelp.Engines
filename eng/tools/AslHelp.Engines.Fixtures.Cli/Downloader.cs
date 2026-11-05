using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Downloads a file over HTTP, resumes a partial download with a <c>Range</c> request and checks the result against a pinned hash.
/// </summary>
internal sealed class Downloader : IDisposable
{
    /// <summary>
    ///     Sizes the copy buffer at 80 KB, the default of <see cref="Stream.CopyToAsync(Stream)"/>.
    /// </summary>
    private const int CopyBufferSize = 81920;

    private readonly HttpClient _client;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Downloader"/> class.
    /// </summary>
    /// <param name="handler">
    ///     The handler that sends the requests or <see langword="null"/> for the one from <see cref="CreateHandler"/>.
    ///     The downloader disposes it.
    /// </param>
    public Downloader(HttpMessageHandler? handler = null)
        : this(handler, TimeProvider.System)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="Downloader"/> class that measures the time limits with <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="handler">
    ///     The handler that sends the requests or <see langword="null"/> for the one from <see cref="CreateHandler"/>.
    ///     The downloader disposes it.
    /// </param>
    /// <param name="timeProvider">The clock that measures the limits of <see cref="TransferTimer"/>.</param>
    internal Downloader(HttpMessageHandler? handler, TimeProvider timeProvider)
    {
        _client = new HttpClient(handler ?? CreateHandler(), disposeHandler: true)
        {
            // Turn the client timeout off, because with ResponseHeadersRead it covers only the headers, which the transfer timer limits already.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates the handler the downloader uses when the caller passes none.
    /// </summary>
    /// <remarks>
    ///     Redirects are followed, because no Authorization header is sent and file hosts often redirect to a mirror.
    /// </remarks>
    /// <returns>
    ///     The handler from <see cref="HttpHandlers.Create"/>. The caller disposes it.
    /// </returns>
    internal static SocketsHttpHandler CreateHandler()
    {
        return HttpHandlers.Create(followRedirects: true);
    }

    /// <summary>
    ///     Downloads <paramref name="url"/> to <paramref name="destinationFile"/> unless the file is there with the pinned hash.
    /// </summary>
    /// <remarks>
    ///     The body is streamed to a sibling <c>.partial</c> file and hashed while it is written.
    ///     A partial file from an earlier run is resumed with <c>Range: bytes=N-</c> or moved without a request when it has the
    ///     pinned hash already.
    ///     The partial file is moved over <paramref name="destinationFile"/> only when its hash matches <paramref name="hash"/>.
    /// </remarks>
    /// <param name="url">The absolute URL of the file.</param>
    /// <param name="destinationFile">The file to write. A missing folder is created.</param>
    /// <param name="hash">The hash the file must have.</param>
    /// <param name="progress">
    ///     The receiver of the bytes on disk (a resumed prefix included) and the length of the file after each chunk
    ///     or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="cancellationToken">The token that stops the download. The partial file is kept for the next run.</param>
    /// <returns>
    ///     A task that completes once <paramref name="destinationFile"/> has the pinned hash.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="url"/> is relative or <paramref name="destinationFile"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="url"/>, <paramref name="destinationFile"/> or <paramref name="hash"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="HttpRequestException">
    ///     The server returned a status other than 200, a 206 that starts at the partial length or a 416 to a resume.
    /// </exception>
    /// <exception cref="IOException">The connection dropped or the file cannot be written. The partial file is kept for the next run.</exception>
    /// <exception cref="InvalidDataException">The file does not have the pinned hash. The partial file is deleted.</exception>
    /// <exception cref="TimeoutException">
    ///     Connecting took longer than <see cref="HttpHandlers.ConnectLimit"/>, the response headers took longer than
    ///     <see cref="TransferTimer.StallLimit"/> or a body read moved no bytes for <see cref="TransferTimer.StallLimit"/>.
    ///     The partial file is kept for the next run.
    /// </exception>
    public async Task DownloadAsync(
        Uri url,
        string destinationFile,
        PinnedHash hash,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrEmpty(destinationFile);
        ArgumentNullException.ThrowIfNull(hash);

        if (!url.IsAbsoluteUri)
        {
            throw new ArgumentException(SR.Format(SR.DownloadUrlRelative, url), nameof(url));
        }

        byte[] buffer = new byte[CopyBufferSize];

        if (File.Exists(destinationFile)
            && await HashFileAsync(destinationFile, PinnedHash.Algorithm, buffer, cancellationToken).ConfigureAwait(false) == hash.Hex)
        {
            return;
        }

        string partialFile = Path.GetFullPath(destinationFile + TempPaths.PartialSuffix);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(partialFile)!);

        using var hasher = IncrementalHash.CreateHash(PinnedHash.Algorithm);
        string actualHex;

        if (File.Exists(partialFile))
        {
            var partial = OpenPartial(partialFile, FileMode.Open);

            await using (partial.ConfigureAwait(false))
            {
                actualHex = await ReceiveAsync(url, partialFile, partial, hash, hasher, progress, buffer, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            actualHex = await ReceiveAsync(url, partialFile, null, hash, hasher, progress, buffer, cancellationToken).ConfigureAwait(false);
        }

        Complete(partialFile, destinationFile, url, hash, actualHex);
    }

    /// <summary>
    ///     Disposes the <see cref="HttpClient"/> and the handler it uses.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    ///     Sends the GET for <paramref name="url"/> and writes the body of the response to the partial file.
    /// </summary>
    /// <param name="url">The absolute URL of the file.</param>
    /// <param name="partialFile">The full path of the partial file.</param>
    /// <param name="partial">The open partial file from an earlier run or <see langword="null"/> when there is none.</param>
    /// <param name="hash">The pinned hash, which the partial file may have already.</param>
    /// <param name="hasher">The empty hash of the pinned algorithm.</param>
    /// <param name="progress">
    ///     The receiver of the bytes on disk and the length of the file after each chunk or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="buffer">The buffer to copy through.</param>
    /// <param name="cancellationToken">The token that stops the download.</param>
    /// <returns>
    ///     The hash of the whole partial file as lowercase hex chars.
    /// </returns>
    /// <exception cref="HttpRequestException">The server returned a status the download does not accept.</exception>
    /// <exception cref="TimeoutException">
    ///     Connecting, the response headers or a body read took longer than its limit. The partial file is kept for the next run.
    /// </exception>
    private async Task<string> ReceiveAsync(
        Uri url,
        string partialFile,
        FileStream? partial,
        PinnedHash hash,
        IncrementalHash hasher,
        IProgress<TransferProgress>? progress,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        long offset = 0;

        if (partial is not null)
        {
            // Hash the partial before the request, because a server can close a connection that idles while GBs are read from disk.
            offset = await HashToEndAsync(partial, hasher, buffer, cancellationToken).ConfigureAwait(false);

            // Finish a partial that has the pinned hash already, because a run can stop after its last write and before the move.
            string partialHex = Convert.ToHexStringLower(hasher.GetCurrentHash());

            if (string.Equals(partialHex, hash.Hex, StringComparison.Ordinal))
            {
                progress?.Report(new TransferProgress(offset, offset));

                return partialHex;
            }
        }

        using HttpRequestMessage request = new(HttpMethod.Get, url);

        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
        }

        using TransferTimer timer = new(_timeProvider, cancellationToken);

        try
        {
            // Read only the headers, so the body is streamed to disk and not buffered in memory.
            using var response = await timer.SendAsync(_client, request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK when partial is null:
                {
                    var created = OpenPartial(partialFile, FileMode.Create);

                    await using (created.ConfigureAwait(false))
                    {
                        return await WriteBodyAsync(response, created, 0, hasher, progress, buffer, timer, cancellationToken).ConfigureAwait(false);
                    }
                }
                case HttpStatusCode.OK:
                {
                    // Drop the partial and its hash, because the server sends the whole file again.
                    _ = hasher.GetHashAndReset();
                    partial.SetLength(0);

                    return await WriteBodyAsync(response, partial, 0, hasher, progress, buffer, timer, cancellationToken).ConfigureAwait(false);
                }
                case HttpStatusCode.PartialContent when offset > 0 && IsRestOfFile(response.Content.Headers.ContentRange, offset):
                {
                    return await WriteBodyAsync(response, partial!, offset, hasher, progress, buffer, timer, cancellationToken).ConfigureAwait(false);
                }
                case HttpStatusCode.PartialContent when offset > 0:
                {
                    string range = response.Content.Headers.ContentRange?.ToString() ?? SR.NoContentRange;

                    throw new HttpRequestException(SR.Format(SR.DownloadBadRange, url, range, offset, partialFile), null, response.StatusCode);
                }
                case HttpStatusCode.RequestedRangeNotSatisfiable when offset > 0:
                {
                    // Verify the partial file as complete, because a server returns 416 for a range that starts at the end of the file.
                    progress?.Report(new TransferProgress(offset, offset));

                    return Convert.ToHexStringLower(hasher.GetHashAndReset());
                }
                default:
                {
                    throw UnexpectedStatus(url, response.StatusCode);
                }
            }
        }
        catch (OperationCanceledException exception) when (timer.IsTimeout(exception))
        {
            throw timer.TimedOut(exception, HttpMethod.Get, url.ToString(), RetryFix(partialFile));
        }
    }

    /// <summary>
    ///     Creates the step that fixes a download that a time limit stopped.
    /// </summary>
    /// <param name="partialFile">The full path of the partial file.</param>
    /// <returns>
    ///     The step, with the number of bytes the next run resumes from when the partial file has any.
    /// </returns>
    private static string RetryFix(string partialFile)
    {
        FileInfo partial = new(partialFile);

        return partial.Exists && partial.Length > 0 ? SR.Format(SR.DownloadResumeFix, partial.Length) : SR.DownloadRetryFix;
    }

    /// <summary>
    ///     Checks whether <paramref name="range"/> covers the file from <paramref name="offset"/> to its end.
    /// </summary>
    /// <remarks>
    ///     A range that ends early is refused, because a server can cap the length of a range and the bytes it leaves out
    ///     would read as a wrong hash. A range of unknown total length counts as the rest, because the hash checks it.
    /// </remarks>
    /// <param name="range">The Content-Range of the response or <see langword="null"/> when it has none.</param>
    /// <param name="offset">The length of the partial file.</param>
    /// <returns>
    ///     <see langword="true"/> if the range starts at <paramref name="offset"/> and ends at the last byte of the file.
    /// </returns>
    private static bool IsRestOfFile(ContentRangeHeaderValue? range, long offset)
    {
        return range is { From: { } from, To: { } to }
            && from == offset
            && (range.Length is not { } length || to + 1 == length);
    }

    /// <summary>
    ///     Opens the partial file at <paramref name="path"/> for reading and writing, locked for other processes.
    /// </summary>
    /// <param name="path">The partial file.</param>
    /// <param name="mode"><see cref="FileMode.Open"/> to resume the file or <see cref="FileMode.Create"/> to start it.</param>
    /// <returns>
    ///     The open file, positioned at its start.
    /// </returns>
    private static FileStream OpenPartial(string path, FileMode mode)
    {
        return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>
    ///     Hashes the file at <paramref name="path"/> in chunks.
    /// </summary>
    /// <param name="path">The file to hash.</param>
    /// <param name="algorithm">The hash algorithm.</param>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="cancellationToken">The token that stops the read.</param>
    /// <returns>
    ///     The hash as lowercase hex chars.
    /// </returns>
    private static async Task<string> HashFileAsync(string path, HashAlgorithmName algorithm, byte[] buffer, CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(algorithm);
        FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using (file.ConfigureAwait(false))
        {
            _ = await HashToEndAsync(file, hasher, buffer, cancellationToken).ConfigureAwait(false);
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    /// <summary>
    ///     Hashes <paramref name="file"/> from its position to its end in chunks.
    /// </summary>
    /// <param name="file">The file to read.</param>
    /// <param name="hasher">The hash to add the bytes to.</param>
    /// <param name="buffer">The buffer to read into.</param>
    /// <param name="cancellationToken">The token that stops the read.</param>
    /// <returns>
    ///     The number of bytes hashed.
    /// </returns>
    private static async Task<long> HashToEndAsync(FileStream file, IncrementalHash hasher, byte[] buffer, CancellationToken cancellationToken)
    {
        long total = 0;
        int read;

        while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            total += read;
        }

        return total;
    }

    /// <summary>
    ///     Appends the body of <paramref name="response"/> to <paramref name="file"/> and adds it to <paramref name="hasher"/>.
    /// </summary>
    /// <param name="response">The response with the body to write.</param>
    /// <param name="file">The partial file, positioned at its end.</param>
    /// <param name="total">The number of bytes already in <paramref name="file"/> and <paramref name="hasher"/>, 0 for a fresh file.</param>
    /// <param name="hasher">The hash of the whole file so far.</param>
    /// <param name="progress">
    ///     The receiver of the bytes on disk and the length of the file after each chunk or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="buffer">The buffer to copy through.</param>
    /// <param name="timer">The timer of the request, which limits each read to <see cref="TransferTimer.StallLimit"/>.</param>
    /// <param name="cancellationToken">The token that stops the copy.</param>
    /// <returns>
    ///     The hash of the whole file as lowercase hex chars.
    /// </returns>
    private static async Task<string> WriteBodyAsync(
        HttpResponseMessage response,
        FileStream file,
        long total,
        IncrementalHash hasher,
        IProgress<TransferProgress>? progress,
        byte[] buffer,
        TransferTimer timer,
        CancellationToken cancellationToken)
    {
        // Take the length of the whole file from Content-Range on a resume, because Content-Length then counts only the rest.
        long? length = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength + total);

        // Report the start total, so an empty body still gives the final total.
        progress?.Report(new TransferProgress(total, length));

        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (body.ConfigureAwait(false))
        {
            int read;

            while ((read = await timer.ReadAsync(body, buffer).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hasher.AppendData(buffer, 0, read);

                total += read;
                progress?.Report(new TransferProgress(total, length));
            }
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    /// <summary>
    ///     Moves <paramref name="partialFile"/> over <paramref name="destinationFile"/> when <paramref name="actualHex"/> matches the pin.
    /// </summary>
    /// <param name="partialFile">The finished partial file.</param>
    /// <param name="destinationFile">The file to replace.</param>
    /// <param name="url">The URL of the file, for the message.</param>
    /// <param name="hash">The pinned hash.</param>
    /// <param name="actualHex">The hash of <paramref name="partialFile"/> as lowercase hex chars.</param>
    /// <exception cref="InvalidDataException">The hashes differ. The partial file is deleted, so the next run starts fresh.</exception>
    private static void Complete(string partialFile, string destinationFile, Uri url, PinnedHash hash, string actualHex)
    {
        if (!string.Equals(actualHex, hash.Hex, StringComparison.Ordinal))
        {
            TempPaths.DeleteFileQuietly(partialFile);

            throw new InvalidDataException(SR.Format(SR.DownloadHashMismatch, url, PinnedHash.Algorithm, actualHex, hash.Hex));
        }

        File.Move(partialFile, destinationFile, overwrite: true);
    }

    /// <summary>
    ///     Creates the exception for a status the download does not accept.
    /// </summary>
    /// <param name="url">The URL of the request.</param>
    /// <param name="status">The status the server returned.</param>
    /// <returns>
    ///     The exception to throw.
    /// </returns>
    private static HttpRequestException UnexpectedStatus(Uri url, HttpStatusCode status)
    {
        return new HttpRequestException(SR.Format(SR.DownloadFailed, url, (int)status, status), null, status);
    }
}
