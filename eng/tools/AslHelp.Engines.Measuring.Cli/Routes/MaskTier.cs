namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Specifies which bytes of a window a candidate masks, from the tightest to the loosest.
/// </summary>
/// <remarks>
///     Each tier masks what the tier before it masks and more. The search tries them in order, so a route takes the tightest mask that matches.
/// </remarks>
internal enum MaskTier
{
    /// <summary>
    ///     Masks the address operands and the branch targets.
    /// </summary>
    Strict,

    /// <summary>
    ///     Also masks the low nibble of the REX prefix of the load, for a global that the versions load into rcx and r9.
    /// </summary>
    /// <remarks>
    ///     The nibble holds REX.W, REX.X and REX.B as well as REX.R, which is sound for the RIP-relative form <c>00 rrr 101</c> that reads
    ///     neither X nor B, and a SIB form that REX.X changes fails the address check of the scan.
    /// </remarks>
    Prefix,

    /// <summary>
    ///     Also masks the high nibble of the ModRM byte of the load, for a global that the versions load into registers with an equal lowest bit.
    /// </summary>
    Nibble,

    /// <summary>
    ///     Also masks the whole ModRM byte of the load, for a global that the versions load into any registers.
    /// </summary>
    Register,
}
