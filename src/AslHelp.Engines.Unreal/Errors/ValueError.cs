using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed read of the value of a property.
/// </summary>
internal abstract record ValueError : ResultError
{
    /// <summary>
    ///     Creates the error for a value whose read failed.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="property">The name of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError Unreadable(Address obj, string property, Address address, ResultError cause)
    {
        return new UnreadableError(obj, property, address, cause);
    }

    /// <summary>
    ///     Creates the error for a typed read of a property of another kind.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="className">The class of the property, like <c>FloatProperty</c>.</param>
    /// <param name="expected">The class that the read needs, like <c>IntProperty</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError WrongKind(string property, string className, string expected)
    {
        return new WrongKindError(property, className, expected);
    }
}

file sealed record UnreadableError(
    Address Object,
    string Property,
    Address Address,
    ResultError Cause) : ValueError
{
    public override string Message => SR.FormatValueUnreadable(Object, Property, Address, Cause.Message);
}

file sealed record WrongKindError(
    string Property,
    string ClassName,
    string Expected) : ValueError
{
    public override string Message => SR.FormatValueWrongKind(Property, ClassName, Expected);
}
