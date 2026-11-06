using AslHelp.Memory;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a name that did not read from a <see cref="FlatNameTable"/>.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record NameTableError : ResultError
{
    /// <summary>
    ///     Creates the error for a null entry pointer.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryNull(int id)
    {
        return new EntryNullError(id);
    }

    /// <summary>
    ///     Creates the error for an entry pointer that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="address">The address of the entry pointer.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError EntryPointerUnreadable(int id, Address address, ResultError cause)
    {
        return new EntryPointerUnreadableError(id, address, cause);
    }

    /// <summary>
    ///     Creates the error for a data pointer or a count of the array that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="address">The address of the failed read.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError HeaderUnreadable(Address array, Address address, ResultError cause)
    {
        return new HeaderUnreadableError(array, address, cause);
    }

    /// <summary>
    ///     Creates the error for an id below 0.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IdNegative(int id)
    {
        return new IdNegativeError(id);
    }

    /// <summary>
    ///     Creates the error for an id at or past the count of the array.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="count">The count of the array.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IdOutOfRange(int id, int count)
    {
        return new IdOutOfRangeError(id, count);
    }

    /// <summary>
    ///     Creates the error for an entry whose index is the index of another id.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="index">The index read from the entry.</param>
    /// <param name="offset">The offset of the index in the entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IndexMismatch(int id, Address entry, int index, int offset)
    {
        return new IndexMismatchError(id, entry, index, offset);
    }

    /// <summary>
    ///     Creates the error for an index that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="address">The address of the index.</param>
    /// <param name="offset">The offset of the index in the entry.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError IndexUnreadable(int id, Address entry, Address address, int offset, ResultError cause)
    {
        return new EntryIndexUnreadableError(id, entry, address, offset, cause);
    }

    /// <summary>
    ///     Creates the error for a text that did not read.
    /// </summary>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="address">The address of the text.</param>
    /// <param name="offset">The offset of the text in the entry.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static NameTableError TextUnreadable(int id, Address entry, Address address, int offset, ResultError cause)
    {
        return new EntryTextUnreadableError(id, entry, address, offset, cause);
    }
}

/// <summary>
///     Represents an index of a name entry that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the entry.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryIndexUnreadableError(
    int Id,
    Address Entry,
    Address Address,
    int Offset,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatNameEntryIndexUnreadable(Id, Entry, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents the text of a name entry that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Offset">The offset of the member in the entry.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryTextUnreadableError(
    int Id,
    Address Entry,
    Address Address,
    int Offset,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatNameEntryTextUnreadable(Id, Entry, Address, Cause.Message, Offset.ToString("X", CultureInfo.InvariantCulture));
}

/// <summary>
///     Represents a null entry pointer.
/// </summary>
/// <param name="Id">The id of the name.</param>
file sealed record EntryNullError(
    int Id) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryNull(Id);
}

/// <summary>
///     Represents an entry pointer that did not read.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Address">The address of the entry pointer.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record EntryPointerUnreadableError(
    int Id,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameEntryPointerUnreadable(Id, Address, Cause.Message);
}

/// <summary>
///     Represents a data pointer or a count of the array that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record HeaderUnreadableError(
    Address Array,
    Address Address,
    ResultError Cause) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameArrayHeaderUnreadable(Array, Address, Cause.Message);
}

/// <summary>
///     Represents an id below 0.
/// </summary>
/// <param name="Id">The id of the name.</param>
file sealed record IdNegativeError(
    int Id) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameIdNegative(Id);
}

/// <summary>
///     Represents an id at or past the count of the array.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Count">The count of the array.</param>
file sealed record IdOutOfRangeError(
    int Id,
    int Count) : NameTableError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatNameIdOutOfRange(Id, Count);
}

/// <summary>
///     Represents an entry whose index is the index of another id.
/// </summary>
/// <param name="Id">The id of the name.</param>
/// <param name="Entry">The address of the entry.</param>
/// <param name="Index">The index read from the entry.</param>
/// <param name="Offset">The offset of the index in the entry.</param>
file sealed record IndexMismatchError(
    int Id,
    Address Entry,
    int Index,
    int Offset) : NameTableError
{
    /// <inheritdoc/>
    public override string Message
        => SR.FormatNameEntryIndexMismatch(Id, Entry, Index, Index >> 1, Offset.ToString("X", CultureInfo.InvariantCulture));
}
