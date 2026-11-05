using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Passes each request and the token it was sent with to a callback, so a test can stall before the headers or read the
///     request body like a server.
/// </summary>
/// <param name="send">Returns the response to a request, given the token of the request.</param>
internal sealed class CallbackHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return send(request, cancellationToken);
    }
}
