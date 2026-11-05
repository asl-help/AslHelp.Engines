using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Returns a few pattern bytes and then moves no bytes until the token of the read or write is canceled, like a
///     connection that goes silent without a reset.
/// </summary>
/// <param name="clock">The clock that each stalled read or write moves forward.</param>
/// <param name="stall">The time a stalled read or write moves <paramref name="clock"/> forward before it waits for the cancel.</param>
/// <param name="length">The number of bytes to return before the reads stall.</param>
internal sealed class StallingStream(
    ManualTimeProvider clock,
    TimeSpan stall,
    int length) : Stream
{
    private int _position;

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
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < length)
        {
            int read = Math.Min(buffer.Length, length - _position);

            for (int i = 0; i < read; i++)
            {
                buffer.Span[i] = (byte)(_position++ & 0xFF);
            }

            return read;
        }

        await clock.StallAsync(stall, cancellationToken);

        throw new UnreachableException();
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await clock.StallAsync(stall, cancellationToken);

        throw new UnreachableException();
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
