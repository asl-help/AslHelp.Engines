namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a list of UDK targets that breaks a rule of the UDK measurement.
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
}
