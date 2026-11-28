using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the largest values that a read takes, so a damaged count fails instead of reading megabytes on every tick.
/// </summary>
public sealed record ValueBounds
{
    /// <summary>
    ///     The largest bound, 1048576. A read sizes its buffers by the bounds, so a larger bound could overflow a size.
    /// </summary>
    public const int Limit = 1 << 20;

    /// <summary>
    ///     Gets the bounds that attach takes when the caller doesn't pass any: 512 characters and 1024 elements.
    /// </summary>
    public static ValueBounds Default { get; } = new();

    /// <summary>
    ///     Gets the most characters of an <c>FString</c> that a read takes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The value is below 1 or above <see cref="Limit"/>.
    /// </exception>
    public int MaxChars
    {
        get;
        init => field = value is >= 1 and <= Limit ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null);
    } = 512;

    /// <summary>
    ///     Gets the most elements or slots of a collection that a whole read or a key lookup takes.
    /// </summary>
    /// <remarks>
    ///     1 element by index only checks the index against the count.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The value is below 1 or above <see cref="Limit"/>.
    /// </exception>
    public int MaxElements
    {
        get;
        init => field = value is >= 1 and <= Limit ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null);
    } = 1024;
}
