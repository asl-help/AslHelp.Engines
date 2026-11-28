using System;
using System.Globalization;

namespace AslHelp.Engines;

/// <summary>
///     Represents the index or key of an item in a collection, like <c>2</c> or <c>"two"</c>.
/// </summary>
/// <remarks>
///     The value is a <see cref="long"/> for an integer, a <see cref="ulong"/> for an integer past <see cref="long.MaxValue"/>,
///     a <see cref="string"/> or a <see cref="bool"/>. The default key is the index 0.
/// </remarks>
public readonly struct PathKey : IEquatable<PathKey>
{
    private static readonly object _zero = 0L;

    private readonly object? _value;

    private PathKey(object value)
    {
        _value = value;
    }

    /// <summary>
    ///     Gets the index or key.
    /// </summary>
    public object Value => _value ?? _zero;

    /// <summary>
    ///     Creates the key of an index.
    /// </summary>
    /// <param name="index">The index.</param>
    public static implicit operator PathKey(long index)
    {
        return new PathKey(index);
    }

    /// <summary>
    ///     Creates the key of a string.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="key"/> is <see langword="null"/>.
    /// </exception>
    public static implicit operator PathKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new PathKey(key);
    }

    /// <summary>
    ///     Creates the key of a <see cref="bool"/>.
    /// </summary>
    /// <param name="key">The key.</param>
    public static implicit operator PathKey(bool key)
    {
        return new PathKey(key);
    }

    /// <summary>
    ///     Compares 2 keys by their values.
    /// </summary>
    /// <param name="left">The 1st key.</param>
    /// <param name="right">The 2nd key.</param>
    /// <returns>
    ///     <see langword="true"/> when the keys are equal.
    /// </returns>
    public static bool operator ==(PathKey left, PathKey right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Compares 2 keys by their values.
    /// </summary>
    /// <param name="left">The 1st key.</param>
    /// <param name="right">The 2nd key.</param>
    /// <returns>
    ///     <see langword="true"/> when the keys differ.
    /// </returns>
    public static bool operator !=(PathKey left, PathKey right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Creates the key of <paramref name="value"/>, like an index or key that a script wrote.
    /// </summary>
    /// <param name="value">The index or key, an integer of any size, a <see cref="string"/> or a <see cref="bool"/>.</param>
    /// <param name="key">The key or the default key.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="value"/> can be a key.
    /// </returns>
    public static bool TryCreate(object? value, out PathKey key)
    {
        key = value switch
        {
            string text => text,
            bool flag => flag,
            sbyte number => number,
            byte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number <= long.MaxValue ? (long)number : new PathKey(number),
            _ => default,
        };

        return key._value is not null;
    }

    /// <inheritdoc/>
    public bool Equals(PathKey other)
    {
        return Value.Equals(other.Value);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is PathKey other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    /// <summary>
    ///     Returns the key as it's written.
    /// </summary>
    /// <remarks>
    ///     A string key escapes <c>\</c> and <c>"</c> with a <c>\</c>, so the text of a path shows 1 key per pair of brackets.
    /// </remarks>
    /// <returns>
    ///     The text, like <c>2</c>, <c>"two"</c> or <c>true</c>.
    /// </returns>
    public override string ToString()
    {
        return Value switch
        {
            string text => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            bool flag => flag ? "true" : "false",
            var number => Convert.ToString(number, CultureInfo.InvariantCulture)!,
        };
    }
}
