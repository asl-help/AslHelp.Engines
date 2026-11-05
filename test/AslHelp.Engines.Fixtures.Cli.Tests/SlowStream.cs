using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Moves the clock forward by a fixed time on each read and write and returns 1 byte per read, like a slow connection
///     that still moves bytes.
/// </summary>
/// <param name="clock">The clock that each read and write moves forward.</param>
/// <param name="delay">The time each read and write takes on <paramref name="clock"/>.</param>
/// <param name="body">The bytes to return, 1 per read.</param>
internal sealed class SlowStream(
    ManualTimeProvider clock,
    TimeSpan delay,
    byte[] body) : Stream
{
    private int _position;

    /// <summary>
    ///     Gets the bytes written so far, in order.
    /// </summary>
    public List<byte> Written { get; } = [];

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        clock.Advance(delay);

        // Fail a read whose token was canceled during the delay, because a network read stops once its token is canceled.
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        if (_position == body.Length)
        {
            return ValueTask.FromResult(0);
        }

        buffer.Span[0] = body[_position++];

        return ValueTask.FromResult(1);
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        clock.Advance(delay);

        // Fail a write whose token was canceled during the delay, because a network write stops once its request is canceled.
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        Written.AddRange(buffer.Span);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
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
