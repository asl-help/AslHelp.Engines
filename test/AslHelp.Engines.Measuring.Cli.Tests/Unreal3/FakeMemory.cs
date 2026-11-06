using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

/// <summary>
///     Reads from x86 byte blocks that a test adds.
/// </summary>
/// <remarks>
///     A read succeeds only when all its bytes sit in 1 block, because a real read fails past the end of committed memory.
/// </remarks>
internal sealed class FakeMemory : IMemoryReader
{
    private readonly List<(Address Start, byte[] Bytes)> _blocks = [];
    private readonly Dictionary<Address, int> _reads = [];

    /// <inheritdoc/>
    public MemoryLayout Layout => MemoryLayout.X86;

    /// <summary>
    ///     Adds a block of readable bytes.
    /// </summary>
    /// <param name="start">The address of the 1st byte.</param>
    /// <param name="bytes">The bytes, which later writes change.</param>
    public void Add(Address start, byte[] bytes)
    {
        _blocks.Add((start, bytes));
    }

    /// <summary>
    ///     Counts the reads that started at <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the 1st byte of the reads.</param>
    /// <returns>
    ///     The number of reads so far.
    /// </returns>
    public int ReadsAt(Address address)
    {
        return _reads.GetValueOrDefault(address);
    }

    /// <summary>
    ///     Writes a 4-byte value into the block that contains <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the value.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentOutOfRangeException">No block contains <paramref name="address"/>.</exception>
    public void WriteInt32(Address address, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(BytesAt(address, sizeof(int)), value);
    }

    /// <summary>
    ///     Writes a pointer into the block that contains <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="value">The pointer.</param>
    /// <exception cref="ArgumentOutOfRangeException">No block contains <paramref name="address"/>.</exception>
    public void WritePointer(Address address, Address value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(BytesAt(address, sizeof(uint)), (uint)value.Value);
    }

    /// <summary>
    ///     Writes <paramref name="bytes"/> into the block that contains <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the 1st byte.</param>
    /// <param name="bytes">The bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">No block contains all the bytes.</exception>
    public void WriteBytes(Address address, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(BytesAt(address, bytes.Length));
    }

    /// <inheritdoc/>
    public Result Read(Address address, Span<byte> buffer)
    {
        _reads[address] = _reads.GetValueOrDefault(address) + 1;

        foreach ((var start, byte[] bytes) in _blocks)
        {
            long at = address - start;

            if (at >= 0 && at + buffer.Length <= bytes.Length)
            {
                bytes.AsSpan((int)at, buffer.Length).CopyTo(buffer);

                return Result.Ok();
            }
        }

        return new UnreadableError(address);
    }

    private Span<byte> BytesAt(Address address, int length)
    {
        foreach ((var start, byte[] bytes) in _blocks)
        {
            long at = address - start;

            if (at >= 0 && at + length <= bytes.Length)
            {
                return bytes.AsSpan((int)at, length);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(address), address, "No block contains this address.");
    }

    /// <summary>
    ///     Represents a read outside the blocks.
    /// </summary>
    /// <param name="Address">The address of the read.</param>
    private sealed record UnreadableError(
        Address Address) : ResultError
    {
        /// <inheritdoc/>
        public override string Message => $"No block contains {Address}.";
    }
}
