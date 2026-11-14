namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a UDK player whose files or process did not read.
/// </summary>
internal sealed record UdkPlayerError : ResultError
{
    private UdkPlayerError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a file that is not a PE file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkPlayerError ExecutableNotPe(string path)
    {
        return new(SR.FormatExecutableNotPe(path));
    }

    /// <summary>
    ///     Creates the error for a file that did not open or read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="reason">The message of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkPlayerError FileUnreadable(string path, string reason)
    {
        return new(UdkSR.FormatFileUnreadable(path, reason));
    }

    /// <summary>
    ///     Creates the error for an executable that the server did not load.
    /// </summary>
    /// <param name="name">The file name of the executable.</param>
    /// <param name="cause">The error of the module lookup.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkPlayerError ModuleNotLoaded(string name, ResultError cause)
    {
        return new(UdkSR.FormatModuleNotLoaded(name, cause.Message));
    }

    /// <summary>
    ///     Creates the error for a server that exited during the measurement.
    /// </summary>
    /// <param name="exitCode">The exit code of the server.</param>
    /// <param name="cause">The error of the measurement or <see langword="null"/> if the measurement passed.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkPlayerError PlayerExitedDuringMeasurement(int exitCode, ResultError? cause)
    {
        string message = UdkSR.FormatPlayerExitedDuringMeasurement(exitCode);

        return new(cause is null ? message : $"{message} {cause.Message}");
    }
}
