using AslHelp.Memory;

namespace AslHelp.Engines.Measuring.Cli.Unreal3.Errors;

/// <summary>
///     Represents a failed check of the fixture object of a UDK player.
/// </summary>
internal sealed record UdkFixtureError : ResultError
{
    private UdkFixtureError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a member that the fixture check reads through and that was not found.
    /// </summary>
    /// <param name="member">The report key of the member.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureCheckNeeds(string member)
    {
        return new(UdkSR.FormatFixtureCheckNeeds(member));
    }

    /// <summary>
    ///     Creates the error for a chain of fields of the fixture class that did not read.
    /// </summary>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureChildrenUnreadable(ResultError cause)
    {
        return new($"{UdkSR.FixtureChildrenUnreadable} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture class without the field and with a field whose name did not read.
    /// </summary>
    /// <param name="cause">The error of the 1st name that did not read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureFieldNameUnreadable(ResultError cause)
    {
        return new($"{UdkSR.FixtureFieldNameUnreadable} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture class without the field.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureFieldNotFound()
    {
        return new(UdkSR.FixtureFieldNotFound);
    }

    /// <summary>
    ///     Creates the error for a fixture class without a spawned object.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureInstanceNotFound()
    {
        return new(UdkSR.FixtureInstanceNotFound);
    }

    /// <summary>
    ///     Creates the error for an offset of the fixture field that did not read.
    /// </summary>
    /// <param name="address">The address of the offset.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureOffsetUnreadable(Address address, ResultError cause)
    {
        return new($"{UdkSR.FormatFixtureOffsetUnreadable(address)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture value that did not read.
    /// </summary>
    /// <param name="address">The address of the value.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureValueUnreadable(Address address, ResultError cause)
    {
        return new($"{UdkSR.FormatFixtureValueUnreadable(address)} {cause.Message}");
    }

    /// <summary>
    ///     Creates the error for a fixture value that is not the value the fixture game sets.
    /// </summary>
    /// <param name="expected">The value the fixture game sets.</param>
    /// <param name="actual">The value read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static UdkFixtureError FixtureValueWrong(int expected, int actual)
    {
        return new(UdkSR.FormatFixtureValueWrong(expected, actual));
    }
}
