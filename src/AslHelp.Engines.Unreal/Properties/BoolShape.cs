using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents how a bool property stores the mask of its bit and creates the access that reads the bit.
/// </summary>
internal abstract class BoolShape
{
    /// <summary>
    ///     Creates the shape of a 4-byte mask in the property, which is how Unreal Engine 3 stores bools.
    /// </summary>
    /// <param name="bitMaskOffset">The offset of the mask in the property.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static BoolShape BitMask(int bitMaskOffset)
    {
        return new BitMaskShape(bitMaskOffset);
    }

    /// <summary>
    ///     Creates the shape of a byte offset and a 1-byte mask in the property, which is how Unreal Engine 4 and 5 store bools.
    /// </summary>
    /// <remarks>
    ///     The field mask is 0xFF for a native bool and the mask of the bit for a bitfield, like <c>uint8 bFirst : 1</c>.
    /// </remarks>
    /// <param name="byteOffsetOffset">The offset of the byte offset in the property.</param>
    /// <param name="fieldMaskOffset">The offset of the field mask in the property.</param>
    /// <returns>
    ///     The shape.
    /// </returns>
    public static BoolShape ByteMask(int byteOffsetOffset, int fieldMaskOffset)
    {
        return new ByteMaskShape(byteOffsetOffset, fieldMaskOffset);
    }

    /// <summary>
    ///     Reads the mask of <paramref name="property"/> and creates the access that reads its bit.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="property">The property.</param>
    /// <returns>
    ///     The access or a failure when the mask doesn't read.
    /// </returns>
    public abstract Result<PropertyAccess> Bind(IMemoryReader memory, UnrealProperty property);
}

file sealed class BitMaskShape(
    int bitMaskOffset) : BoolShape
{
    private readonly int _bitMaskOffset = bitMaskOffset;

    public override Result<PropertyAccess> Bind(IMemoryReader memory, UnrealProperty property)
    {
        var address = property.Address + _bitMaskOffset;

        return memory.Read<uint>(address).TryUnwrap(out uint bitMask, out var error)
            ? new BitMaskAccess(property, bitMask)
            : ValueError.BoolMaskUnreadable(property.Name, address, error);
    }
}

file sealed class ByteMaskShape(
    int byteOffsetOffset,
    int fieldMaskOffset) : BoolShape
{
    private readonly int _byteOffsetOffset = byteOffsetOffset;
    private readonly int _fieldMaskOffset = fieldMaskOffset;

    public override Result<PropertyAccess> Bind(IMemoryReader memory, UnrealProperty property)
    {
        var byteOffsetAddress = property.Address + _byteOffsetOffset;

        if (!memory.Read<byte>(byteOffsetAddress).TryUnwrap(out byte byteOffset, out var error))
        {
            return ValueError.BoolMaskUnreadable(property.Name, byteOffsetAddress, error);
        }

        var fieldMaskAddress = property.Address + _fieldMaskOffset;

        return memory.Read<byte>(fieldMaskAddress).TryUnwrap(out byte fieldMask, out error)
            ? new ByteMaskAccess(property, byteOffset, fieldMask)
            : ValueError.BoolMaskUnreadable(property.Name, fieldMaskAddress, error);
    }
}

file sealed class BitMaskAccess(
    UnrealProperty property,
    uint bitMask) : ValueAccess<bool>(property)
{
    private readonly uint _bitMask = bitMask;

    public override Result<bool> Read(IMemoryReader memory, Address container, bool last)
    {
        var address = container + Property.Offset;

        return memory.Read<uint>(address).TryUnwrap(out uint bits, out var error)
            ? (bits & _bitMask) != 0
            : ValueError.Unreadable(container, Property.Name, address, error);
    }
}

file sealed class ByteMaskAccess(
    UnrealProperty property,
    byte byteOffset,
    byte fieldMask) : ValueAccess<bool>(property)
{
    private readonly byte _byteOffset = byteOffset;
    private readonly byte _fieldMask = fieldMask;

    public override Result<bool> Read(IMemoryReader memory, Address container, bool last)
    {
        var address = container + Property.Offset + _byteOffset;

        return memory.Read<byte>(address).TryUnwrap(out byte bits, out var error)
            ? (bits & _fieldMask) != 0
            : ValueError.Unreadable(container, Property.Name, address, error);
    }
}
