using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Records every request a <see cref="Downloader"/> sends and returns a response without a network.
/// </summary>
/// <param name="respond">Builds the response to a recorded request.</param>
internal sealed class FakeHttpHandler(
    Func<FakeHttpHandler.RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
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

    /// <summary>
    ///     Creates a 200 response with <paramref name="body"/>.
    /// </summary>
    /// <param name="body">The whole file.</param>
    /// <returns>
    ///     The response.
    /// </returns>
    public static HttpResponseMessage Ok(byte[] body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    /// <summary>
    ///     Creates a 206 response with the bytes of <paramref name="all"/> from <paramref name="from"/> and a matching <c>Content-Range</c>.
    /// </summary>
    /// <param name="all">The whole file.</param>
    /// <param name="from">The index of the first byte to send.</param>
    /// <returns>
    ///     The response.
    /// </returns>
    public static HttpResponseMessage Range206(byte[] all, int from)
    {
        HttpResponseMessage response = new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(all, from, all.Length - from) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, all.Length - 1, all.Length);

        return response;
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RecordedRequest recorded = new(
            request.Method.Method,
            request.RequestUri!,
            request.Headers.Range?.ToString());

        Requests.Add(recorded);

        return Task.FromResult(respond(recorded));
    }

    /// <summary>
    ///     Describes 1 request the handler received.
    /// </summary>
    /// <param name="Method">The HTTP method, such as <c>GET</c>.</param>
    /// <param name="Uri">The full request URI.</param>
    /// <param name="Range">The <c>Range</c> header (such as <c>bytes=5-</c>) or <see langword="null"/> when it is missing.</param>
    internal sealed record RecordedRequest(
        string Method,
        Uri Uri,
        string? Range);
}
