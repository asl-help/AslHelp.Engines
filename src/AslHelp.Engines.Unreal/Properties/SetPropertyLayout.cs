namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets that a set property reads.
/// </summary>
/// <param name="ElementProp">The offset of the pointer to the property of the elements in a set property.</param>
/// <param name="SetLayout">The offset of the set layout in a set property.</param>
/// <param name="Set">The offsets of the set.</param>
internal sealed record SetPropertyLayout(
    int ElementProp,
    int SetLayout,
    SetLayout Set);
