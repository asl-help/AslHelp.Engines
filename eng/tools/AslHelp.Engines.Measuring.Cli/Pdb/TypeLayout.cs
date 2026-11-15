using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Describes 1 complete layout of a type in a PDB.
/// </summary>
/// <param name="Name">The name of the type.</param>
/// <param name="Size">The size of the type in bytes.</param>
/// <param name="Members">The data members, own members first, then the members of each base class.</param>
internal sealed record TypeLayout(
    string Name,
    uint Size,
    IReadOnlyList<TypeMember> Members)
{
    /// <summary>
    ///     Finds a data member by name.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <returns>
    ///     The 1st member with that name or <see langword="null"/> if there isn't one. An own member comes before a base class member.
    /// </returns>
    public TypeMember? Member(string name)
    {
        foreach (var member in Members)
        {
            if (member.Name == name)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    ///     Compares 2 layouts by name, size and every member.
    /// </summary>
    /// <param name="other">The other layout.</param>
    /// <returns>
    ///     <see langword="true"/> if both layouts are the same.
    /// </returns>
    public bool Equals(TypeLayout? other)
    {
        return other is not null && Name == other.Name && Size == other.Size && Members.SequenceEqual(other.Members);
    }

    /// <summary>
    ///     Gets a hash code from the name, the size and the number of members.
    /// </summary>
    /// <returns>
    ///     The hash code.
    /// </returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Size, Members.Count);
    }
}
