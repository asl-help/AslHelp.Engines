namespace AslHelp.Engines.Measuring.Cli.Targets.Errors;

/// <summary>
///     Represents a targets file of any engine that is missing, does not read or breaks a rule that every engine has.
/// </summary>
internal sealed record TargetsError : ResultError
{
    private TargetsError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a target with a blank name.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError BlankName(string path)
    {
        return new(SR.FormatTargetsBlankName(path));
    }

    /// <summary>
    ///     Creates the error for a target with a blank value.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError BlankValue(string path, string name)
    {
        return new(SR.FormatTargetsBlankValue(path, name));
    }

    /// <summary>
    ///     Creates the error for a file without targets.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError Empty(string path)
    {
        return new(SR.FormatTargetsEmpty(path));
    }

    /// <summary>
    ///     Creates the error for a file that does not parse as targets.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the parser.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError Invalid(string path, string reason)
    {
        return new(SR.FormatTargetsInvalid(path, reason));
    }

    /// <summary>
    ///     Creates the error for an assembly without the embedded targets.
    /// </summary>
    /// <param name="resource">The name of the resource.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError NotEmbedded(string resource)
    {
        return new(SR.FormatTargetsNotEmbedded(resource));
    }

    /// <summary>
    ///     Creates the error for a targets file that does not exist.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError NotFound(string path)
    {
        return new(SR.FormatTargetsNotFound(path));
    }

    /// <summary>
    ///     Creates the error for a null target.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="name">The name of the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError Null(string path, string name)
    {
        return new(SR.FormatTargetsNull(path, name));
    }

    /// <summary>
    ///     Creates the error for a targets file that did not open or read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static TargetsError Unreadable(string path, string reason)
    {
        return new(SR.FormatFileUnreadable(path, reason));
    }
}
