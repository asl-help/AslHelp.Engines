using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Records every request a <see cref="DavStore"/> sends and returns a response without a network.
/// </summary>
/// <param name="respond">Builds the response to a recorded request.</param>
internal sealed class FakeDavHandler(
    Func<FakeDavHandler.RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    /// <summary>
    ///     Gets the requests in the order they were sent.
    /// </summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>
    ///     Creates an empty response with <paramref name="code"/>.
    /// </summary>
    /// <param name="code">The status code to return.</param>
    /// <returns>
    ///     The response.
    /// </returns>
    public static HttpResponseMessage Status(HttpStatusCode code)
    {
        return new HttpResponseMessage(code);
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Read the body whole, because the tests compare it to the source bytes.
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        RecordedRequest recorded = new(
            request.Method.Method,
            request.RequestUri!,
            request.Headers.Authorization,
            request.Content?.Headers.ContentLength,
            request.Headers.ExpectContinue,
            body);

        Requests.Add(recorded);

        return respond(recorded);
    }

    /// <summary>
    ///     Describes 1 request the handler received.
    /// </summary>
    /// <param name="Method">The HTTP method, such as <c>PUT</c>.</param>
    /// <param name="Uri">The full request URI.</param>
    /// <param name="Authorization">The <c>Authorization</c> header or <see langword="null"/> when it is missing.</param>
    /// <param name="ContentLength">The <c>Content-Length</c> header or <see langword="null"/> when it is missing.</param>
    /// <param name="ExpectContinue">
    ///     Whether the request sends <c>Expect: 100-continue</c> or <see langword="null"/> when the header is not set.
    /// </param>
    /// <param name="Body">The request body, empty when there is none.</param>
    internal sealed record RecordedRequest(
        string Method,
        Uri Uri,
        AuthenticationHeaderValue? Authorization,
        long? ContentLength,
        bool? ExpectContinue,
        byte[] Body);
}

/// <summary>
///     Returns a few pattern bytes and then fails, like a connection that drops mid-download.
/// </summary>
/// <param name="length">The number of bytes to return before the failure.</param>
/// <param name="beforeFailure">Runs right before the read that fails.</param>
internal sealed class ThrowingStream(
    int length,
    Action beforeFailure) : Stream
{
    private int _position;

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
        if (_position >= length)
        {
            beforeFailure();
            throw new IOException("The test stream fails after its bytes.");
        }

        int read = Math.Min(count, length - _position);

        for (int i = 0; i < read; i++)
        {
            buffer[offset + i] = (byte)(_position++ & 0xFF);
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
