using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a member that did not read from a struct or a field.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record StructError : ResultError
{
    /// <summary>
    ///     Creates the error for a struct on the super chain whose name or super pointer did not read.
    /// </summary>
    /// <param name="type">The address of the struct the chain starts from.</param>
    /// <param name="level">The position on the chain, with the struct itself at 1.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static StructError ChainUnreadable(Address type, int level, ResultError cause)
    {
        return new ChainUnreadableError(type, level, cause);
    }

    /// <summary>
    ///     Creates the error for a children pointer that did not read.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the struct.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static StructError ChildrenUnreadable(Address type, Address address, int offset, ResultError cause)
    {
        return new ChildrenUnreadableError(type, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a field on the chain whose outer pointer did not read.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="position">The position on the chain, with the 1st field at 1.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static StructError FieldUnreadable(Address type, int position, ResultError cause)
    {
        return new FieldUnreadableError(type, position, cause);
    }

    /// <summary>
    ///     Creates the error for a next pointer that did not read.
    /// </summary>
    /// <param name="field">The address of the field.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the field.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static StructError NextUnreadable(Address field, Address address, int offset, ResultError cause)
    {
        return new NextUnreadableError(field, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a super pointer that did not read.
    /// </summary>
    /// <param name="type">The address of the struct.</param>
    /// <param name="address">The address of the pointer.</param>
    /// <param name="offset">The offset of the pointer in the struct.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static StructError SuperUnreadable(Address type, Address address, int offset, ResultError cause)
    {
        return new SuperUnreadableError(type, address, offset, cause);
    }
}

/// <summary>
///     Represents a struct on the super chain whose name or super pointer did not read.
/// </summary>
/// <param name="Type">The address of the struct the chain starts from.</param>
/// <param name="Level">The position on the chain, with the struct itself at 1.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ChainUnreadableError(
    Address Type,
    int Level,
    ResultError Cause) : StructError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatStructChainUnreadable(Type, Level, Cause.Message);
}

/// <summary>
///     Represents a field on the chain whose outer pointer did not read.
/// </summary>
/// <param name="Type">The address of the struct.</param>
/// <param name="Position">The position on the chain, with the 1st field at 1.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record FieldUnreadableError(
    Address Type,
    int Position,
    ResultError Cause) : StructError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatStructFieldUnreadable(Position, Type, Cause.Message);
}

/// <summary>
///     Represents a children pointer that did not read.
/// </summary>
/// <param name="Owner">The address the member belongs to.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record ChildrenUnreadableError(
    Address Owner,
    Address Address,
    int Offset,
    ResultError Cause) : StructError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatStructChildrenUnreadable(Owner, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a next pointer that did not read.
/// </summary>
/// <param name="Owner">The address the member belongs to.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record NextUnreadableError(
    Address Owner,
    Address Address,
    int Offset,
    ResultError Cause) : StructError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatStructFieldNextUnreadable(Owner, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a super pointer that did not read.
/// </summary>
/// <param name="Owner">The address the member belongs to.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record SuperUnreadableError(
    Address Owner,
    Address Address,
    int Offset,
    ResultError Cause) : StructError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatStructSuperUnreadable(Owner, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}
