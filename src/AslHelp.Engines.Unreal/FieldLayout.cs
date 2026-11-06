namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside a field.
/// </summary>
/// <param name="Next">The offset of the pointer to the next field.</param>
public sealed record FieldLayout(
    int Next);
