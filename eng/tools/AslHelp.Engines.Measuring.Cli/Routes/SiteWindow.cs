namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents the instructions around 1 instruction with an address operand of a wanted address.
/// </summary>
/// <param name="Address">The address that the operand reaches.</param>
/// <param name="Bytes">A copy of the bytes of the instructions.</param>
/// <param name="Starts">The position in <paramref name="Bytes"/> of each instruction.</param>
/// <param name="Site">The index in <paramref name="Starts"/> of the instruction with the address operand.</param>
/// <param name="Operands">The number of the masked operand of each byte or 0 for a byte that stays literal.</param>
/// <param name="OperandAt">The position in <paramref name="Bytes"/> of the address operand.</param>
/// <param name="RegisterAt">The position in <paramref name="Bytes"/> of the ModRM byte of the load or -1 if it has none.</param>
internal sealed record SiteWindow(
    ulong Address,
    byte[] Bytes,
    int[] Starts,
    int Site,
    int[] Operands,
    int OperandAt,
    int RegisterAt);
