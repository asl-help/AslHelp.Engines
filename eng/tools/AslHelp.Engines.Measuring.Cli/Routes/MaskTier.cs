namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Specifies which bytes of a window a candidate masks.
/// </summary>
internal enum MaskTier
{
    /// <summary>
    ///     Masks the address operands and the branch targets.
    /// </summary>
    Strict,

    /// <summary>
    ///     Also masks the ModRM byte of the load, for a global that the versions load into different registers.
    /// </summary>
    Register,
}
