using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a struct in an object, which a path goes into by the offset of the property without a pointer read.
/// </summary>
/// <remarks>
///     A struct whose members are all single numbers of 1 class right after each other also reads as an array of that number.
///     <c>FVector</c> reads as <see cref="float"/>[] or <see cref="double"/>[], depending on the class of its members.
/// </remarks>
internal sealed class StructKind : LayoutKind<int>
{
    private const string ClassName = "StructProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitStruct(property);
    }

    /// <summary>
    ///     Reads the offset of the pointer to the struct type in a struct property.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The offset or a failure with its path.
    /// </returns>
    public override Result<int> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);

        return offsets.Finish(offsets.ReadProperty(ClassName, "Struct"));
    }

    /// <summary>
    ///     Creates the access that goes into the struct of <paramref name="property"/>.
    /// </summary>
    /// <remarks>
    ///     Reads the pointer to the struct type and its properties once, because the engine doesn't change a loaded property.
    /// </remarks>
    /// <param name="property">The property.</param>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="layout">The offset of the pointer to the struct type.</param>
    /// <returns>
    ///     The access or a failure when the struct pointer, the name of the struct or its properties don't read.
    /// </returns>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, int layout)
    {
        var address = property.Address + layout;

        if (!classReader.Memory.ReadPointer(address).TryUnwrap(out var structType, out var error))
        {
            return ValueError.StructTypeUnreadable(property.Name, address, error);
        }

        if (structType == Address.Zero)
        {
            return new FailedAccess(property, ValueError.StructTypeNull(property.Name, property.Address, layout));
        }

        if (!classReader.StructAt(structType).TryUnwrap(out var type, out error)
            || !FindNumbers(classReader, type).TryUnwrap(out var numbers, out error))
        {
            return error;
        }

        if (numbers is not { } found)
        {
            return new StructAccess(property, type);
        }

        return found.First.Bind().TryUnwrap(out var first, out error)
            ? first.Accept(new NumberStructFactory(property, type, found.Count, found.First.ElementSize))
            : error;
    }

    // Return the member at offset 0 and the number of members when every member is 1 number of the same class
    // and each sits right after the one before it, otherwise null.
    private static Result<(UnrealProperty First, int Count)?> FindNumbers(ClassReader classReader, UnrealClass type)
    {
        List<UnrealProperty> members = [];

        foreach (var item in classReader.PropertiesOf(type.Address))
        {
            if (!item.TryUnwrap(out var member, out var error))
            {
                return error;
            }

            if (!member.Kind.IsNumber || member.ClassName != (members.Count == 0 ? member.ClassName : members[0].ClassName))
            {
                return Result.Ok<(UnrealProperty, int)?>(null);
            }

            // Keep a struct with a static array member like float V[3] a plain struct, because the array would only get its 1st element.
            if (!classReader.ArrayDimOf(member.Address).TryUnwrap(out int arrayDim, out error))
            {
                return error;
            }

            if (arrayDim != 1)
            {
                return Result.Ok<(UnrealProperty, int)?>(null);
            }

            members.Add(member);
        }

        if (members.Count == 0)
        {
            return Result.Ok<(UnrealProperty, int)?>(null);
        }

        members.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Offset != i * members[0].ElementSize)
            {
                return Result.Ok<(UnrealProperty, int)?>(null);
            }
        }

        return Result.Ok<(UnrealProperty, int)?>((members[0], members.Count));
    }
}

file sealed class NumberStructFactory(
    UnrealProperty property,
    UnrealClass structType,
    int count,
    int stride) : AccessVisitor<Result<PropertyAccess>>
{
    public override Result<PropertyAccess> VisitOther(PropertyAccess access)
    {
        return new StructAccess(property, structType);
    }

    public override Result<PropertyAccess> VisitValue<T>(ValueAccess<T> access)
    {
        return new NumberStructAccess<T>(property, structType, access, count, stride);
    }
}

file sealed class StructAccess(
    UnrealProperty property,
    UnrealClass structType) : PropertyAccess(property)
{
    private readonly UnrealClass _structType = structType;

    private ValueError? _structEndError;

    public override ResultError NotValueOf(Type type)
    {
        return _structEndError ??= ValueError.StructEnd(Property.Name, _structType.Name);
    }

    public override Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last)
    {
        return NotValueOf(typeof(object));
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container + Property.Offset, _structType);
    }
}

file sealed class NumberStructAccess<T>(
    UnrealProperty property,
    UnrealClass structType,
    ValueAccess<T> element,
    int count,
    int stride) : ValueAccess<T[]>(property)
{
    private readonly ElementBuffer<T> _buffer = new();

    public override Result<T[]> Read(IMemoryReader memory, Address container, T[]? last)
    {
        return _buffer.Read(memory, element, container + Property.Offset, count, stride, last);
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container + Property.Offset, structType);
    }
}
