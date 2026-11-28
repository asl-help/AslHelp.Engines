namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets inside an object property.
/// </summary>
/// <param name="PropertyClass">
///     The offset of the pointer to the class that the property declares or <see langword="null"/> when the profile doesn't have it.
/// </param>
internal sealed record ObjectPropertyLayout(
    int? PropertyClass);
