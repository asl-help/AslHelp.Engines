using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of an <c>FString</c>, which reads as its UTF-16 text without the terminator.
/// </summary>
internal sealed class StringKind : LayoutKind<StringLayout>
{
    private const string ClassName = "StrProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitValue<string>(property);
    }

    /// <summary>
    ///     Reads the offsets of the data pointer and the count of the array inside an <c>FString</c>.
    /// </summary>
    /// <remarks>
    ///     Adds <c>FString.Data</c>, the offset of the array in the string, when the document has it. Unreal Engine 3 documents don't.
    /// </remarks>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the missing offsets of the array.
    /// </returns>
    public override Result<StringLayout> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);
        int array = offsets.ReadOrNull("FString", "Data") ?? 0;
        int data = offsets.Read("FScriptArray", "Data");
        int count = offsets.Read("FScriptArray", "ArrayNum");

        return offsets.Finish(new StringLayout(array + data, array + count));
    }

    /// <inheritdoc/>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, StringLayout layout)
    {
        return new StringAccess(property, layout);
    }
}

file sealed class StringAccess(
    UnrealProperty property,
    StringLayout layout) : ValueAccess<string>(property)
{
    private char[] _chars = [];
    private readonly StringLayout _layout = layout;

    public override Result<string> Read(IMemoryReader memory, Address container, string? last)
    {
        if (!ReadChars(memory, container).TryUnwrap(out int length, out var error))
        {
            return error;
        }

        // Return the last text when it's equal, so a text that doesn't change doesn't allocate.
        return last is not null && _chars.AsSpan(0, length).SequenceEqual(last.AsSpan()) ? last : new string(_chars, 0, length);
    }

    public override Result<bool> Matches(IMemoryReader memory, Address container, string value)
    {
        return ReadChars(memory, container).TryUnwrap(out int length, out var error)
            ? _chars.AsSpan(0, length).SequenceEqual(value.AsSpan())
            : error;
    }

    // Read the characters into the kept buffer and return their number without the terminator.
    private Result<int> ReadChars(IMemoryReader memory, Address container)
    {
        var start = container + Property.Offset;
        var countAddress = start + _layout.Count;

        if (!memory.Read<int>(countAddress).TryUnwrap(out int count, out var error))
        {
            return ValueError.Unreadable(container, Property.Name, countAddress, error);
        }

        if (count < 0)
        {
            return ValueError.StringCountNegative(Property.Name, count);
        }

        // Read 0 and 1 as empty without the data, because the count includes the terminator and an empty FString doesn't allocate.
        if (count <= 1)
        {
            return 0;
        }

        int length = count - 1;

        if (length > ValueBounds.MaxChars)
        {
            return ValueError.StringTooLong(Property.Name, length, ValueBounds.MaxChars);
        }

        var dataAddress = start + _layout.Data;

        if (!memory.ReadPointer(dataAddress).TryUnwrap(out var data, out error))
        {
            return ValueError.Unreadable(container, Property.Name, dataAddress, error);
        }

        if (data == Address.Zero)
        {
            return ValueError.StringDataNull(Property.Name, length);
        }

        if (_chars.Length < length)
        {
            _chars = new char[ValueBounds.MaxChars];
        }

        var chars = _chars.AsSpan(0, length);

        if (memory.Read(data, MemoryMarshal.AsBytes(chars)).TryUnwrapErr(out error))
        {
            return ValueError.Unreadable(container, Property.Name, data, error);
        }

        // Swap the bytes of each character when the game's byte order isn't the host's, because the read copies the bytes as they are.
        if (BitConverter.IsLittleEndian != (memory.Layout.ByteOrder == ByteOrder.LittleEndian))
        {
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)BinaryPrimitives.ReverseEndianness(chars[i]);
            }
        }

        return length;
    }
}
