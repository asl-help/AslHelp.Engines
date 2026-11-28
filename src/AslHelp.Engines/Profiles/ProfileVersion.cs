using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AslHelp.Engines.Profiles;

/// <summary>
///     Represents an engine version the way a profile document or a game writes it, with the numbers that the engine reads from the text.
/// </summary>
/// <remarks>
///     The engine library picks the parts, so it decides the order. A part can stand for a letter too,
///     like a release type that the engine ranks with a number.
///     Versions compare part by part. A version that leaves parts off sorts below and doesn't equal a longer version,
///     but <see cref="ProfileCatalog{TProfile}.Select"/> treats it as its whole line.
/// </remarks>
public sealed class ProfileVersion : IComparable<ProfileVersion>, IEquatable<ProfileVersion>
{
    private readonly int[] _parts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProfileVersion"/> class.
    /// </summary>
    /// <param name="text">The text as written, like <c>1.2.0</c>.</param>
    /// <param name="parts">The numbers that the engine reads from <paramref name="text"/>, like 1, 2 and 0. The version keeps a copy.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="parts"/> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="text"/> or <paramref name="parts"/> is <see langword="null"/>.
    /// </exception>
    public ProfileVersion(string text, params int[] parts)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(parts);

        if (parts.Length == 0)
        {
            throw new ArgumentException(SR.VersionPartsEmpty, nameof(parts));
        }

        Text = text;
        _parts = [.. parts];
        Parts = new ReadOnlyCollection<int>(_parts);
    }

    /// <summary>
    ///     Gets the numbers that the engine read from <see cref="Text"/>.
    /// </summary>
    public IReadOnlyList<int> Parts { get; }

    /// <summary>
    ///     Gets the text as written.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Checks whether 2 versions have the same parts.
    /// </summary>
    /// <param name="left">The 1st version.</param>
    /// <param name="right">The 2nd version.</param>
    /// <returns>
    ///     <see langword="true"/> when both are <see langword="null"/> or have the same parts.
    /// </returns>
    public static bool operator ==(ProfileVersion? left, ProfileVersion? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    ///     Checks whether 2 versions have different parts.
    /// </summary>
    /// <param name="left">The 1st version.</param>
    /// <param name="right">The 2nd version.</param>
    /// <returns>
    ///     <see langword="true"/> when only 1 is <see langword="null"/> or the parts differ.
    /// </returns>
    public static bool operator !=(ProfileVersion? left, ProfileVersion? right)
    {
        return !(left == right);
    }

    /// <summary>
    ///     Checks whether <paramref name="left"/> sorts below <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The 1st version.</param>
    /// <param name="right">The 2nd version.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> sorts below <paramref name="right"/>, with <see langword="null"/> below any version.
    /// </returns>
    public static bool operator <(ProfileVersion? left, ProfileVersion? right)
    {
        return left is null ? right is not null : left.CompareTo(right) < 0;
    }

    /// <summary>
    ///     Checks whether <paramref name="left"/> sorts below or equal to <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The 1st version.</param>
    /// <param name="right">The 2nd version.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> doesn't sort above <paramref name="right"/>.
    /// </returns>
    public static bool operator <=(ProfileVersion? left, ProfileVersion? right)
    {
        return !(right < left);
    }

    /// <summary>
    ///     Checks whether <paramref name="left"/> sorts above <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The 1st version.</param>
    /// <param name="right">The 2nd version.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> sorts above <paramref name="right"/>.
    /// </returns>
    public static bool operator >(ProfileVersion? left, ProfileVersion? right)
    {
        return right < left;
    }

    /// <summary>
    ///     Checks whether <paramref name="left"/> sorts above or equal to <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The 1st version.</param>
    /// <param name="right">The 2nd version.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> doesn't sort below <paramref name="right"/>.
    /// </returns>
    public static bool operator >=(ProfileVersion? left, ProfileVersion? right)
    {
        return !(left < right);
    }

    /// <summary>
    ///     Compares the parts 1 by 1.
    /// </summary>
    /// <remarks>
    ///     A version that runs out of parts first sorts below, so <c>1.2</c> sorts below <c>1.2.0</c>.
    /// </remarks>
    /// <param name="other">The other version.</param>
    /// <returns>
    ///     A negative number, 0 or a positive number when this version is below, equal to or above <paramref name="other"/>.
    ///     Any version sorts above <see langword="null"/>.
    /// </returns>
    public int CompareTo(ProfileVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (int i = 0; i < _parts.Length && i < other._parts.Length; i++)
        {
            if (_parts[i] != other._parts[i])
            {
                return _parts[i].CompareTo(other._parts[i]);
            }
        }

        return _parts.Length.CompareTo(other._parts.Length);
    }

    /// <inheritdoc/>
    public bool Equals(ProfileVersion? other)
    {
        return other is not null && _parts.AsSpan().SequenceEqual(other._parts);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as ProfileVersion);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 17;

        foreach (int part in _parts)
        {
            hash = unchecked((hash * 31) + part);
        }

        return hash;
    }

    /// <summary>
    ///     Returns the text as written.
    /// </summary>
    /// <returns>
    ///     <see cref="Text"/>.
    /// </returns>
    public override string ToString()
    {
        return Text;
    }

    /// <summary>
    ///     Checks whether <paramref name="other"/> starts with the parts of this version and has more of them, like <c>1.2</c> for <c>1.2.3</c>.
    /// </summary>
    /// <param name="other">The longer version.</param>
    /// <returns>
    ///     <see langword="true"/> when this version is the line of <paramref name="other"/>.
    /// </returns>
    internal bool IsLineOf(ProfileVersion other)
    {
        return _parts.Length < other._parts.Length && other._parts.AsSpan(0, _parts.Length).SequenceEqual(_parts);
    }
}
