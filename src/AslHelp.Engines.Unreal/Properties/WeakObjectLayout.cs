namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets inside the value of an <c>FWeakObjectPtr</c>.
/// </summary>
/// <param name="ObjectIndex">The offset of the 4-byte position of the object in the object array.</param>
/// <param name="ObjectSerialNumber">The offset of the 4-byte serial number that the item at that position has to match.</param>
/// <param name="PropertyClass">
///     The offset of the pointer to the class that the property declares or <see langword="null"/> when the profile doesn't have it.
/// </param>
internal sealed record WeakObjectLayout(
    int ObjectIndex,
    int ObjectSerialNumber,
    int? PropertyClass);
