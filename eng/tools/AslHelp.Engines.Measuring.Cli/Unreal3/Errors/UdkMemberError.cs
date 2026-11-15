namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a class or a member offset of a UDK player that was not found.
/// </summary>
internal sealed record UdkMemberError : ResultError
{
    private UdkMemberError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a class that does not exist.
    /// </summary>
    /// <param name="name">The name of the class.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkMemberError ClassNotFound(string name)
    {
        return new(UdkSR.FormatClassNotFound(name));
    }

    /// <summary>
    ///     Creates the error for a class offset that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkMemberError ClassOffsetNotFound()
    {
        return new(UdkSR.ClassOffsetNotFound);
    }

    /// <summary>
    ///     Creates the error for a name offset that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkMemberError NameOffsetNotFound()
    {
        return new(UdkSR.NameOffsetNotFound);
    }

    /// <summary>
    ///     Creates the error for an outer offset that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkMemberError OuterOffsetNotFound()
    {
        return new(UdkSR.OuterOffsetNotFound);
    }
}
