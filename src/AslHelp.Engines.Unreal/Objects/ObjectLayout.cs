using AslHelp.Engines.Unreal.Structs;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Represents the offsets inside an object.
/// </summary>
/// <param name="Outer">The offset of the pointer to the outer object.</param>
/// <param name="Name">The offset of the name id, with the 4-byte name number right after it.</param>
/// <param name="Class">The offset of the pointer to the class.</param>
public sealed record ObjectLayout(
    int Outer,
    int Name,
    int Class);
