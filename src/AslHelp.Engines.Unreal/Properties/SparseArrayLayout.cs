namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets inside a <c>TSparseArray</c>, the storage of a set or a map, from the start of the sparse array.
/// </summary>
/// <remarks>
///     The allocator of the bit array keeps 4 words inline, then the pointer to the words on the heap, right before <c>NumBits</c>.
///     The PDBs of most versions don't have the allocator type, so a read takes the pointer 1 pointer before <c>NumBits</c>.
/// </remarks>
/// <param name="Data">The offset of the pointer to the slots.</param>
/// <param name="Count">The offset of the 4-byte number of slots, used or free.</param>
/// <param name="NumFreeIndices">The offset of the 4-byte number of free slots.</param>
/// <param name="InlineBits">The offset of the allocation bits when they fit in the sparse array.</param>
/// <param name="NumBits">The offset of the number of bits, right after the pointer to the allocation bits on the heap.</param>
internal sealed record SparseArrayLayout(
    int Data,
    int Count,
    int NumFreeIndices,
    int InlineBits,
    int NumBits)
{
    /// <summary>
    ///     Reads the offsets inside a sparse array and its bit array.
    /// </summary>
    /// <param name="offsets">The reads of the kind, which collect the missing offsets.</param>
    /// <returns>
    ///     The layout, with 0 for each missing offset.
    /// </returns>
    public static SparseArrayLayout Read(KindOffsets offsets)
    {
        int data = offsets.Read("FScriptSparseArray", "Data");
        int arrayData = offsets.Read("FScriptArray", "Data");
        int arrayNum = offsets.Read("FScriptArray", "ArrayNum");
        int numFreeIndices = offsets.Read("FScriptSparseArray", "NumFreeIndices");
        int flags = offsets.Read("FScriptSparseArray", "AllocationFlags");
        int allocator = offsets.Read("FScriptBitArray", "AllocatorInstance");
        int numBits = offsets.Read("FScriptBitArray", "NumBits");

        return new SparseArrayLayout(data + arrayData, data + arrayNum, numFreeIndices, flags + allocator, flags + numBits);
    }
}
