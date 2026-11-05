using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Limits each wait of a download or an upload for the network and turns a passed limit into a <see cref="TimeoutException"/>.
/// </summary>
/// <remarks>
///     Send each request with <see cref="SendAsync"/> and move each chunk of a body with <see cref="ReadAsync"/> or
///     <see cref="WriteAsync"/>, so a slow disk write or a blocked progress report between 2 waits never counts toward a limit.
///     A passed limit needs a <see cref="TimeoutException"/>, because a cancel prints nothing.
/// </remarks>
internal sealed class TransferTimer : IDisposable
{
    /// <summary>
    ///     Limits each wait that moves no bytes, for the response headers or for 1 body read or write, to 1 minute,
    ///     because a live server moves bytes within seconds, even on a slow link.
    /// </summary>
    public static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Limits the wait for the response headers after the last byte of an upload to 5 minutes,
    ///     because a server can take minutes to store a large file before it responds.
    /// </summary>
    public static readonly TimeSpan StoreLimit = TimeSpan.FromMinutes(5);

    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _registration;
    private readonly CancellationTokenSource _source;

    private TimeSpan _limit;
    private bool _waitsForResponse;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransferTimer"/> class, which runs no limit until the first wait.
    /// </summary>
    /// <param name="timeProvider">The clock that measures the limits.</param>
    /// <param name="cancellationToken">The token of the caller, which cancels the transfer too.</param>
    public TransferTimer(TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _source = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);

        // Link the token by hand, because CreateLinkedTokenSource takes no TimeProvider.
        _registration = cancellationToken.Register(static source => ((CancellationTokenSource)source!).Cancel(), _source);
    }

    /// <summary>
    ///     Sends <paramref name="request"/> through <paramref name="client"/> and limits the wait for the response headers
    ///     to <see cref="StallLimit"/>.
    /// </summary>
    /// <remarks>
    ///     The content of the request can replace the limit while its body is sent, through <see cref="WriteAsync"/>
    ///     and <see cref="StartResponseWait"/>. The limit that runs last stops once the call returns.
    /// </remarks>
    /// <param name="client">The client that sends the request.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="completion">When the call returns, after the headers or after the whole body.</param>
    /// <returns>
    ///     The response. The caller disposes it.
    /// </returns>
    /// <exception cref="HttpRequestException">The request failed, such as on a dropped connection.</exception>
    /// <exception cref="OperationCanceledException">
    ///     A limit passed, the handler gave up connecting or the caller canceled. <see cref="IsTimeout"/> separates a timeout
    ///     from a cancel.
    /// </exception>
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completion)
    {
        StartResponseWait(StallLimit);

        try
        {
            return await client.SendAsync(request, completion, _source.Token).ConfigureAwait(false);
        }
        finally
        {
            Stop();
        }
    }

    /// <summary>
    ///     Reads the next chunk of a body from <paramref name="stream"/> into <paramref name="buffer"/> and limits the read
    ///     to <see cref="StallLimit"/>.
    /// </summary>
    /// <param name="stream">The body to read.</param>
    /// <param name="buffer">The buffer to read into.</param>
    /// <returns>
    ///     The number of bytes read, 0 at the end of the body.
    /// </returns>
    /// <exception cref="IOException">The connection dropped.</exception>
    /// <exception cref="OperationCanceledException">The read moved no bytes for <see cref="StallLimit"/> or the caller canceled.</exception>
    public async ValueTask<int> ReadAsync(Stream stream, Memory<byte> buffer)
    {
        Start(StallLimit, waitsForResponse: false);

        try
        {
            return await stream.ReadAsync(buffer, _source.Token).ConfigureAwait(false);
        }
        finally
        {
            Stop();
        }
    }

    /// <summary>
    ///     Writes the next chunk of a body from <paramref name="buffer"/> to <paramref name="stream"/> and limits the write
    ///     to <see cref="StallLimit"/>.
    /// </summary>
    /// <remarks>
    ///     Write a request body only inside <see cref="SendAsync"/>, because the HTTP/1.1 request body stream of the handler
    ///     ignores the token of a write and stops a write only when the request is canceled.
    /// </remarks>
    /// <param name="stream">The body to write to.</param>
    /// <param name="buffer">The bytes to write.</param>
    /// <returns>
    ///     A task that completes once <paramref name="stream"/> took the bytes.
    /// </returns>
    /// <exception cref="IOException">The connection dropped.</exception>
    /// <exception cref="OperationCanceledException">The write moved no bytes for <see cref="StallLimit"/> or the caller canceled.</exception>
    public async ValueTask WriteAsync(Stream stream, ReadOnlyMemory<byte> buffer)
    {
        Start(StallLimit, waitsForResponse: false);

        try
        {
            await stream.WriteAsync(buffer, _source.Token).ConfigureAwait(false);
        }
        finally
        {
            Stop();
        }
    }

    /// <summary>
    ///     Starts the limit of a wait for the response headers over at <paramref name="limit"/>.
    /// </summary>
    /// <remarks>
    ///     <see cref="SendAsync"/> stops the limit once the response arrives.
    /// </remarks>
    /// <param name="limit">The time the server may take to send the response headers.</param>
    public void StartResponseWait(TimeSpan limit)
    {
        Start(limit, waitsForResponse: true);
    }

    /// <summary>
    ///     Checks whether <paramref name="exception"/> stopped the transfer because a time limit passed.
    /// </summary>
    /// <remarks>
    ///     A limit passed when this timer canceled the transfer or when the handler gave up connecting, which
    ///     <see cref="SocketsHttpHandler"/> throws as a cancel with a <see cref="TimeoutException"/> inside.
    ///     A cancel of the caller and any other cancel stay as they are.
    /// </remarks>
    /// <param name="exception">The cancel that stopped the transfer.</param>
    /// <returns>
    ///     <see langword="true"/> if a limit passed and the caller did not cancel.
    /// </returns>
    public bool IsTimeout(OperationCanceledException exception)
    {
        return !_cancellationToken.IsCancellationRequested
            && (_source.IsCancellationRequested || exception.InnerException is TimeoutException);
    }

    /// <summary>
    ///     Creates the exception for a transfer that a time limit stopped.
    /// </summary>
    /// <param name="exception">The cancel that <see cref="IsTimeout"/> accepted.</param>
    /// <param name="method">The method of the request, for the message.</param>
    /// <param name="target">The URL or the store path of the request, for the message.</param>
    /// <param name="fix">The step that fixes the cause, for the message.</param>
    /// <returns>
    ///     The exception to throw. After a connect that passed the limit of the handler, it has the <see cref="TimeoutException"/>
    ///     of the handler inside.
    /// </returns>
    public TimeoutException TimedOut(OperationCanceledException exception, HttpMethod method, string target, string fix)
    {
        if (!_source.IsCancellationRequested)
        {
            return new TimeoutException(SR.Format(SR.TransferNoConnection, method, target, fix), exception.InnerException);
        }

        string format = _waitsForResponse ? SR.TransferNoResponse : SR.TransferStalled;

        return new TimeoutException(SR.Format(format, method, target, _limit.TotalSeconds, fix));
    }

    /// <summary>
    ///     Removes the link to the token of the caller and stops the limit.
    /// </summary>
    public void Dispose()
    {
        // Remove the link first, so a later cancel of the caller never reaches a disposed source.
        _registration.Dispose();
        _source.Dispose();
    }

    /// <summary>
    ///     Starts the time limit over at <paramref name="limit"/> for the next wait.
    /// </summary>
    /// <remarks>
    ///     A limit that passed stays, so a wait that starts after it never changes the message of <see cref="TimedOut"/>.
    /// </remarks>
    /// <param name="limit">The time the next wait may take.</param>
    /// <param name="waitsForResponse">
    ///     <see langword="true"/> for a wait for the response headers, <see langword="false"/> for a body read or write.
    /// </param>
    private void Start(TimeSpan limit, bool waitsForResponse)
    {
        if (_source.IsCancellationRequested)
        {
            return;
        }

        _limit = limit;
        _waitsForResponse = waitsForResponse;
        _source.CancelAfter(limit);
    }

    /// <summary>
    ///     Stops the time limit until the next wait.
    /// </summary>
    private void Stop()
    {
        _source.CancelAfter(Timeout.InfiniteTimeSpan);
    }
}
