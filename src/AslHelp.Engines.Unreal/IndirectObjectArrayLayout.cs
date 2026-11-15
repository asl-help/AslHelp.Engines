namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside an object array that keeps its chunk pointers inside the array itself.
/// </summary>
/// <param name="Objects">The offset of the 1st chunk pointer.</param>
/// <param name="NumElements">The offset of the 4-byte count of objects.</param>
/// <param name="NumChunks">The offset of the 4-byte count of allocated chunks.</param>
public sealed record IndirectObjectArrayLayout(
    int Objects,
    int NumElements,
    int NumChunks);
