using AslHelp.Memory;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the storage shape of the name table that a profile lists and creates the <see cref="INameTable"/> of that shape.
/// </summary>
internal abstract class NameTableShape
{
    /// <summary>
    ///     Gets the key of the global under <c>globals</c> in the profile that the table is read from.
    /// </summary>
    public abstract string Global { get; }

    /// <summary>
    ///     Creates the shape of a name pool, which is <c>chunked</c> in a profile.
    /// </summary>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static NameTableShape Chunked()
    {
        return new ChunkedNameTableShape();
    }

    /// <summary>
    ///     Creates the shape of a chunk table of entry pointers, which is <c>indirect</c> in a profile.
    /// </summary>
    /// <param name="layout">The offsets inside a name entry.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static NameTableShape Indirect(NameEntryLayout layout)
    {
        return new IndirectNameTableShape(layout);
    }

    /// <summary>
    ///     Creates the name table of this shape.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="global">The address of the global with the key <see cref="Global"/>.</param>
    /// <returns>
    ///     The name table.
    /// </returns>
    public abstract INameTable Create(IMemoryReader memory, Address global);
}

file sealed class ChunkedNameTableShape : NameTableShape
{
    public override string Global => "NamePoolData";

    public override INameTable Create(IMemoryReader memory, Address global)
    {
        return new ChunkedNameTable(memory, global);
    }
}

file sealed class IndirectNameTableShape(
    NameEntryLayout layout) : NameTableShape
{
    private readonly NameEntryLayout _layout = layout;

    public override string Global => "NameTable";

    public override INameTable Create(IMemoryReader memory, Address global)
    {
        return new IndirectNameTable(memory, global, _layout);
    }
}
