namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets that a map property reads.
/// </summary>
/// <param name="KeyProp">The offset of the pointer to the property of the keys in a map property.</param>
/// <param name="ValueProp">The offset of the pointer to the property of the values in a map property.</param>
/// <param name="MapLayout">The offset of the map layout in a map property.</param>
/// <param name="SetLayout">The offset of the set layout in the map layout.</param>
/// <param name="Pairs">The offset of the set of pairs in a <c>TMap</c>.</param>
/// <param name="Set">The offsets of the set of pairs.</param>
internal sealed record MapPropertyLayout(
    int KeyProp,
    int ValueProp,
    int MapLayout,
    int SetLayout,
    int Pairs,
    SetLayout Set);
