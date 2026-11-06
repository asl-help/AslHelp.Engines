using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a part of a <see cref="FlatObjectArray"/> that did not read.
/// </summary>
/// <remarks>
///     Each failure is a <see langword="file"/> record that stores its values and formats <see cref="ResultError.Message"/> on get,
///     because a search that tries every offset fails millions of reads and reads none of the messages.
/// </remarks>
internal abstract record ObjectArrayError : ResultError
{
    /// <summary>
    ///     Creates the error for a block of pointers that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="position">The position of the 1st pointer of the block.</param>
    /// <param name="length">The number of pointers in the block.</param>
    /// <param name="address">The address of the 1st pointer of the block.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError BlockUnreadable(Address array, int position, int length, Address address, ResultError cause)
    {
        return new BlockUnreadableError(array, position, length, address, cause);
    }

    /// <summary>
    ///     Creates the error for a count that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="address">The address of the count.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError CountUnreadable(Address array, Address address, ResultError cause)
    {
        return new HeaderUnreadableError("count", array, address, cause);
    }

    /// <summary>
    ///     Creates the error for a data pointer that did not read.
    /// </summary>
    /// <param name="array">The address of the array.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ObjectArrayError DataUnreadable(Address array, ResultError cause)
    {
        return new HeaderUnreadableError("data pointer", array, array, cause);
    }
}

/// <summary>
///     Represents a block of pointers that did not read.
/// </summary>
/// <param name="Array">The address of the array.</param>
/// <param name="Position">The position of the 1st pointer of the block.</param>
/// <param name="Length">The number of pointers in the block.</param>
/// <param name="Address">The address of the 1st pointer of the block.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record BlockUnreadableError(
    Address Array,
    int Position,
    int Length,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message
        => $"Can't read the {Length} pointers from position {Position} of the object array at {Array}: "
        + $"reading {Address} failed. {Cause.Message}";
}

/// <summary>
///     Represents a count or a data pointer that did not read.
/// </summary>
/// <param name="Part">The part in the message.</param>
/// <param name="Array">The address of the array.</param>
/// <param name="Address">The address of the failed read.</param>
/// <param name="Cause">The error of the read.</param>
file sealed record HeaderUnreadableError(
    string Part,
    Address Array,
    Address Address,
    ResultError Cause) : ObjectArrayError
{
    /// <inheritdoc/>
    public override string Message => $"Can't read the {Part} of the object array at {Array}: reading {Address} failed. {Cause.Message}";
}
