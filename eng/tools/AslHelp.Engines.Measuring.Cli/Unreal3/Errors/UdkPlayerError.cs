namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a UDK player whose process did not load or exited.
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
