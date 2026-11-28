using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Provides reads of 1 storage shape of the object array.
/// </summary>
public interface IObjectArray
{
    /// <summary>
    ///     Reads the count of the array.
    /// </summary>
    /// <returns>
    ///     The count or a failure with the array and either the failed read or the count outside 0 to 64M.
    /// </returns>
    Result<int> ReadCount();

    /// <summary>
    ///     Enumerates the first pointers of the array in position order.
    /// </summary>
    /// <remarks>
    ///     Reads the memory when the enumeration moves to it, so a caller can stop early.
    ///     Keeps <see cref="Address.Zero"/> for an empty position, so the position of a pointer stays the index of its object.
    ///     Gives a failure in place of each pointer that doesn't read and goes on, so 1 unreadable block or chunk doesn't hide the objects behind it.
    ///     Ends with a failure as the last item when the array itself doesn't read or a block of 1 allocation doesn't read at all.
    /// </remarks>
    /// <param name="count">The number of pointers to read.</param>
    /// <returns>
    ///     The first <paramref name="count"/> pointers or fewer when the array is shorter, with a failure in place of each pointer that doesn't read.
    ///     A count of the array outside 0 to 64M or past its chunks gives only the failure.
    /// </returns>
    IEnumerable<Result<Address>> Read(int count);

    /// <summary>
    ///     Reads the object and the serial number at position <paramref name="index"/>, which is how a weak pointer finds its object.
    /// </summary>
    /// <param name="index">The position, 0 or above.</param>
    /// <returns>
    ///     The item, an empty item for a position past the count or a failure when a read fails.
    ///     A shape that doesn't keep serial numbers returns <see langword="null"/> as the serial number.
    /// </returns>
    Result<ObjectItem> ReadItem(int index);
}
