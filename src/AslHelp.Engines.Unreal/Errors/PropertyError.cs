using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a member that did not read from a property.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record PropertyError : ResultError
{
    /// <summary>
    ///     Creates the error for an array size that did not read.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="offset">The offset of the member in the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ArrayDimUnreadable(Address property, Address address, int offset, ResultError cause)
    {
        return new ArrayDimUnreadableError(property, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a name of a field class that did not read.
    /// </summary>
    /// <remarks>
    ///     Prints the name id address with "from", because the number or the name table can fail too and the cause shows which read failed.
    /// </remarks>
    /// <param name="property">The address of the property.</param>
    /// <param name="klass">The address of the field class.</param>
    /// <param name="address">The address of the name id.</param>
    /// <param name="offset">The offset of the name id in the field class.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ClassNameUnreadable(Address property, Address klass, Address address, int offset, ResultError cause)
    {
        return new ClassNameUnreadableError(property, klass, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a pointer to the field class that is 0.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="offset">The offset of the pointer in the property.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ClassNull(Address property, int offset)
    {
        return new ClassNullError(property, offset);
    }

    /// <summary>
    ///     Creates the error for a pointer to the field class that did not read.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ClassUnreadable(Address property, Address address, int offset, ResultError cause)
    {
        return new ClassUnreadableError(property, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for an element size that did not read.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="offset">The offset of the member in the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ElementSizeUnreadable(Address property, Address address, int offset, ResultError cause)
    {
        return new ElementSizeUnreadableError(property, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a field on the chain whose class pointer is 0.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="position">The position on the chain, with the 1st field at 1.</param>
    /// <param name="offset">The offset of the pointer in the field.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError FieldClassNull(Address type, int position, int offset)
    {
        return new FieldClassNullError(type, position, offset);
    }

    /// <summary>
    ///     Creates the error for a field on the chain whose class or super chain did not read.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="position">The position on the chain, with the 1st field at 1.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError FieldClassUnreadable(Address type, int position, ResultError cause)
    {
        return new FieldClassUnreadableError(type, position, cause);
    }

    /// <summary>
    ///     Creates the error for a property whose class or class name did not read.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError KindUnreadable(Address property, ResultError cause)
    {
        return new KindUnreadableError(property, cause);
    }

    /// <summary>
    ///     Creates the error for a property chain that goes on past the limit.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="limit">The number of properties that the chain stops after.</param>
    /// <param name="offset">The offset of the next pointer in a property.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ListPastLimit(Address type, int limit, int offset)
    {
        return new ListPastLimitError(type, limit, offset);
    }

    /// <summary>
    ///     Creates the error for a pointer to the 1st property that did not read.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the struct.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError ListUnreadable(Address type, Address address, int offset, ResultError cause)
    {
        return new ListUnreadableError(type, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a name number below 0.
    /// </summary>
    /// <param name="address">The address of the name id.</param>
    /// <param name="number">The number read behind the name id.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError NameNumberNegative(Address address, int number)
    {
        return new NameNumberNegativeError(address, number);
    }

    /// <summary>
    ///     Creates the error for a name of a property that did not read.
    /// </summary>
    /// <remarks>
    ///     Prints the name id address with "from", because the number or the name table can fail too and the cause shows which read failed.
    /// </remarks>
    /// <param name="property">The address of the property.</param>
    /// <param name="address">The address of the name id.</param>
    /// <param name="offset">The offset of the name id in the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError NameUnreadable(Address property, Address address, int offset, ResultError cause)
    {
        return new NameUnreadableError(property, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a next pointer that did not read.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError NextUnreadable(Address property, Address address, int offset, ResultError cause)
    {
        return new NextUnreadableError(property, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for an offset that did not read.
    /// </summary>
    /// <param name="property">The address of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="offset">The offset of the member in the property.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PropertyError OffsetUnreadable(Address property, Address address, int offset, ResultError cause)
    {
        return new OffsetUnreadableError(property, address, offset, cause);
    }
}

/// <summary>
///     Represents an array size that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ArrayDimUnreadableError(
    Address Property,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyArrayDimUnreadable(Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a name of a field class that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Class">The address of the field class.</param>
/// <param name="Address">The address of the name id.</param>
/// <param name="Offset">The offset of the name id in the field class.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ClassNameUnreadableError(
    Address Property,
    Address Class,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyClassNameUnreadable(Class, Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a pointer to the field class that is 0.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Offset">The offset of the pointer in the property.</param>
file sealed record ClassNullError(
    Address Property,
    int Offset) : PropertyError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPropertyClassNull(Property, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a pointer to the field class that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Address">The address of the pointer.</param>
/// <param name="Offset">The offset of the pointer in the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ClassUnreadableError(
    Address Property,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyClassUnreadable(Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents an element size that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ElementSizeUnreadableError(
    Address Property,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyElementSizeUnreadable(Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a field on the chain whose class pointer is 0.
/// </summary>
/// <param name="Type">The address of the struct.</param>
/// <param name="Position">The position on the chain, with the 1st field at 1.</param>
/// <param name="Offset">The offset of the pointer in the field.</param>
file sealed record FieldClassNullError(
    Address Type,
    int Position,
    int Offset) : PropertyError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPropertyFieldClassNull(Position, Type, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a field on the chain whose class or super chain did not read.
/// </summary>
/// <param name="Type">The address of the struct.</param>
/// <param name="Position">The position on the chain, with the 1st field at 1.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record FieldClassUnreadableError(
    Address Type,
    int Position,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPropertyFieldClassUnreadable(Position, Type, Cause.Message);
}

/// <summary>
///     Represents a property whose class or class name did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record KindUnreadableError(
    Address Property,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPropertyKindUnreadable(Property, Cause.Message);
}

/// <summary>
///     Represents a property chain that goes on past the limit.
/// </summary>
/// <param name="Type">The address of the struct.</param>
/// <param name="Limit">The number of properties that the chain stops after.</param>
/// <param name="Offset">The offset of the next pointer in a property.</param>
file sealed record ListPastLimitError(
    Address Type,
    int Limit,
    int Offset) : PropertyError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPropertyListPastLimit(Type, Limit, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a pointer to the 1st property that did not read.
/// </summary>
/// <param name="Type">The address of the struct.</param>
/// <param name="Address">The address of the pointer.</param>
/// <param name="Offset">The offset of the pointer in the struct.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ListUnreadableError(
    Address Type,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyListUnreadable(Type, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a name number below 0.
/// </summary>
/// <param name="Address">The address of the name id.</param>
/// <param name="Number">The number read behind the name id.</param>
file sealed record NameNumberNegativeError(
    Address Address,
    int Number) : PropertyError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPropertyNameNumberNegative(Address, Number);
}

/// <summary>
///     Represents a name of a property that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Address">The address of the name id.</param>
/// <param name="Offset">The offset of the name id in the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record NameUnreadableError(
    Address Property,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyNameUnreadable(Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a next pointer that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Address">The address of the pointer.</param>
/// <param name="Offset">The offset of the pointer in the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record NextUnreadableError(
    Address Property,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyNextUnreadable(Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents an offset that did not read.
/// </summary>
/// <param name="Property">The address of the property.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the property.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record OffsetUnreadableError(
    Address Property,
    Address Address,
    int Offset,
    ResultError Cause) : PropertyError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatPropertyOffsetUnreadable(Property, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}
