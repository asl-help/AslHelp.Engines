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
    ///     Searches every part of <paramref name="image"/> that the loader maps, the code, the headers, the sections without code
    ///     and the zeros after the bytes of each part, because a scan of the whole module at run time reads them all.
    /// </summary>
    /// <param name="image">The code.</param>
    /// <returns>
    ///     Whether anything matched, with the 1 RVA that every match reaches or <see langword="null"/> if 2 disagree, 1 is not an address operand
    ///     or 1 reaches into the zeros.
    /// </returns>
    public ScanResult Search(CodeImage image)
    {
        uint? reached = null;

        for (int index = 0; index < PartCount(image); index++)
        {
            if (!SearchPart(image, PartAt(image, index), ref reached))
            {
                return ScanResult.Conflicting;
            }
        }

        if (MatchesZeros(image))
        {
            return ScanResult.Conflicting;
        }

        return reached is { } only ? ScanResult.Reached(only) : ScanResult.None;
    }

    private static int PartCount(CodeImage image)
    {
        return image.Sections.Count + image.Data.Count;
    }

    private static CodeSection PartAt(CodeImage image, int index)
    {
        return index < image.Sections.Count ? image.Sections[index] : image.Data[index - image.Sections.Count];
    }

    /// <summary>
    ///     Searches the bytes of 1 part and checks that every match reaches <paramref name="reached"/>.
    /// </summary>
    /// <param name="image">The code.</param>
    /// <param name="part">The part.</param>
    /// <param name="reached">The RVA that the matches so far reach or <see langword="null"/> before the 1st match.</param>
    /// <returns>
    ///     <see langword="false"/> if a match reaches another RVA or is not an address operand.
    /// </returns>
    private bool SearchPart(CodeImage image, CodeSection part, ref uint? reached)
    {
        var bytes = part.Bytes.Span;

        if (bytes.Length < _values.Length)
        {
            return true;
        }

        ReadOnlySpan<byte> run = _values.AsSpan(_runAt, _runLength);
        ulong partAddress = image.ImageBase + part.Rva;
        MemoryCodeReader? reader = null;
        Decoder? decoder = null;

        try
        {
            int from = _runAt;
            int lastRunAt = bytes.Length - _values.Length + _runAt;

            while (from <= lastRunAt)
            {
                int found = bytes[from..(lastRunAt + _runLength)].IndexOf(run);

                if (found < 0)
                {
                    break;
                }

                int match = from + found - _runAt;
                from += found + 1;

                if (!IsMatch(bytes[match..]))
                {
                    continue;
                }

                // Create the decoder at the 1st match, because most parts don't have one and the scan of a part then allocates nothing.
                reader ??= new MemoryCodeReader(part.Bytes);
                decoder ??= Decoder.Create(image.Is64Bit ? 64 : 32, reader, partAddress);

                if (ReadRva(decoder, reader, partAddress, match, image) is not { } rva || (reached is not null && rva != reached))
                {
                    return false;
                }

                reached = rva;
            }

            return true;
        }
        finally
        {
            reader?.Dispose();
        }
    }

    /// <summary>
    ///     Checks for a match that reads a zero the loader fills in, past the bytes of a part or before the 1st part.
    /// </summary>
    /// <param name="image">The code.</param>
    /// <returns>
    ///     <see langword="true"/> if such a match exists, which reaches no address.
    /// </returns>
    private bool MatchesZeros(CodeImage image)
    {
        bool zerosMatch = !_values.AsSpan().ContainsAnyExcept((byte)0);
        long firstRva = image.ImageSize;

        for (int index = 0; index < PartCount(image); index++)
        {
            var part = PartAt(image, index);

            // Skip a part without bytes, such as uninitialized data, because its whole size is zero fill that the parts with bytes border.
            if (part.Bytes.IsEmpty)
            {
                continue;
            }

            long start = part.Rva;
            long end = start + part.Bytes.Length;
            firstRva = long.Min(firstRva, start);

            // Check each match that crosses the start or the end of the part, because it reads zeros or the bytes of a neighbor.
            if (MatchesAcross(image, start) || MatchesAcross(image, end))
            {
                return true;
            }

            if (zerosMatch && NextPartAt(image, end) - end >= _values.Length)
            {
                return true;
            }
        }

        return zerosMatch && firstRva >= _values.Length;
    }

    private bool MatchesAcross(CodeImage image, long boundary)
    {
        for (long start = long.Max(0, boundary - _values.Length + 1); start < boundary; start++)
        {
            if (MatchesAt(image, start))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesAt(CodeImage image, long start)
    {
        long end = start + _values.Length;

        if (end > image.ImageSize)
        {
            return false;
        }

        // Copy the bytes of each part once into a window of zeros, because the image reads as zeros outside its parts
        // and the span of a mapped part costs a call.
        Span<byte> window = stackalloc byte[_values.Length];
        window.Clear();

        for (int index = 0; index < PartCount(image); index++)
        {
            var part = PartAt(image, index);
            long from = long.Max(start, part.Rva);
            long to = long.Min(end, part.Rva + part.Bytes.Length);

            if (from < to)
            {
                part.Bytes.Span[(int)(from - part.Rva)..(int)(to - part.Rva)].CopyTo(window[(int)(from - start)..]);
            }
        }

        return IsMatch(window);
    }

    private static long NextPartAt(CodeImage image, long end)
    {
        long next = image.ImageSize;

        for (int index = 0; index < PartCount(image); index++)
        {
            var part = PartAt(image, index);

            if (!part.Bytes.IsEmpty && part.Rva >= end && part.Rva < next)
            {
                next = part.Rva;
            }
        }

        return next;
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
    private uint? ReadRva(Decoder decoder, MemoryCodeReader reader, ulong sectionAddress, int match, CodeImage image)
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

            // Refuse an address outside the image, because a small value at the offset is a match in real code and doesn't have an RVA.
            if (address is not { } value || value < image.ImageBase || value - image.ImageBase >= image.ImageSize)
            {
                return null;
            }

            return (uint)(value - image.ImageBase);
        }
    }
}
