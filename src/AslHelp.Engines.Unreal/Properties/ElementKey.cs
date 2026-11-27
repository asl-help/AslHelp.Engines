using AslHelp.Engines.Unreal.Errors;
using System;
using System.Globalization;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the index of an array element or the key of a set or map element, as the script wrote it.
/// </summary>
/// <remarks>
///     Keeps the key converted to the key type of the last collection that read it, the slot it matched there
///     and its last failure, so a steady lookup doesn't convert or allocate.
///     Isn't thread-safe.
/// </remarks>
internal sealed class ElementKey
{
    private object? _collection;
    private object? _converted;
    private ResultError? _error;
    private ResultError? _notFoundError;
    private (int Count, ResultError Error)? _outOfRange;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ElementKey"/> class.
    /// </summary>
    /// <param name="value">The key, which is an integer, a <see cref="bool"/> or a <see cref="string"/>.</param>
    public ElementKey(object value)
    {
        Value = value;
        Text = value switch
        {
            string text => $"\"{text}\"",
            bool flag => flag ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        };
    }

    /// <summary>
    ///     Gets or sets the slot that the key matched last or -1. A lookup checks this slot 1st.
    /// </summary>
    public int SlotHint { get; set; } = -1;

    /// <summary>
    ///     Gets the text of the key as a script writes it, like <c>2</c> or <c>"two"</c>.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the key.
    /// </summary>
    public object Value { get; }

    /// <summary>
    ///     Checks whether <paramref name="value"/> can be a key, which is an integer, a <see cref="bool"/> or a <see cref="string"/>.
    /// </summary>
    /// <param name="value">The key.</param>
    /// <returns>
    ///     <see langword="true"/> for a key of 1 of those types.
    /// </returns>
    public static bool IsKeyType(object value)
    {
        return value is string or bool || IsInteger(Type.GetTypeCode(value.GetType()));
    }

    /// <summary>
    ///     Converts the key to <typeparamref name="TKey"/>, the key type of <paramref name="collection"/>.
    /// </summary>
    /// <remarks>
    ///     An integer converts to any integer type it fits in. An integer that doesn't fit fails as a key that isn't there.
    ///     Keeps the result for the collection, so the next call with the same collection only unboxes.
    /// </remarks>
    /// <typeparam name="TKey">The key type of the collection.</typeparam>
    /// <param name="collection">The collection, for the kept result.</param>
    /// <param name="property">The name of the collection, for the failure.</param>
    /// <returns>
    ///     The key or a failure when it can't be a <typeparamref name="TKey"/> or doesn't fit.
    /// </returns>
    public Result<TKey> As<TKey>(object collection, string property)
    {
        if (!ReferenceEquals(_collection, collection))
        {
            _collection = collection;
            _converted = ConvertTo<TKey>(out bool outOfRange);
            _notFoundError = null;
            _outOfRange = null;
            _error = _converted is not null
                ? null
                : outOfRange
                    ? NotFound(property)
                    : CollectionError.KeyTypeMismatch(property, typeof(TKey).Name, Text);
            SlotHint = -1;
        }

        if (_error is not null)
        {
            return _error;
        }

        return (TKey)_converted!;
    }

    /// <summary>
    ///     Returns the failure for a map that doesn't have the key.
    /// </summary>
    /// <param name="property">The name of the map.</param>
    /// <returns>
    ///     The failure, the same instance until another collection reads the key.
    /// </returns>
    public ResultError NotFound(string property)
    {
        return _notFoundError ??= CollectionError.KeyNotFound(property, Text);
    }

    /// <summary>
    ///     Returns the failure for an index outside the <paramref name="count"/> elements of an array.
    /// </summary>
    /// <param name="property">The name of the array.</param>
    /// <param name="index">The index, which is the key converted by <see cref="As{TKey}"/>.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>
    ///     The failure, the same instance while the count and the collection that reads the key stay the same.
    /// </returns>
    public ResultError OutOfRange(string property, int index, int count)
    {
        if (_outOfRange is not { } kept || kept.Count != count)
        {
            kept = (count, CollectionError.IndexOutOfRange(property, index, count));
            _outOfRange = kept;
        }

        return kept.Error;
    }

    /// <summary>
    ///     Returns the text of the key.
    /// </summary>
    /// <returns>
    ///     The text, like <c>2</c> or <c>"two"</c>.
    /// </returns>
    public override string ToString()
    {
        return Text;
    }

    private static bool IsInteger(TypeCode code)
    {
        return code is >= TypeCode.SByte and <= TypeCode.UInt64;
    }

    private object? ConvertTo<TKey>(out bool outOfRange)
    {
        outOfRange = false;

        if (Value is TKey)
        {
            return Value;
        }

        if (!IsInteger(Type.GetTypeCode(Value.GetType())) || !IsInteger(Type.GetTypeCode(typeof(TKey))))
        {
            return null;
        }

        // Catch here, because Convert only throws for an integer that doesn't fit.
        try
        {
            return Convert.ChangeType(Value, typeof(TKey), CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            outOfRange = true;

            return null;
        }
    }
}
