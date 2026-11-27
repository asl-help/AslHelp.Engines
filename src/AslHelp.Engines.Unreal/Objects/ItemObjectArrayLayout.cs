namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Represents the offsets inside an object array that keeps all its items behind 1 pointer.
/// </summary>
/// <param name="Objects">The offset of the pointer to the items.</param>
/// <param name="NumElements">The offset of the 4-byte count of items.</param>
public sealed record ItemObjectArrayLayout(
    int Objects,
    int NumElements);
