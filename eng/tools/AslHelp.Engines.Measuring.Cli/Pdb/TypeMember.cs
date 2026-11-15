namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Describes a data member of a type in a PDB.
/// </summary>
/// <param name="Name">The name of the member.</param>
/// <param name="Offset">The offset from the start of the type, with the offsets of the base classes added.</param>
/// <param name="Size">
///     The size in bytes, the size of the storage word for a bitfield,
///     or <see langword="null"/> if the PDB doesn't have 1 size for the type of the member.
/// </param>
/// <param name="BitPosition">The position of the 1st bit in the storage word or <see langword="null"/> if the member is not a bitfield.</param>
/// <param name="BitCount">The number of bits or <see langword="null"/> if the member is not a bitfield.</param>
/// <param name="DeclaredBy">The name of the type that declares the member.</param>
internal sealed record TypeMember(
    string Name,
    uint Offset,
    uint? Size,
    byte? BitPosition,
    byte? BitCount,
    string DeclaredBy);
