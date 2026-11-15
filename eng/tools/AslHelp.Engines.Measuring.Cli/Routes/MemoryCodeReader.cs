using Iced.Intel;
using System;
using System.Buffers;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Reads code for a decoder straight from memory, because the code of an image stays in its file mapping and a copy would cost
///     its whole size.
/// </summary>
internal sealed unsafe class MemoryCodeReader : CodeReader, IDisposable
{
    private readonly int _length;

    // Stays writable, because the dispose of a handle changes it.
    private MemoryHandle _handle;
    private readonly byte* _start;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryCodeReader"/> class and pins <paramref name="code"/> until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="code">The code.</param>
    public MemoryCodeReader(ReadOnlyMemory<byte> code)
    {
        _handle = code.Pin();
        _start = (byte*)_handle.Pointer;
        _length = code.Length;
    }

    /// <summary>
    ///     Gets a value indicating whether a byte is left to read.
    /// </summary>
    public bool CanReadByte => (uint)Position < (uint)_length;

    /// <summary>
    ///     Gets or sets the position of the next byte to read.
    /// </summary>
    public int Position { get; set; }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        return (uint)Position < (uint)_length ? _start[Position++] : -1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _handle.Dispose();
    }
}
