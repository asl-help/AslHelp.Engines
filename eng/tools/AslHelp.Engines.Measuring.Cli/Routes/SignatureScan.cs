using Iced.Intel;
using System;
using System.Globalization;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Searches code for 1 candidate.
/// </summary>
internal sealed class SignatureScan
{
    private const byte Literal = 0xFF;

    private readonly byte[] _masks;
    private readonly int _offset;
    private readonly int _runAt;
    private readonly int _runLength;
    private readonly byte[] _values;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SignatureScan"/> class.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    public SignatureScan(RouteCandidate candidate)
    {
        _offset = candidate.Offset;
        _values = new byte[candidate.Bytes];
        _masks = new byte[candidate.Bytes];

        int position = 0;

        foreach (string token in candidate.Forward.Split(' '))
        {
            if (token[0] == '?')
            {
                position += token.Length / 2;
            }
            else
            {
                _values[position] = byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                _masks[position] = Literal;
                position++;
            }
        }

        // Find the longest literal run, because a search on it skips the most positions.
        int runStart = 0;

        for (int index = 0; index <= _masks.Length; index++)
        {
            if (index < _masks.Length && _masks[index] == Literal)
            {
                continue;
            }

            if (index - runStart > _runLength)
            {
                _runAt = runStart;
                _runLength = index - runStart;
            }

            runStart = index + 1;
        }
    }

    /// <summary>
    ///     Searches every code section of <paramref name="image"/>.
    /// </summary>
    /// <param name="image">The code.</param>
    /// <returns>
    ///     The 1 RVA that every match reaches or <see langword="null"/> if none matched, 2 disagree or 1 is not an address operand.
    /// </returns>
    public uint? Search(CodeImage image)
    {
        ReadOnlySpan<byte> run = _values.AsSpan(_runAt, _runLength);
        uint? reached = null;

        foreach (var section in image.Sections)
        {
            ReadOnlySpan<byte> code = section.Bytes;

            if (code.Length < _values.Length)
            {
                continue;
            }

            ByteArrayCodeReader reader = new(section.Bytes);
            ulong sectionAddress = image.ImageBase + section.Rva;
            var decoder = Decoder.Create(image.Is64Bit ? 64 : 32, reader, sectionAddress);

            int from = _runAt;
            int lastRunAt = code.Length - _values.Length + _runAt;

            while (from <= lastRunAt)
            {
                int found = code[from..(lastRunAt + _runLength)].IndexOf(run);

                if (found < 0)
                {
                    break;
                }

                int match = from + found - _runAt;
                from += found + 1;

                if (!IsMatch(code[match..]))
                {
                    continue;
                }

                if (ReadRva(decoder, reader, sectionAddress, match, image) is not { } rva || (reached is not null && rva != reached))
                {
                    return null;
                }

                reached = rva;
            }
        }

        return reached;
    }

    private bool IsMatch(ReadOnlySpan<byte> code)
    {
        for (int index = 0; index < _values.Length; index++)
        {
            if (((code[index] ^ _values[index]) & _masks[index]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Decodes from <paramref name="match"/> to the instruction at the offset of the candidate and reads the RVA of its address operand.
    /// </summary>
    /// <param name="decoder">The decoder of the section.</param>
    /// <param name="reader">The reader of <paramref name="decoder"/>.</param>
    /// <param name="sectionAddress">The address of the section.</param>
    /// <param name="match">The position of the match in the section.</param>
    /// <param name="image">The code.</param>
    /// <returns>
    ///     The RVA or <see langword="null"/> if no address operand starts at the offset or its address is outside the image.
    /// </returns>
    private uint? ReadRva(Decoder decoder, ByteArrayCodeReader reader, ulong sectionAddress, int match, CodeImage image)
    {
        int operandAt = match + _offset;
        int start = match;

        reader.Position = match;
        decoder.IP = sectionAddress + (uint)match;

        while (true)
        {
            decoder.Decode(out var instruction);

            if (instruction.IsInvalid)
            {
                return null;
            }

            if (operandAt >= start + instruction.Length)
            {
                start += instruction.Length;

                continue;
            }

            var offsets = decoder.GetConstantOffsets(instruction);
            ulong? address = null;

            if (start + offsets.DisplacementOffset == operandAt && offsets.DisplacementSize != 0)
            {
                address = InstructionSites.DisplacementAddress(instruction, offsets, image.Is64Bit);
            }
            else if (start + offsets.ImmediateOffset == operandAt && offsets.ImmediateSize != 0)
            {
                address = InstructionSites.ImmediateAddress(instruction, offsets, image.Is64Bit);
            }

            // Refuse an address below the image base, because a small value at the offset is a match in real code and doesn't have an RVA.
            if (address is not { } value || value < image.ImageBase || value - image.ImageBase > uint.MaxValue)
            {
                return null;
            }

            return (uint)(value - image.ImageBase);
        }
    }
}
