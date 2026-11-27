using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents a kind whose value is 1 number of <typeparamref name="T"/> at the offset of the property, like <c>IntProperty</c>.
/// </summary>
/// <typeparam name="T">The type of the number.</typeparam>
/// <param name="className">The property class without a prefix, like <c>IntProperty</c>.</param>
internal sealed class NumberKind<T>(
    string className) : PropertyKind
    where T : unmanaged
{
    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [className];

    /// <inheritdoc/>
    public override bool IsNumber => true;

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitValue<T>(property);
    }

    /// <inheritdoc/>
    public override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader)
    {
        return new NumberAccess<T>(property);
    }
}

file sealed class NumberAccess<T>(
    UnrealProperty property) : ValueAccess<T>(property)
    where T : unmanaged
{
    public override Result<T> Read(IMemoryReader memory, Address container, T last)
    {
        var address = container + Property.Offset;

        return memory.Read<T>(address).TryUnwrap(out var value, out var error)
            ? value
            : ValueError.Unreadable(container, Property.Name, address, error);
    }

    // Read every element in 1 read when the numbers sit right after each other, like the elements of an int array.
    public override Result ReadMany(IMemoryReader memory, Address data, int stride, Span<T> values, ReadOnlySpan<T> last)
    {
        var bytes = MemoryMarshal.AsBytes(values);
        int size = values.IsEmpty ? 0 : bytes.Length / values.Length;

        if (values.IsEmpty || stride != size || Property.Offset != 0)
        {
            return base.ReadMany(memory, data, stride, values, last);
        }

        if (memory.Read(data, bytes).TryUnwrapErr(out var error))
        {
            return ValueError.Unreadable(data, Property.Name, data, error);
        }

        // Swap the bytes of each number when the game's byte order isn't the host's, because the read copies the bytes as they are.
        if (size > 1 && BitConverter.IsLittleEndian != (memory.Layout.ByteOrder == ByteOrder.LittleEndian))
        {
            for (int i = 0; i < bytes.Length; i += size)
            {
                bytes.Slice(i, size).Reverse();
            }
        }

        return Result.Ok();
    }
}
