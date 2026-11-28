using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Reads consecutive elements into a kept buffer and returns the last array when the elements didn't change.
/// </summary>
/// <remarks>
///     Isn't thread-safe.
/// </remarks>
/// <typeparam name="T">The type of an element.</typeparam>
internal sealed class ElementBuffer<T>
{
    private T[] _values = [];

    /// <summary>
    ///     Reads <paramref name="count"/> elements that sit <paramref name="stride"/> bytes apart.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="element">The read of 1 element.</param>
    /// <param name="data">The address of the 1st element.</param>
    /// <param name="count">The number of elements.</param>
    /// <param name="stride">The size of 1 element in bytes.</param>
    /// <param name="last">The array of the last read or <see langword="null"/>.</param>
    /// <returns>
    ///     <paramref name="last"/> when every element equals it, otherwise a new array or the failure of an element.
    /// </returns>
    public Result<T[]> Read(IMemoryReader memory, ValueAccess<T> element, Address data, int count, int stride, T[]? last)
    {
        if (_values.Length < count)
        {
            _values = new T[Math.Max(count, (int)Math.Min(_values.Length * 2L, int.MaxValue))];
        }

        var values = _values.AsSpan(0, count);

        if (element.ReadMany(memory, data, stride, values, last).TryUnwrapErr(out var error))
        {
            return error;
        }

        return last is not null && Equal(values, last) ? last : values.ToArray();
    }

    private static bool Equal(ReadOnlySpan<T> values, T[] last)
    {
        if (values.Length != last.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;

        for (int i = 0; i < values.Length; i++)
        {
            if (!comparer.Equals(values[i], last[i]))
            {
                return false;
            }
        }

        return true;
    }
}
