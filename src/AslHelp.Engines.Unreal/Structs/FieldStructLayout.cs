namespace AslHelp.Engines.Unreal.Structs;

/// <summary>
///     Represents the offsets inside a struct whose properties aren't objects, which is how Unreal Engine 4.25 and later store properties.
/// </summary>
/// <param name="ChildProperties">The offset of the pointer to the 1st property.</param>
public sealed record FieldStructLayout(
    int ChildProperties);
