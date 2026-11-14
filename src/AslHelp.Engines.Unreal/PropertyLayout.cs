namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents the offsets inside a property, in both storage shapes.
/// </summary>
/// <param name="ArrayDim">The offset of the number of elements.</param>
/// <param name="ElementSize">The offset of the size in bytes of 1 element.</param>
/// <param name="Offset">The offset of the position of the value in an instance of the struct.</param>
public sealed record PropertyLayout(
    int ArrayDim,
    int ElementSize,
    int Offset);
