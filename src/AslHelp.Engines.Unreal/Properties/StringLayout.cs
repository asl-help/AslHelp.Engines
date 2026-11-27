namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets inside the value of an <c>FString</c>.
/// </summary>
/// <param name="Data">The offset of the pointer to the characters.</param>
/// <param name="Count">The offset of the 4-byte count of characters with the terminator.</param>
internal sealed record StringLayout(
    int Data,
    int Count);
