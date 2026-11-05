using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Provides the HTTP handler that file transfers send their requests through.
/// </summary>
internal static class HttpHandlers
{
    /// <summary>
    ///     Limits connecting, the TCP and TLS handshakes, to 30 seconds, because a reachable server finishes both within a few seconds.
    /// </summary>
    public static readonly TimeSpan ConnectLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Creates a handler that stops a connect attempt after <see cref="ConnectLimit"/> and checks the certificate of the server
    ///     for revocation online.
    /// </summary>
    /// <remarks>
    ///     A connect attempt that passes the limit fails with an <see cref="OperationCanceledException"/> that has a
    ///     <see cref="TimeoutException"/> inside, which <see cref="TransferTimer.IsTimeout"/> checks for.
    /// </remarks>
    /// <param name="followRedirects">Whether the handler follows redirects.</param>
    /// <returns>
    ///     The handler. The caller disposes it.
    /// </returns>
    public static SocketsHttpHandler Create(bool followRedirects)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            ConnectTimeout = ConnectLimit,
            SslOptions = new() { CertificateRevocationCheckMode = X509RevocationMode.Online },
        };
    }
}
