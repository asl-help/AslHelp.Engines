namespace AslHelp.Engines.Measuring.Cli.Files.Errors;

/// <summary>
///     Represents a file of any kind that does not open or does not have the format it needs.
/// </summary>
internal sealed record FileError : ResultError
{
    private FileError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for an executable that is not x86 or x64.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static FileError MachineUnsupported(string path)
    {
        return new(SR.FormatExecutableMachineUnsupported(path));
    }

    /// <summary>
    ///     Creates the error for a file that is not a PE file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static FileError NotPe(string path)
    {
        return new(SR.FormatExecutableNotPe(path));
    }

    /// <summary>
    ///     Creates the error for a file that did not open or read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the exception.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static FileError Unreadable(string path, string reason)
    {
        return new(SR.FormatFileUnreadable(path, reason));
    }
}
