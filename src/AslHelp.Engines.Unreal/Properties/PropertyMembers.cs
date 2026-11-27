using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides the reads of the members that the <see cref="IPropertyReader"/> shapes share.
/// </summary>
internal static class PropertyMembers
{
    /// <summary>
    ///     Reads the number of elements of a property.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="prop">The address of the property.</param>
    /// <param name="layout">The offsets inside a property.</param>
    /// <returns>
    ///     The number of elements or a failure with the property and the failed read.
    /// </returns>
    public static Result<int> ReadArrayDim(IMemoryReader memory, Address prop, PropertyLayout layout)
    {
        var address = prop + layout.ArrayDim;

        return memory.Read<int>(address).TryUnwrap(out int arrayDim, out var error)
            ? arrayDim
            : PropertyError.ArrayDimUnreadable(prop, address, layout.ArrayDim, error);
    }

    /// <summary>
    ///     Reads the size in bytes of 1 element of a property.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="prop">The address of the property.</param>
    /// <param name="layout">The offsets inside a property.</param>
    /// <returns>
    ///     The size or a failure with the property and the failed read.
    /// </returns>
    public static Result<int> ReadElementSize(IMemoryReader memory, Address prop, PropertyLayout layout)
    {
        var address = prop + layout.ElementSize;

        return memory.Read<int>(address).TryUnwrap(out int elementSize, out var error)
            ? elementSize
            : PropertyError.ElementSizeUnreadable(prop, address, layout.ElementSize, error);
    }

    /// <summary>
    ///     Reads the position of the value of a property in an instance of its struct.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="prop">The address of the property.</param>
    /// <param name="layout">The offsets inside a property.</param>
    /// <returns>
    ///     The position or a failure with the property and the failed read.
    /// </returns>
    public static Result<int> ReadOffset(IMemoryReader memory, Address prop, PropertyLayout layout)
    {
        var address = prop + layout.Offset;

        return memory.Read<int>(address).TryUnwrap(out int offset, out var error)
            ? offset
            : PropertyError.OffsetUnreadable(prop, address, layout.Offset, error);
    }
}
