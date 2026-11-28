using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AslHelp.Engines;

/// <summary>
///     Represents the value of a struct as its members by name, like <c>X</c>, <c>Y</c> and <c>Z</c> of a vector.
/// </summary>
/// <remarks>
///     Looks a member up by its name, compared ordinally.
///     2 values are equal when they have the same type name and the same members with equal values,
///     so a watcher only reports a change when a member changes.
///     Member values compare with <see cref="object.Equals(object, object)"/>, so an array member only equals the same array.
/// </remarks>
public sealed class StructValue : IEquatable<StructValue>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="StructValue"/> class.
    /// </summary>
    /// <param name="typeName">The name of the struct type, like <c>Vector</c>.</param>
    /// <param name="names">The names of the members. The constructor doesn't copy the list, so 2 values of 1 struct can share it.</param>
    /// <param name="values">The value of each member, in the order of <paramref name="names"/>.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="values"/> doesn't have 1 value per name.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="typeName"/>, <paramref name="names"/> or <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    public StructValue(string typeName, IReadOnlyList<string> names, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != names.Count)
        {
            throw new ArgumentException(SR.FormatStructValueCountMismatch(names.Count, values.Count), nameof(values));
        }

        TypeName = typeName;
        Names = names;
        Values = values;
    }

    /// <summary>
    ///     Gets the names of the members.
    /// </summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>
    ///     Gets the name of the struct type, like <c>Vector</c>.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    ///     Gets the value of each member, in the order of <see cref="Names"/>.
    /// </summary>
    public IReadOnlyList<object?> Values { get; }

    /// <summary>
    ///     Gets the value of the member <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <returns>
    ///     The value.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    ///     The struct doesn't have the member.
    /// </exception>
    public object? this[string name] => TryGetValue(name, out object? value)
        ? value
        : throw new KeyNotFoundException(SR.FormatStructMemberNotFound(TypeName, name, string.Join(", ", Names)));

    /// <inheritdoc/>
    public bool Equals(StructValue? other)
    {
        if (other is null || other.TypeName != TypeName || other.Names.Count != Names.Count)
        {
            return false;
        }

        for (int i = 0; i < Names.Count; i++)
        {
            if (other.Names[i] != Names[i] || !Equals(other.Values[i], Values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as StructValue);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = StringComparer.Ordinal.GetHashCode(TypeName);

        foreach (object? value in Values)
        {
            hash = unchecked((hash * 31) + (value?.GetHashCode() ?? 0));
        }

        return hash;
    }

    /// <summary>
    ///     Returns the type name and each member with its value.
    /// </summary>
    /// <returns>
    ///     The text, like <c>Vector(X: 1, Y: 2, Z: 3)</c>.
    /// </returns>
    public override string ToString()
    {
        StringBuilder text = new(TypeName);
        text.Append('(');

        for (int i = 0; i < Names.Count; i++)
        {
            string value = Values[i] is { } member ? Convert.ToString(member, CultureInfo.InvariantCulture) : "null";
            text.Append(i == 0 ? "" : ", ").Append(Names[i]).Append(": ").Append(value);
        }

        return text.Append(')').ToString();
    }

    /// <summary>
    ///     Tries to get the value of the member <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <param name="value">The value or <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when the struct has the member.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public bool TryGetValue(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (int i = 0; i < Names.Count; i++)
        {
            if (Names[i] == name)
            {
                value = Values[i];

                return true;
            }
        }

        value = null;

        return false;
    }
}
