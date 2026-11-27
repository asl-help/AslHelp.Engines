namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets inside the value of a name property.
/// </summary>
/// <param name="Index">The offset of the 4-byte id in the name table.</param>
/// <param name="Number">The offset of the 4-byte number, which is the printed number plus 1.</param>
internal sealed record NameValueLayout(
    int Index,
    int Number);
