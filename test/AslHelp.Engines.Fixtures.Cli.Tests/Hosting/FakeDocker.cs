using Docker.DotNet;
using Docker.DotNet.Handler.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Hosting;

/// <summary>
///     Creates Docker clients that send every request to a handler in place of a Docker engine.
/// </summary>
internal static class FakeDocker
{
    /// <summary>
    ///     Represents the ID of the container that <see cref="Engine"/> creates.
    /// </summary>
    private const string ContainerId = "c0ffee";

    /// <summary>
    ///     Creates a client that sends every request to <paramref name="handler"/>.
    /// </summary>
    /// <param name="handler">The handler that answers in place of the engine.</param>
    /// <returns>
    ///     The client.
    /// </returns>
    public static DockerClient NewClient(HttpMessageHandler handler)
    {
        return new DockerClientBuilder<HttpMessageHandler>(new FakeHandlerFactory(), handler)
            .WithApiVersion(new Version(1, 44))
            .Build();
    }

    /// <summary>
    ///     Creates a handler that creates <see cref="ContainerId"/> and answers its start, its wait and its removal with the
    ///     given responses.
    /// </summary>
    /// <param name="start">The response to the start.</param>
    /// <param name="wait">The response to the wait, which can stay pending like the wait on a running container.</param>
    /// <param name="remove">The response to the removal.</param>
    /// <returns>
    ///     The handler.
    /// </returns>
    public static CallbackHandler Engine(
        Func<HttpResponseMessage> start,
        Func<Task<HttpResponseMessage>> wait,
        Func<HttpResponseMessage> remove)
    {
        return new CallbackHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith($"/containers/{ContainerId}/wait", StringComparison.Ordinal))
            {
                return wait();
            }

            var response = path switch
            {
                _ when path.EndsWith("/containers/create", StringComparison.Ordinal)
                    => Json(HttpStatusCode.Created, $$"""{"Id":"{{ContainerId}}"}"""),
                _ when path.EndsWith($"/containers/{ContainerId}/start", StringComparison.Ordinal) => start(),
                _ when request.Method == HttpMethod.Delete => remove(),
                _ => Json(HttpStatusCode.NotFound, """{"message":"unexpected request"}"""),
            };

            return Task.FromResult(response);
        });
    }

    /// <summary>
    ///     Creates a response with a JSON body.
    /// </summary>
    /// <param name="code">The status code.</param>
    /// <param name="body">The JSON body.</param>
    /// <returns>
    ///     The response.
    /// </returns>
    public static HttpResponseMessage Json(HttpStatusCode code, string body)
    {
        return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>
///     Hands the handler of the transport options to the client, so the client sends no request to a real engine.
/// </summary>
file sealed class FakeHandlerFactory : IDockerHandlerFactory<HttpMessageHandler>
{
    /// <inheritdoc/>
    public ResolvedTransport CreateHandler(HttpMessageHandler transportOptions, ResolvedClientOptions clientOptions, ILogger logger)
    {
        return new ResolvedTransport(transportOptions, new Uri("http://localhost"));
    }

    /// <inheritdoc/>
    public ResolvedTransport CreateHandler(ResolvedClientOptions clientOptions, ILogger logger)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public Task<WriteClosableStream> HijackStreamAsync(HttpContent content)
    {
        throw new NotSupportedException();
    }
}
