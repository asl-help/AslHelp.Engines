using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of a flat array of object pointers, which is how Unreal Engine 3 stores its objects.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the engine moves the data when the array grows.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="array">The address of the array, which is a data pointer and then a 4-byte count.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/> is <see langword="null"/>.
/// </exception>
public sealed class FlatObjectArray(
    IMemoryReader memory,
    Address array)
{
    private const int BlockLength = 1024;

    private readonly Address _array = array;
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    /// <summary>
    ///     Reads the count of the array.
    /// </summary>
    /// <returns>
    ///     The count or a failure with the array and the failed read.
    /// </returns>
    public Result<int> ReadCount()
    {
        var address = _array + _memory.Layout.PointerSize;

        return _memory.Read<int>(address).TryUnwrap(out int count, out var error)
            ? count
            : ObjectArrayError.CountUnreadable(_array, address, error);
    }

    /// <summary>
    ///     Enumerates the first pointers of the array in position order.
    /// </summary>
    /// <remarks>
    ///     Reads 1024 pointers at a time when the enumeration moves to them, so a caller can stop early.
    ///     Keeps <see cref="Address.Zero"/> for an empty position, so the position of a pointer stays the index of its object.
    ///     Ends at the 1st read that fails, with the failure as the last item, so the failure comes where the pointers stop.
    /// </remarks>
    /// <param name="count">The number of pointers to read.</param>
    /// <returns>
    ///     The first <paramref name="count"/> pointers or fewer when the array is shorter, then a failure if a read failed.
    /// </returns>
    public IEnumerable<Result<Address>> Read(int count)
    {
        if (!ReadCount().TryUnwrap(out int arrayCount, out var error))
        {
            yield return error;
            yield break;
        }

        int total = Math.Min(count, arrayCount);

        if (total <= 0)
        {
            yield break;
        }

        if (!_memory.ReadPointer(_array).TryUnwrap(out var data, out error))
        {
            yield return ObjectArrayError.DataUnreadable(_array, error);
            yield break;
        }

        int pointerSize = _memory.Layout.PointerSize;

        for (int position = 0; position < total; position += BlockLength)
        {
            int length = Math.Min(BlockLength, total - position);
            var start = data + ((long)position * pointerSize);

            if (pointerSize == sizeof(ulong))
            {
                if (!_memory.ReadArray<ulong>(start, length).TryUnwrap(out ulong[]? block, out error))
                {
                    yield return ObjectArrayError.BlockUnreadable(_array, position, length, start, error);
                    yield break;
                }

                foreach (ulong pointer in block)
                {
                    yield return new Address(pointer);
                }
            }
            else
            {
                if (!_memory.ReadArray<uint>(start, length).TryUnwrap(out uint[]? block, out error))
                {
                    yield return ObjectArrayError.BlockUnreadable(_array, position, length, start, error);
                    yield break;
                }

                foreach (uint pointer in block)
                {
                    yield return new Address(pointer);
                }
            }
        }
    }
}
