using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the reads of 1 property whose value is a <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="property">The property, for the messages.</param>
internal abstract class ValueAccess<T>(
    UnrealProperty property) : PropertyAccess(property)
{
    // Keep the failure of the last type read as, because a watcher of the wrong type reads as the same type on every tick.
    private (Type Type, ValueError Error)? _wrongTypeError;

    /// <inheritdoc/>
    public sealed override TResult Accept<TResult>(AccessVisitor<TResult> visitor)
    {
        return visitor.VisitValue(this);
    }

    /// <summary>
    ///     Reads the value of this property in <paramref name="container"/> and compares it with <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    ///     A string read compares the characters without creating the text, so a key lookup doesn't allocate.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object, struct or element with the value.</param>
    /// <param name="value">The value to compare with.</param>
    /// <returns>
    ///     <see langword="true"/> when the value equals <paramref name="value"/> or a failure when the read fails.
    /// </returns>
    public virtual Result<bool> Matches(IMemoryReader memory, Address container, T value)
    {
        return Read(memory, container, value).TryUnwrap(out var read, out var error)
            ? EqualityComparer<T>.Default.Equals(read, value)
            : error;
    }

    /// <inheritdoc/>
    public sealed override ResultError NotValueOf(Type type)
    {
        if (_wrongTypeError is { } kept && kept.Type == type)
        {
            return kept.Error;
        }

        var error = ValueError.WrongType(Property.Name, Property.ClassName, typeof(T).Name, type.Name);
        _wrongTypeError = (type, error);

        return error;
    }

    /// <summary>
    ///     Reads the value of this property in <paramref name="container"/>.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the value.</param>
    /// <param name="last">The value of the last read or the default value. A string read returns it when the text is equal.</param>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public abstract Result<T> Read(IMemoryReader memory, Address container, T? last);

    /// <inheritdoc/>
    public override Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last)
    {
        var lastValue = last is T kept ? kept : default;

        if (!Read(memory, container, lastValue).TryUnwrap(out var value, out var error))
        {
            return error;
        }

        return last is T && EqualityComparer<T>.Default.Equals(value, lastValue!) ? Result.Ok<object?>(last) : Result.Ok<object?>(value);
    }

    /// <summary>
    ///     Reads the value of this property in each of <paramref name="values"/>.Length elements that sit <paramref name="stride"/> bytes apart.
    /// </summary>
    /// <remarks>
    ///     Reads 1 element at a time unless a kind overrides it, like a number kind that reads every element in 1 read.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="data">The address of the 1st element.</param>
    /// <param name="stride">The size of 1 element in bytes.</param>
    /// <param name="values">The buffer that gets the values.</param>
    /// <param name="last">The values of the last read or an empty span. A string read returns the last text when it's equal.</param>
    /// <returns>
    ///     A success or the failure of the 1st element that doesn't read.
    /// </returns>
    public virtual Result ReadMany(IMemoryReader memory, Address data, int stride, Span<T> values, ReadOnlySpan<T> last)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (!Read(memory, data + ((long)i * stride), i < last.Length ? last[i] : default).TryUnwrap(out var value, out var error))
            {
                return error;
            }

            values[i] = value;
        }

        return Result.Ok();
    }
}
