namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside an object array that keeps its items in chunks behind a pointer to the chunk pointers.
/// </summary>
/// <param name="Objects">The offset of the pointer to the chunk pointers.</param>
/// <param name="NumElements">The offset of the 4-byte count of items.</param>
/// <param name="NumChunks">The offset of the 4-byte count of allocated chunks.</param>
/// <param name="ElementsPerChunk">The number of items in 1 chunk, which is 66560 on 4.20 and 65536 from 4.21.</param>
public sealed record ChunkedObjectArrayLayout(
    int Objects,
    int NumElements,
    int NumChunks,
    int ElementsPerChunk);
