namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside an object.
/// </summary>
/// <param name="Index">The offset of the position of the object in the object array.</param>
/// <param name="Outer">The offset of the pointer to the outer object.</param>
/// <param name="Name">The offset of the name id, with the 4-byte name number right after it.</param>
/// <param name="Class">The offset of the pointer to the class.</param>
public sealed record ObjectLayout(
    int Index,
    int Outer,
    int Name,
    int Class);
