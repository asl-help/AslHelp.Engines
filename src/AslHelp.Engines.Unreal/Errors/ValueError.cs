using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed read of the value of a property.
/// </summary>
internal abstract record ValueError : ResultError
{
    /// <summary>
    ///     Creates the error for a mask of a bool property that didn't read.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError BoolMaskUnreadable(string property, Address address, ResultError cause)
    {
        return new BoolMaskUnreadableError(property, address, cause);
    }

    /// <summary>
    ///     Creates the error for an enum property whose underlying property isn't a number, like another enum from a wrong offset.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="className">The class of the underlying property, like <c>EnumProperty</c>.</param>
    /// <param name="offset">The offset of the pointer to the underlying property in the profile.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError EnumUnderlyingNotNumber(string property, string className, int offset)
    {
        return new EnumUnderlyingNotNumberError(property, className, offset);
    }

    /// <summary>
    ///     Creates the error for an enum property whose pointer to its underlying property is 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="address">The address of the property.</param>
    /// <param name="offset">The offset of the pointer to the underlying property in the profile.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError EnumUnderlyingNull(string property, Address address, int offset)
    {
        return new EnumUnderlyingNullError(property, address, offset);
    }

    /// <summary>
    ///     Creates the error for a pointer to the underlying property of an enum property that didn't read.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError EnumUnderlyingUnreadable(string property, Address address, ResultError cause)
    {
        return new EnumUnderlyingUnreadableError(property, address, cause);
    }

    /// <summary>
    ///     Creates the error for a path that goes on past a property whose kind doesn't have members.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="className">The class of the property, like <c>IntProperty</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError NotContainer(string property, string className)
    {
        return new NotContainerError(property, className);
    }

    /// <summary>
    ///     Creates the error for an <c>FString</c> whose count is below 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="count">The count.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError StringCountNegative(string property, int count)
    {
        return new StringCountNegativeError(property, count);
    }

    /// <summary>
    ///     Creates the error for an <c>FString</c> with characters whose data pointer is 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="length">The number of characters without the terminator.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError StringDataNull(string property, int length)
    {
        return new StringDataNullError(property, length);
    }

    /// <summary>
    ///     Creates the error for an <c>FString</c> with more characters than a read takes.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="length">The number of characters without the terminator.</param>
    /// <param name="maxLength">The most characters a read takes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError StringTooLong(string property, int length, int maxLength)
    {
        return new StringTooLongError(property, length, maxLength);
    }

    /// <summary>
    ///     Creates the error for a struct property whose pointer to its struct type is 0.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="address">The address of the property.</param>
    /// <param name="offset">The offset of the pointer to the struct type in the profile.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError StructTypeNull(string property, Address address, int offset)
    {
        return new StructTypeNullError(property, address, offset);
    }

    /// <summary>
    ///     Creates the error for a pointer to the struct type of a struct property that didn't read.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError StructTypeUnreadable(string property, Address address, ResultError cause)
    {
        return new StructTypeUnreadableError(property, address, cause);
    }

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
    public static ValueError Unreadable(
        Address obj,
        string property,
        Address address,
        ResultError cause)
    {
        return new UnreadableError(obj, property, address, cause);
    }

    /// <summary>
    ///     Creates the error for a property of a kind the library doesn't read.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="className">The class of the property, like <c>TextProperty</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError Unsupported(string property, string className)
    {
        return new UnsupportedError(property, className);
    }

    /// <summary>
    ///     Creates the error for a weak pointer in a game whose object array doesn't keep a serial number per object.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError WeakSerialNumbersMissing(string property)
    {
        return new WeakSerialNumbersMissingError(property);
    }

    /// <summary>
    ///     Creates the error for a typed read of a property whose kind reads another type.
    /// </summary>
    /// <param name="property">The name of the property.</param>
    /// <param name="className">The class of the property, like <c>FloatProperty</c>.</param>
    /// <param name="readType">The name of the type the kind reads, like <c>Single</c>.</param>
    /// <param name="requestedType">The name of the type of the read, like <c>Int32</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ValueError WrongType(
        string property,
        string className,
        string readType,
        string requestedType)
    {
        return new WrongTypeError(property, className, readType, requestedType);
    }
}

file sealed record BoolMaskUnreadableError(
    string Property,
    Address Address,
    ResultError Cause) : ValueError
{
    public override string Message => SR.FormatValueBoolMaskUnreadable(Property, Address, Cause.Message);
}

file sealed record EnumUnderlyingNotNumberError(
    string Property,
    string ClassName,
    int Offset) : ValueError
{
    public override string Message
        => SR.FormatValueEnumUnderlyingNotNumber(Property, ClassName, Offset.ToString("X", CultureInfo.InvariantCulture));
}

file sealed record EnumUnderlyingNullError(
    string Property,
    Address Address,
    int Offset) : ValueError
{
    public override string Message => SR.FormatValueEnumUnderlyingNull(Property, Address, Offset.ToString("X", CultureInfo.InvariantCulture));
}

file sealed record EnumUnderlyingUnreadableError(
    string Property,
    Address Address,
    ResultError Cause) : ValueError
{
    public override string Message => SR.FormatValueEnumUnderlyingUnreadable(Property, Address, Cause.Message);
}

file sealed record NotContainerError(
    string Property,
    string ClassName) : ValueError
{
    public override string Message => SR.FormatValueNotContainer(Property, ClassName);
}

file sealed record StringCountNegativeError(
    string Property,
    int Count) : ValueError
{
    public override string Message => SR.FormatValueStringCountNegative(Property, Count);
}

file sealed record StringDataNullError(
    string Property,
    int Length) : ValueError
{
    public override string Message => SR.FormatValueStringDataNull(Property, Length);
}

file sealed record StringTooLongError(
    string Property,
    int Length,
    int MaxLength) : ValueError
{
    public override string Message => SR.FormatValueStringTooLong(Property, Length, MaxLength);
}

file sealed record StructTypeNullError(
    string Property,
    Address Address,
    int Offset) : ValueError
{
    public override string Message => SR.FormatValueStructTypeNull(Property, Address, Offset.ToString("X", CultureInfo.InvariantCulture));
}

file sealed record StructTypeUnreadableError(
    string Property,
    Address Address,
    ResultError Cause) : ValueError
{
    public override string Message => SR.FormatValueStructTypeUnreadable(Property, Address, Cause.Message);
}

file sealed record UnreadableError(
    Address Object,
    string Property,
    Address Address,
    ResultError Cause) : ValueError
{
    public override string Message => SR.FormatValueUnreadable(Object, Property, Address, Cause.Message);
}

file sealed record UnsupportedError(
    string Property,
    string ClassName) : ValueError
{
    public override string Message => SR.FormatValueUnsupported(Property, ClassName);
}

file sealed record WeakSerialNumbersMissingError(
    string Property) : ValueError
{
    public override string Message => SR.FormatValueWeakSerialNumbersMissing(Property);
}

file sealed record WrongTypeError(
    string Property,
    string ClassName,
    string ReadType,
    string RequestedType) : ValueError
{
    public override string Message => SR.FormatValueWrongType(Property, ClassName, ReadType, RequestedType);
}
