namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a name array or an object array of a UDK player that was not found or did not read.
/// </summary>
internal sealed record UdkArrayError : ResultError
{
    private UdkArrayError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for 2 name arrays with different data that both fit.
    /// </summary>
    /// <param name="firstRva">The RVA of the 1st array in hex.</param>
    /// <param name="otherRva">The RVA of the other array in hex.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError NameArrayAmbiguous(string firstRva, string otherRva)
    {
        return new(UdkSR.FormatNameArrayAmbiguous(firstRva, otherRva));
    }

    /// <summary>
    ///     Creates the error for a first name of the name array that did not read.
    /// </summary>
    /// <param name="rva">The RVA of the array in hex.</param>
    /// <param name="id">The position of the name.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError NameArrayNameUnreadable(string rva, int id, ResultError cause)
    {
        return new($"{UdkSR.FormatNameArrayNameUnreadable(rva, id)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a name array that was not found.
    /// </summary>
    /// <param name="first">The 1st name the search starts from.</param>
    /// <param name="second">The 2nd name the search starts from.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError NameArrayNotFound(string first, string second)
    {
        return new(UdkSR.FormatNameArrayNotFound(first, second));
    }

    /// <summary>
    ///     Creates the error for a name array with another name at a position of the first names.
    /// </summary>
    /// <param name="rva">The RVA of the array in hex.</param>
    /// <param name="actual">The name read.</param>
    /// <param name="id">The position of the name.</param>
    /// <param name="expected">The first name at that position.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError NameArrayWrongName(string rva, string actual, int id, string expected)
    {
        return new(UdkSR.FormatNameArrayWrongName(rva, actual, id, expected));
    }

    /// <summary>
    ///     Creates the error for 2 object arrays with different data and the same count that both fit.
    /// </summary>
    /// <param name="firstRva">The RVA of the 1st array in hex.</param>
    /// <param name="otherRva">The RVA of the other array in hex.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError ObjectArrayAmbiguous(string firstRva, string otherRva)
    {
        return new(UdkSR.FormatObjectArrayAmbiguous(firstRva, otherRva));
    }

    /// <summary>
    ///     Creates the error for an object array that was not found.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError ObjectArrayNotFound()
    {
        return new(UdkSR.ObjectArrayNotFound);
    }

    /// <summary>
    ///     Creates the error for an object array that did not read to its end after the search found it.
    /// </summary>
    /// <param name="rva">The RVA of the array in hex.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkArrayError ObjectArrayUnreadable(string rva, ResultError cause)
    {
        return new($"{UdkSR.FormatObjectArrayUnreadable(rva)} {cause.Message}");
    }
}
