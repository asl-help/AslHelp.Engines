using System;
using System.Runtime.CompilerServices;

namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Identifies 1 cached result, such as an engine build, a player or a download, by its kind and a sha256 over
///     its inputs.
/// </summary>
/// <remarks>
///     <see cref="CacheKeyBuilder"/> makes keys from inputs that each have a name.
/// </remarks>
public sealed record CacheKey
{
    private const int HashLength = 64;
    private const int ShortHashLength = 16;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CacheKey"/> class.
    /// </summary>
    /// <param name="kind">The kind of result, in lowercase ascii letters, digits and '-'.</param>
    /// <param name="hash">The sha256 over the inputs, as 64 lowercase hex chars.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="kind"/> is empty or has a character outside lowercase ascii letters, digits and '-'.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="hash"/> is not 64 lowercase hex chars.</exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="kind"/> or <paramref name="hash"/> is <see langword="null"/>.
    /// </exception>
    public CacheKey(string kind, string hash)
    {
        ThrowIfNotName(kind);
        ThrowIfNotHash(hash);

        Kind = kind;
        Hash = hash;
    }

    /// <summary>
    ///     Gets the kind of result, in lowercase ascii letters, digits and '-'.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    ///     Gets the sha256 over the inputs, as 64 lowercase hex chars.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    ///     Gets the first 16 hex chars of <see cref="Hash"/>, the part that <see cref="ToString"/> puts after the kind.
    /// </summary>
    /// <remarks>
    ///     The part is used in file and folder names, so it stays short and in file-safe characters.
    /// </remarks>
    public string ShortHash => Hash[..ShortHashLength];

    /// <summary>
    ///     Formats the key as the kind, '-' and the first 16 hex chars of the hash, as in
    ///     <c>player-3f2a9c01b7d4e5f6</c>.
    /// </summary>
    /// <remarks>
    ///     The string is used as a file and folder name, so it stays short and in file-safe characters.
    /// </remarks>
    /// <returns>
    ///     The formatted key.
    /// </returns>
    public override string ToString()
    {
        return $"{Kind}-{ShortHash}";
    }

    /// <summary>
    ///     Throws when <paramref name="value"/> is not a valid kind or input name.
    /// </summary>
    /// <param name="value">The kind or input name to check.</param>
    /// <param name="paramName">The name of the parameter that <paramref name="value"/> came from.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="value"/> is empty or has a character outside lowercase ascii letters, digits and '-'.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    internal static void ThrowIfNotName(string value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        if (!IsName(value))
        {
            throw new ArgumentException(SR.Format(SR.InvalidKeyName, value), paramName);
        }
    }

    private static void ThrowIfNotHash(string hash, [CallerArgumentExpression(nameof(hash))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(hash, paramName);

        if (!IsHash(hash))
        {
            throw new ArgumentException(SR.InvalidHash, paramName);
        }
    }

    private static bool IsName(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHash(string value)
    {
        if (value.Length != HashLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (c is not ((>= 'a' and <= 'f') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return true;
    }
}
