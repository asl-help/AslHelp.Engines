using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a member that did not read from an object.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record ObjectError : ResultError
{
    /// <summary>
    ///     Creates the error for a class pointer that did not read.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the object.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectError ClassUnreadable(Address obj, Address address, int offset, ResultError cause)
    {
        return new ClassUnreadableError(obj, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a name id that did not read from the name table.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="id">The name id read from the object.</param>
    /// <param name="offset">The offset of the name id in the object.</param>
    /// <param name="cause">The error of the name table.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectError NameNotFound(Address obj, int id, int offset, ResultError cause)
    {
        return new NameNotFoundError(obj, id, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a name id or a name number that did not read.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="offset">The offset of the name id in the object.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectError NameUnreadable(Address obj, Address address, int offset, ResultError cause)
    {
        return new NameUnreadableError(obj, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a name number below 0.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="number">The number read from the object.</param>
    /// <param name="offset">The offset of the name id in the object.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectError NegativeNumber(Address obj, int number, int offset)
    {
        return new NegativeNumberError(obj, number, offset);
    }

    /// <summary>
    ///     Creates the error for an object pointer of 0.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectError NullObject()
    {
        return new NullObjectError();
    }

    /// <summary>
    ///     Creates the error for an outer pointer that did not read.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the object.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectError OuterUnreadable(Address obj, Address address, int offset, ResultError cause)
    {
        return new OuterUnreadableError(obj, address, offset, cause);
    }
}

/// <summary>
///     Represents a class pointer that did not read.
/// </summary>
/// <param name="Owner">The address the member belongs to.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ClassUnreadableError(
    Address Owner,
    Address Address,
    int Offset,
    ResultError Cause) : ObjectError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectClassUnreadable(Owner, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a name id or a name number that did not read.
/// </summary>
/// <param name="Owner">The address the member belongs to.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record NameUnreadableError(
    Address Owner,
    Address Address,
    int Offset,
    ResultError Cause) : ObjectError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectNameUnreadable(Owner, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents an outer pointer that did not read.
/// </summary>
/// <param name="Owner">The address the member belongs to.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record OuterUnreadableError(
    Address Owner,
    Address Address,
    int Offset,
    ResultError Cause) : ObjectError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectOuterUnreadable(Owner, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a name id that did not read from the name table.
/// </summary>
/// <param name="Object">The address of the object.</param>
/// <param name="Id">The name id read from the object.</param>
/// <param name="Offset">The offset of the name id in the object.</param>
/// <param name="Cause">The error of the name table.</param>
file sealed record NameNotFoundError(
    Address Object,
    int Id,
    int Offset,
    ResultError Cause) : ObjectError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectNameNotFound(Object, Id, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a name number below 0.
/// </summary>
/// <param name="Object">The address of the object.</param>
/// <param name="Number">The number read from the object.</param>
/// <param name="Offset">The offset of the name id in the object.</param>
file sealed record NegativeNumberError(
    Address Object,
    int Number,
    int Offset) : ObjectError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatObjectNameNumberNegative(Object, Number, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents an object pointer of 0.
/// </summary>
file sealed record NullObjectError : ObjectError
{
    /// <inheritdoc/>
    public override string Message => SR.ObjectNull;
}
