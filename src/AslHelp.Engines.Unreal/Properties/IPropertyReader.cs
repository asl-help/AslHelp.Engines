using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Provides reads of the properties of a struct from 1 storage shape.
/// </summary>
public interface IPropertyReader
{
    /// <summary>
    ///     Enumerates the properties that <paramref name="type"/> declares itself in chain order.
    /// </summary>
    /// <remarks>
    ///     Leaves out the properties of the super structs. <see cref="StructReader.SuperOf"/> reads the next struct to enumerate.
    ///     Reads each property when the enumeration moves to it, so a caller can stop early.
    ///     Stops at <see cref="Address.Zero"/> and fails after 4096 fields, because a damaged chain can loop.
    ///     Ends at the 1st read that fails, with the failure as the last item.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The addresses of the properties, then a failure if a read failed or the chain goes on past 4096 fields.
    /// </returns>
    IEnumerable<Result<Address>> PropertiesOf(Address type);

    /// <summary>
    ///     Reads the name of <paramref name="prop"/> as the engine prints it.
    /// </summary>
    /// <remarks>
    ///     Returns the base name for name number 0 and the base name, <c>_</c> and the number minus 1 for a number above 0.
    /// </remarks>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The name or a failure with the property and the failed read.
    /// </returns>
    Result<string> NameOf(Address prop);

    /// <summary>
    ///     Reads the class name of <paramref name="prop"/>, without a prefix, such as <c>IntProperty</c>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The class name or a failure with the property and the failed read.
    /// </returns>
    Result<string> ClassNameOf(Address prop);

    /// <summary>
    ///     Reads the number of elements of <paramref name="prop"/>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The number of elements or a failure with the property and the failed read.
    /// </returns>
    Result<int> ArrayDimOf(Address prop);

    /// <summary>
    ///     Reads the size in bytes of 1 element of <paramref name="prop"/>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The size or a failure with the property and the failed read.
    /// </returns>
    Result<int> ElementSizeOf(Address prop);

    /// <summary>
    ///     Reads the position of the value of <paramref name="prop"/> in an instance of its struct.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The position or a failure with the property and the failed read.
    /// </returns>
    Result<int> OffsetOf(Address prop);
}
