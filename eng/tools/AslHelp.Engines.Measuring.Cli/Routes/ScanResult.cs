namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Describes the matches of 1 candidate in 1 image.
/// </summary>
/// <param name="Matched">Whether any match exists.</param>
/// <param name="Rva">
///     The 1 RVA that every match reaches or <see langword="null"/> if nothing matched, 2 matches disagree or 1 is not an address operand.
/// </param>
internal readonly record struct ScanResult(
    bool Matched,
    uint? Rva)
{
    /// <summary>
    ///     Gets the result of a search whose matches do not agree on 1 RVA.
    /// </summary>
    public static ScanResult Conflicting => new(true, null);

    /// <summary>
    ///     Gets the result of a search without any match.
    /// </summary>
    public static ScanResult None => default;

    /// <summary>
    ///     Creates the result of a search whose every match reaches <paramref name="rva"/>.
    /// </summary>
    /// <param name="rva">The RVA.</param>
    /// <returns>
    ///     The result.
    /// </returns>
    public static ScanResult Reached(uint rva)
    {
        return new(true, rva);
    }
}
