using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Globalization;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides the read of the name id and the name number and the join of a full name, which objects and properties share.
/// </summary>
internal static class NameMembers
{
    /// <summary>
    ///     Joins a base name and a name number into the name the engine prints.
    /// </summary>
    /// <remarks>
    ///     Returns <paramref name="name"/> itself for number 0, because most names have no number and the cached text needs no copy.
    ///     Appends <c>_</c> and the number minus 1 for a number above 0, because the engine stores the printed number plus 1.
    /// </remarks>
    /// <param name="name">The base name.</param>
    /// <param name="number">The name number, 0 or above.</param>
    /// <returns>
    ///     The full name.
    /// </returns>
    public static string Join(string name, int number)
    {
        return number == 0
            ? name
            : string.Concat(name, "_", (number - 1).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Reads the 4-byte name id and the 4-byte name number after it in 1 read.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="address">The address of the name id.</param>
    /// <returns>
    ///     The id and the number or the failed read.
    /// </returns>
    public static Result<(int Id, int Number)> Read(IMemoryReader memory, Address address)
    {
        Span<byte> bytes = stackalloc byte[2 * sizeof(int)];

        if (memory.Read(address, bytes).TryUnwrapErr(out var error))
        {
            return error;
        }

        var numberBytes = bytes.Slice(sizeof(int));

        return memory.Layout.ByteOrder == ByteOrder.LittleEndian
            ? (BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadInt32LittleEndian(numberBytes))
            : (BinaryPrimitives.ReadInt32BigEndian(bytes), BinaryPrimitives.ReadInt32BigEndian(numberBytes));
    }
}
