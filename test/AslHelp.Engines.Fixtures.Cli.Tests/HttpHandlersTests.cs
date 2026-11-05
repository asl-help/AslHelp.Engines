using NUnit.Framework;
using System.Security.Cryptography.X509Certificates;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class HttpHandlersTests
{
    // The handler turns on the connect limit and the revocation check, because SocketsHttpHandler has both off by default.
    [TestCase(false)]
    [TestCase(true)]
    public void Create_EitherRedirectSetting_LimitsTheConnectAndChecksRevocation(bool followRedirects)
    {
        using var handler = HttpHandlers.Create(followRedirects);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handler.AllowAutoRedirect, Is.EqualTo(followRedirects));
            Assert.That(handler.ConnectTimeout, Is.EqualTo(HttpHandlers.ConnectLimit));
            Assert.That(handler.SslOptions.CertificateRevocationCheckMode, Is.EqualTo(X509RevocationMode.Online));
        }
    }
}
