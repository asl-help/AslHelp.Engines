using Iced.Intel;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Finds the instructions with an address operand of a wanted address.
/// </summary>
internal static class InstructionSites
{
    private const int AddressSize = sizeof(uint);
    private const int LargeAddressSize = sizeof(ulong);
    private const byte ModRmFormMask = 0xC7;
    private const byte ModRmFormOfAddress = 0x05;
    private const byte RexPrefixForm = 0x40;
    private const byte RexPrefixMask = 0xF0;

    private static readonly SearchValues<byte> _legacyPrefixes = SearchValues.Create(0x26, 0x2E, 0x36, 0x3E, 0x64, 0x65, 0x66, 0x67, 0xF0, 0xF2, 0xF3);

    /// <summary>
    ///     Decodes the code sections of <paramref name="image"/> linearly and lists the window around each site.
    /// </summary>
    /// <remarks>
    ///     A window starts after the last padding, return or jump before the site and ends at the 1st return or jump after it or before the 1st padding,
    ///     because padding moves with the code it pads and the bytes after a return or a jump belong to other code.
    /// </remarks>
    /// <param name="image">The code.</param>
    /// <param name="addresses">The wanted addresses at the preferred image base.</param>
    /// <returns>
    ///     The windows in section and code order.
    /// </returns>
    public static IReadOnlyList<SiteWindow> Find(CodeImage image, IReadOnlySet<ulong> addresses)
    {
        List<SiteWindow> windows = [];
        int[] starts = new int[RouteCandidates.MostNeighbors];

        foreach (var section in image.Sections)
        {
            using MemoryCodeReader reader = new(section.Bytes);
            ulong sectionAddress = image.ImageBase + section.Rva;
            var decoder = Decoder.Create(image.Is64Bit ? 64 : 32, reader, sectionAddress);

            // Count the decoded instructions since the last undecodable bytes, padding, return or jump, because a window never crosses them.
            int decoded = 0;

            while (reader.CanReadByte)
            {
                decoder.Decode(out var instruction);

                if (instruction.IsInvalid)
                {
                    decoded = 0;

                    continue;
                }

                int start = (int)(instruction.IP - sectionAddress);
                var offsets = decoder.GetConstantOffsets(instruction);

                int before = int.Min(decoded, starts.Length);
                int from = before == 0 ? start : starts[(decoded - before) % starts.Length];

                if (DisplacementAddress(instruction, offsets, image.Is64Bit) is { } displacement && addresses.Contains(displacement))
                {
                    windows.Add(ReadWindow(image, section, from, before, displacement, start - from + offsets.DisplacementOffset));
                }

                if (ImmediateAddress(instruction, offsets, image.Is64Bit) is { } immediate && addresses.Contains(immediate))
                {
                    windows.Add(ReadWindow(image, section, from, before, immediate, start - from + offsets.ImmediateOffset));
                }

                if (IsPadding(instruction) || EndsPath(instruction))
                {
                    decoded = 0;

                    continue;
                }

                starts[decoded % starts.Length] = start;
                decoded++;
            }
        }

        return windows;
    }

    /// <summary>
    ///     Gets the address of the displacement of <paramref name="instruction"/> if the read step of a route can reach it.
    /// </summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="offsets">The operand positions of the instruction.</param>
    /// <param name="is64Bit">Whether the code is x64.</param>
    /// <returns>
    ///     The address or <see langword="null"/> if the displacement is not an address operand.
    /// </returns>
    public static ulong? DisplacementAddress(in Instruction instruction, in ConstantOffsets offsets, bool is64Bit)
    {
        if (offsets.DisplacementSize != AddressSize)
        {
            return null;
        }

        if (is64Bit)
        {
            // Refuse an immediate after the displacement, because the read step adds 4 to the operand position and reaches the wrong address.
            return instruction.IsIPRelativeMemoryOperand && offsets.DisplacementOffset + AddressSize == instruction.Length
                ? instruction.IPRelativeMemoryAddress
                : null;
        }

        return instruction is { MemoryBase: Register.None, MemoryIndex: Register.None } ? instruction.MemoryDisplacement32 : null;
    }

    /// <summary>
    ///     Gets the address of the immediate of <paramref name="instruction"/> if the immediate can be an address.
    /// </summary>
    /// <param name="instruction">The instruction.</param>
    /// <param name="offsets">The operand positions of the instruction.</param>
    /// <param name="is64Bit">Whether the code is x64.</param>
    /// <returns>
    ///     The address or <see langword="null"/> if the instruction doesn't have such an immediate,
    ///     always on x64 because x64 code reaches a global RIP-relative.
    /// </returns>
    public static ulong? ImmediateAddress(in Instruction instruction, in ConstantOffsets offsets, bool is64Bit)
    {
        return !is64Bit && offsets.ImmediateSize == AddressSize && !IsNearBranch(instruction) ? instruction.Immediate32 : null;
    }

    /// <summary>
    ///     Decodes the instructions from <paramref name="from"/> to the 8th after the site and copies their bytes.
    ///     Stops before padding and after a return or a jump at or after the site.
    /// </summary>
    /// <param name="image">The code.</param>
    /// <param name="section">The section of the site.</param>
    /// <param name="from">The position in the section of the 1st instruction of the window.</param>
    /// <param name="site">The index of the site in the window.</param>
    /// <param name="address">The address that the operand of the site reaches.</param>
    /// <param name="operandAt">The position in the window of the address operand.</param>
    /// <returns>
    ///     The window.
    /// </returns>
    private static SiteWindow ReadWindow(CodeImage image, CodeSection section, int from, int site, ulong address, int operandAt)
    {
        using MemoryCodeReader reader = new(section.Bytes[from..]);
        var decoder = Decoder.Create(image.Is64Bit ? 64 : 32, reader, image.ImageBase + section.Rva + (uint)from);

        List<int> starts = [];
        List<(int At, int Size)> masks = [];
        int length = 0;
        int registerAt = -1;
        int prefixAt = -1;

        while (starts.Count <= site + RouteCandidates.MostNeighbors && reader.CanReadByte)
        {
            decoder.Decode(out var instruction);

            if (instruction.IsInvalid || (starts.Count > site && IsPadding(instruction)))
            {
                break;
            }

            var offsets = decoder.GetConstantOffsets(instruction);

            // Check the form of the byte before a displacement, because the byte before the operand of mov eax, [address] is the opcode.
            if (starts.Count == site
                && offsets.DisplacementSize != 0
                && length + offsets.DisplacementOffset == operandAt
                && (section.Bytes.Span[from + operandAt - 1] & ModRmFormMask) == ModRmFormOfAddress)
            {
                // Step over a SIB byte, because [address] also encodes as ModRM, SIB, displacement and only the opcode comes right before the ModRM.
                registerAt = section.Bytes.Span[from + operandAt - 2] == (byte)instruction.OpCode.OpCode ? operandAt - 1 : operandAt - 2;

                // Look for a REX prefix only on x64, because 0x40 to 0x4F are the opcodes of inc and dec on x86.
                int rex = image.Is64Bit ? RexPrefixAt(section.Bytes.Span[(from + length)..(from + registerAt)]) : -1;
                prefixAt = rex < 0 ? -1 : length + rex;
            }

            if (IsAddressDisplacement(instruction, offsets, image))
            {
                masks.Add((length + offsets.DisplacementOffset, offsets.DisplacementSize));
            }

            if (IsAddressImmediate(instruction, offsets, image))
            {
                masks.Add((length + offsets.ImmediateOffset, offsets.ImmediateSize));
            }

            starts.Add(length);
            length += instruction.Length;

            if (starts.Count > site && EndsPath(instruction))
            {
                break;
            }
        }

        byte[] bytes = section.Bytes.Span.Slice(from, length).ToArray();
        int[] operands = new int[length];

        for (int mask = 0; mask < masks.Count; mask++)
        {
            operands.AsSpan(masks[mask].At, masks[mask].Size).Fill(mask + 1);
        }

        return new SiteWindow(address, bytes, [.. starts], site, operands, operandAt, registerAt, prefixAt);
    }

    /// <summary>
    ///     Finds the REX prefix among the bytes of an x64 instruction before its ModRM byte.
    /// </summary>
    /// <param name="head">The bytes from the start of the instruction to its ModRM byte.</param>
    /// <returns>
    ///     The position of the REX prefix in <paramref name="head"/> or -1 if it has none.
    /// </returns>
    private static int RexPrefixAt(ReadOnlySpan<byte> head)
    {
        // Step over the legacy prefixes, because the REX prefix comes after them, right before the opcode.
        int position = head.IndexOfAnyExcept(_legacyPrefixes);

        return position >= 0 && (head[position] & RexPrefixMask) == RexPrefixForm ? position : -1;
    }

    private static bool IsAddressDisplacement(in Instruction instruction, in ConstantOffsets offsets, CodeImage image)
    {
        return instruction.IsIPRelativeMemoryOperand
            || (offsets.DisplacementSize >= AddressSize && IsInsideImage(instruction.MemoryDisplacement64, image));
    }

    private static bool IsAddressImmediate(in Instruction instruction, in ConstantOffsets offsets, CodeImage image)
    {
        return IsNearBranch(instruction)
            || (offsets.ImmediateSize == AddressSize && IsInsideImage(instruction.Immediate32, image))
            || (offsets.ImmediateSize == LargeAddressSize && IsInsideImage(instruction.Immediate64, image));
    }

    // Treat a return and a jump as the end of the straight path, because the bytes after them belong to other code.
    private static bool EndsPath(in Instruction instruction)
    {
        return instruction.FlowControl is FlowControl.Return or FlowControl.UnconditionalBranch or FlowControl.IndirectBranch;
    }

    // Treat nop and int3 as padding, because a compiler fills space with them and they move with what they pad.
    private static bool IsPadding(in Instruction instruction)
    {
        return instruction.Mnemonic is Mnemonic.Nop or Mnemonic.Int3;
    }

    private static bool IsInsideImage(ulong value, CodeImage image)
    {
        return value >= image.ImageBase && value - image.ImageBase < image.ImageSize;
    }

    private static bool IsNearBranch(in Instruction instruction)
    {
        return instruction.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64;
    }
}
