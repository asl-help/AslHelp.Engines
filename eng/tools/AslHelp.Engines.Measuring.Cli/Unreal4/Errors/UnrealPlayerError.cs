namespace AslHelp.Engines.Measuring.Cli.Unreal4.Errors;

/// <summary>
///     Represents an Unreal player folder whose id or files do not read.
/// </summary>
internal sealed record UnrealPlayerError : ResultError
{
    private UnrealPlayerError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a player folder without its executable.
    /// </summary>
    /// <param name="path">The path of the executable.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealPlayerError ExecutableNotFound(string path)
    {
        return new(UnrealSR.FormatExecutableNotFound(path));
    }

    /// <summary>
    ///     Creates the error for a player folder without its PDB.
    /// </summary>
    /// <param name="path">The path of the PDB.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealPlayerError PdbNotFound(string path)
    {
        return new(UnrealSR.FormatPdbNotFound(path));
    }

    /// <summary>
    ///     Creates the error for a text that doesn't have the form of a player id.
    /// </summary>
    /// <param name="id">The text.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UnrealPlayerError PlayerIdInvalid(string id)
    {
        return new(UnrealSR.FormatPlayerIdInvalid(id));
    }
}
