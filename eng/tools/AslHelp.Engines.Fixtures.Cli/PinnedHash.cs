using System;
using System.Security.Cryptography;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Represents the sha1 hash a downloaded file must have, as its lowercase hex digest.
/// </summary>
internal sealed record PinnedHash
{
    /// <summary>
    ///     Sizes a sha1 digest at 40 hex chars.
    /// </summary>
    private const int Sha1HexLength = 40;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PinnedHash"/> class from a digest that <see cref="Sha1"/> checked.
    /// </summary>
    /// <param name="hex">The digest as 40 lowercase hex chars.</param>
    private PinnedHash(string hex)
    {
        Hex = hex;
    }

    /// <summary>
    ///     Gets the algorithm of every pin, <see cref="HashAlgorithmName.SHA1"/>.
    /// </summary>
    public static HashAlgorithmName Algorithm => HashAlgorithmName.SHA1;

    /// <summary>
    ///     Gets the digest as 40 lowercase hex chars.
    /// </summary>
    public string Hex { get; }

    /// <summary>
    ///     Creates the pin of a sha1 digest written as 40 lowercase hex chars.
    /// </summary>
    /// <param name="hex">The digest to pin.</param>
    /// <returns>
    ///     The pin.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="hex"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="hex"/> is not 40 chars long or has a char outside lowercase hex.</exception>
    public static PinnedHash Sha1(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        return IsSha1Hex(hex) ? new PinnedHash(hex) : throw new FormatException(SR.FormatPinnedHashInvalid(hex));
    }

    /// <summary>
    ///     Formats the pin as it shows in messages, such as <c>sha1:a9993e364706816aba3e25717850c26c9cd0d89d</c>.
    /// </summary>
    /// <returns>
    ///     <c>sha1:</c> and the hex digest.
    /// </returns>
    public override string ToString()
    {
        return $"sha1:{Hex}";
    }

    /// <summary>
    ///     Checks whether <paramref name="value"/> is a sha1 digest in lowercase hex chars.
    /// </summary>
    /// <param name="value">The text to check.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="value"/> is 40 chars long and every char is in [0-9a-f].
    /// </returns>
    private static bool IsSha1Hex(string value)
    {
        if (value.Length != Sha1HexLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigitLower(c))
            {
                return false;
            }
        }

        return true;
    }
}
