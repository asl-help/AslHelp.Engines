using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Represents the object at 1 position of an object array and the serial number that a weak pointer compares.
/// </summary>
/// <param name="Address">The address of the object or <see cref="Address.Zero"/> for an empty position.</param>
/// <param name="SerialNumber">
///     The serial number or <see langword="null"/> when the array doesn't keep one. It stays 0 until the engine creates a weak pointer to the object.
/// </param>
public readonly record struct ObjectItem(
    Address Address,
    int? SerialNumber);
