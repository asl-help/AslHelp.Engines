namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside a struct.
/// </summary>
/// <param name="SuperStruct">The offset of the pointer to the struct that this struct derives from.</param>
/// <param name="Children">The offset of the pointer to the 1st field.</param>
/// <param name="PropertySize">The offset of the size in bytes of the properties.</param>
public sealed record StructLayout(
    int SuperStruct,
    int Children,
    int PropertySize);
