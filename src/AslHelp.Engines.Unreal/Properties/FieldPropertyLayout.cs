using AslHelp.Engines.Unreal.Structs;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets inside a property that isn't an object, which is how Unreal Engine 4.25 and later store properties.
/// </summary>
/// <param name="Class">The offset of the pointer to the field class.</param>
/// <param name="Next">The offset of the pointer to the next property.</param>
/// <param name="Name">The offset of the name id, with the 4-byte name number right after it.</param>
public sealed record FieldPropertyLayout(
    int Class,
    int Next,
    int Name);
