using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a <c>TArray</c>, whose elements read through the kind of its inner property.
/// </summary>
/// <remarks>
///     An array of a value kind reads as an array of that type, like <see cref="int"/>[] for <c>TArray&lt;int32&gt;</c>.
///     An index reads 1 element or steps into it, so an array of structs steps by the element size and an array of objects reads the pointer.
/// </remarks>
internal sealed class ArrayKind : LayoutKind<ArrayPropertyLayout>
{
    private const string ClassName = "ArrayProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitCollection(property);
    }

    /// <summary>
    ///     Reads the offset of the inner property and the offsets inside a <c>TArray</c>.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the missing offsets.
    /// </returns>
    public override Result<ArrayPropertyLayout> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);
        int inner = offsets.ReadProperty(ClassName, "Inner");
        int data = offsets.Read("FScriptArray", "Data");
        int count = offsets.Read("FScriptArray", "ArrayNum");

        return offsets.Finish(new ArrayPropertyLayout(inner, data, count));
    }

    /// <summary>
    ///     Reads the inner property of <paramref name="property"/> and creates the access of the array.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="layout">The offsets of the array.</param>
    /// <returns>
    ///     The access or a failure when the inner property doesn't read or its element size isn't above 0.
    /// </returns>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, ArrayPropertyLayout layout)
    {
        if (!InnerProperty.Read(classReader, property, "Inner", layout.Inner).TryUnwrap(out var inner, out var error)
            || !inner.Bind().TryUnwrap(out var element, out error))
        {
            return error;
        }

        if (inner.ElementSize <= 0)
        {
            return CollectionError.ElementSizeInvalid(property.Name, inner.ElementSize, "ElementSize");
        }

        ArrayElements elements = new(property, layout, element, inner.ElementSize);

        return element.Accept(new ArrayAccessFactory(property, elements, inner.ClassName));
    }
}

file sealed class ArrayAccessFactory(
    UnrealProperty property,
    ArrayElements elements,
    string elementClass) : AccessVisitor<Result<PropertyAccess>>
{
    public override Result<PropertyAccess> VisitOther(PropertyAccess access)
    {
        return new CollectionStepAccess(property, elements, elementClass);
    }

    public override Result<PropertyAccess> VisitValue<T>(ValueAccess<T> access)
    {
        return new ArrayValueAccess<T>(property, elements, access);
    }
}

file sealed class ArrayElements(
    UnrealProperty property,
    ArrayPropertyLayout layout,
    PropertyAccess element,
    int stride) : CollectionAccess(property)
{
    private readonly ArrayPropertyLayout _layout = layout;

    public override ValueAccess<int> Count { get; } = new ArrayCount(property, layout);

    public override PropertyAccess Element { get; } = element;

    public int Stride { get; } = stride;

    public override Result<Address> FindElement(IMemoryReader memory, Address container, ElementKey key)
    {
        if (!key.As<int>(this, Property.Name).TryUnwrap(out int index, out var error)
            || !ReadCount(memory, container).TryUnwrap(out int count, out error))
        {
            return error;
        }

        if (index < 0 || index >= count)
        {
            return key.OutOfRange(Property.Name, index, count);
        }

        return ReadData(memory, container, count).TryUnwrap(out var data, out error)
            ? data + ((long)index * Stride)
            : error;
    }

    // Read the number of elements and fail a count below 0.
    public Result<int> ReadCount(IMemoryReader memory, Address container)
    {
        return Count.Read(memory, container, 0);
    }

    // Read the pointer to the elements and fail a pointer of 0 when the array has elements.
    public Result<Address> ReadData(IMemoryReader memory, Address container, int count)
    {
        var address = container + Property.Offset + _layout.Data;

        if (!memory.ReadPointer(address).TryUnwrap(out var data, out var error))
        {
            return ValueError.Unreadable(container, Property.Name, address, error);
        }

        return data == Address.Zero ? CollectionError.DataNull(Property.Name, count) : data;
    }
}

file sealed class ArrayCount(
    UnrealProperty property,
    ArrayPropertyLayout layout) : ValueAccess<int>(property)
{
    private readonly ArrayPropertyLayout _layout = layout;

    public override Result<int> Read(IMemoryReader memory, Address container, int last)
    {
        var address = container + Property.Offset + _layout.Count;

        if (!memory.Read<int>(address).TryUnwrap(out int count, out var error))
        {
            return ValueError.Unreadable(container, Property.Name, address, error);
        }

        return count < 0 ? CollectionError.CountNegative(Property.Name, count) : count;
    }
}

file sealed class ArrayValueAccess<T>(
    UnrealProperty property,
    ArrayElements elements,
    ValueAccess<T> element) : ValueAccess<T[]>(property)
{
    private readonly ElementBuffer<T> _buffer = new();
    private readonly ValueAccess<T> _element = element;
    private readonly ArrayElements _elements = elements;

    public override Result<T[]> Read(IMemoryReader memory, Address container, T[]? last)
    {
        if (!_elements.ReadCount(memory, container).TryUnwrap(out int count, out var error))
        {
            return error;
        }

        if (count > ValueBounds.MaxElements)
        {
            return CollectionError.TooLong(Property.Name, count, ValueBounds.MaxElements);
        }

        if (count == 0)
        {
            T[] empty = [];

            return last is { Length: 0 } ? last : empty;
        }

        return _elements.ReadData(memory, container, count).TryUnwrap(out var data, out error)
            ? _buffer.Read(memory, _element, data, count, _elements.Stride, last)
            : error;
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container, null, _elements);
    }
}
