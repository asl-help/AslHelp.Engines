using AslHelp.Memory;
using AslHelp.Memory.Locating;

namespace AslHelp.Engines.Unreal.Objects;

/// <summary>
///     Reads the serial numbers that Unreal Engine 4.0 to 4.10 keep in blocks apart from the object array.
/// </summary>
/// <remarks>
///     <c>GSerialNumberBlocksForDebugVisualizersRoot</c> points at a table of block pointers. Each block has the serial numbers of 16384 slots.
///     The engine allocates a block the 1st time a weak pointer to an object in it is made, so a slot in a missing block reads as 0.
/// </remarks>
/// <param name="memory">The memory of the game.</param>
/// <param name="root">The slot of <c>GSerialNumberBlocksForDebugVisualizersRoot</c>.</param>
internal sealed class SerialNumberBlocks(
    IMemoryReader memory,
    GlobalPointer root)
{
    /// <summary>
    ///     The name of the global with the table of blocks.
    /// </summary>
    public const string Global = "GSerialNumberBlocksForDebugVisualizersRoot";

    private const int SerialsPerBlock = 16 * 1024;

    /// <summary>
    ///     Reads the serial number of the slot at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The index of the slot, at least 0.</param>
    /// <returns>
    ///     The serial number or a failure when a read fails. A slot whose table or block isn't allocated yet reads as 0.
    /// </returns>
    public Result<int> Read(int index)
    {
        if (!root.Read().TryUnwrap(out var table, out var error))
        {
            return error;
        }

        if (table == Address.Zero)
        {
            return 0;
        }

        if (!memory.ReadPointer(table + ((long)(index / SerialsPerBlock) * memory.Layout.PointerSize)).TryUnwrap(out var block, out error))
        {
            return error;
        }

        return block == Address.Zero ? 0 : memory.Read<int>(block + ((long)(index % SerialsPerBlock) * sizeof(int)));
    }
}
