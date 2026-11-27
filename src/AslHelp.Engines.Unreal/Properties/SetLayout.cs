using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the offsets of a <c>TSet</c>, the storage of a set and of the pairs of a map.
/// </summary>
/// <param name="Size">The offset of the 4-byte size of 1 slot in the set layout that a set or map property keeps.</param>
/// <param name="Elements">The offset of the sparse array in the set.</param>
/// <param name="SparseArray">The offsets inside the sparse array.</param>
internal sealed record SetLayout(
    int Size,
    int Elements,
    SparseArrayLayout SparseArray)
{
    /// <summary>
    ///     Reads the offsets of a set.
    /// </summary>
    /// <remarks>
    ///     Reads the 5.7 names <c>FScriptSparseSetLayout</c> and <c>TScriptSparseSet</c> when the document has them,
    ///     otherwise <c>FScriptSetLayout</c> and <c>FScriptSet</c>.
    /// </remarks>
    /// <param name="offsets">The reads of the kind, which collect the missing offsets.</param>
    /// <returns>
    ///     The layout, with 0 for each missing offset.
    /// </returns>
    public static SetLayout Read(KindOffsets offsets)
    {
        bool sparseSet = offsets.Has("FScriptSparseSetLayout", "Size");
        int size = offsets.Read(sparseSet ? "FScriptSparseSetLayout" : "FScriptSetLayout", "Size");
        int elements = offsets.Read(sparseSet ? "TScriptSparseSet<FDefaultSetAllocator>" : "FScriptSet", "Elements");

        return new SetLayout(size, elements, SparseArrayLayout.Read(offsets));
    }

    /// <summary>
    ///     Reads the 4-byte size of 1 slot from the set layout of a set or a map property.
    /// </summary>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="collectionProperty">The set or map property.</param>
    /// <param name="setLayout">The offset of the set layout in <paramref name="collectionProperty"/>.</param>
    /// <returns>
    ///     The size or a failure when the read fails or the size isn't above 0.
    /// </returns>
    public Result<int> ReadSlotSize(ClassReader classReader, UnrealProperty collectionProperty, int setLayout)
    {
        var address = collectionProperty.Address + setLayout + Size;

        if (!classReader.Memory.Read<int>(address).TryUnwrap(out int size, out var error))
        {
            return CollectionError.MemberUnreadable(collectionProperty.Name, "SetLayout", address, error);
        }

        return size > 0 ? size : CollectionError.ElementSizeInvalid(collectionProperty.Name, size, "SetLayout");
    }
}
