namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a list of UDK targets that is not valid.
/// </summary>
internal sealed record UdkTargetsError : ResultError
{
    private UdkTargetsError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a target with a blank value.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="global">The global of the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsBlankValue(string path, string global)
    {
        return new(UdkSR.FormatTargetsBlankValue(path, global));
    }

    /// <summary>
    ///     Creates the error for a file without targets.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsEmpty(string path)
    {
        return new(UdkSR.FormatTargetsEmpty(path));
    }

    /// <summary>
    ///     Creates the error for a name array with too few first names.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="global">The global of the target.</param>
    /// <param name="least">The smallest number of first names.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsFewFirstNames(string path, string global, int least)
    {
        return new(UdkSR.FormatTargetsFewFirstNames(path, global, least));
    }

    /// <summary>
    ///     Creates the error for a file that does not parse as targets.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the parser.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsInvalid(string path, string reason)
    {
        return new(UdkSR.FormatTargetsInvalid(path, reason));
    }

    /// <summary>
    ///     Creates the error for a file without exactly 1 name array.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsNeedsOneNameArray(string path)
    {
        return new(UdkSR.FormatTargetsNeedsOneNameArray(path));
    }

    /// <summary>
    ///     Creates the error for a file without exactly 1 object array.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsNeedsOneObjectArray(string path)
    {
        return new(UdkSR.FormatTargetsNeedsOneObjectArray(path));
    }

    /// <summary>
    ///     Creates the error for an assembly without the embedded targets.
    /// </summary>
    /// <param name="resource">The name of the resource.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsNotEmbedded(string resource)
    {
        return new(UdkSR.FormatTargetsNotEmbedded(resource));
    }

    /// <summary>
    ///     Creates the error for a targets file that does not exist.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsNotFound(string path)
    {
        return new(UdkSR.FormatTargetsNotFound(path));
    }

    /// <summary>
    ///     Creates the error for a null target.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="global">The global of the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkTargetsError TargetsNull(string path, string global)
    {
        return new(UdkSR.FormatTargetsNull(path, global));
    }
}
