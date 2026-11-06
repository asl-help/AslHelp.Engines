namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside a name entry.
/// </summary>
/// <param name="Index">The offset of the id, which is shifted left by 1 with the low bit set for UTF-16 text.</param>
/// <param name="Name">The offset of the text.</param>
public sealed record NameEntryLayout(
    int Index,
    int Name);
