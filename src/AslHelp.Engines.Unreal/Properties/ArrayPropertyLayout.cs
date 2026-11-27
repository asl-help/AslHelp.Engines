namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets that an array property reads.
/// </summary>
/// <param name="Inner">The offset of the pointer to the property of the elements in an array property.</param>
/// <param name="Data">The offset of the pointer to the elements in a <c>TArray</c>.</param>
/// <param name="Count">The offset of the 4-byte number of elements in a <c>TArray</c>.</param>
internal sealed record ArrayPropertyLayout(
    int Inner,
    int Data,
    int Count);
