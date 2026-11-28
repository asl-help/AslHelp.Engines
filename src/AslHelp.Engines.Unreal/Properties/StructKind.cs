using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a struct in an object, which a path goes into by the offset of the property without a pointer read.
/// </summary>
/// <remarks>
///     A struct reads as a <see cref="StructValue"/> with its members by name in offset order, each in the type of its kind.
///     A struct whose members are all single numbers of 1 class right after each other also reads as an array of that number.
///     <c>FVector</c> reads as <see cref="float"/>[] or <see cref="double"/>[], depending on the class of its members.
///     <c>FVector</c> and <c>FRotator</c> also read as <see cref="Vector"/> and <see cref="Rotator"/> with doubles on every version.
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

        if (numbers is null)
        {
            return new StructAccess(property, type, classReader);
        }

        return numbers[0].Bind().TryUnwrap(out var first, out error)
            ? first.Accept(new NumberStructFactory(property, type, classReader, numbers))
            : error;
    }

    // Return the members in offset order when every member is 1 number of the same class
    // and each sits right after the one before it, otherwise null.
    private static Result<IReadOnlyList<UnrealProperty>?> FindNumbers(ClassReader classReader, UnrealClass type)
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
                return Result.Ok<IReadOnlyList<UnrealProperty>?>(null);
            }

            // Keep a struct with a static array member like float V[3] a plain struct, because the array would only get its 1st element.
            if (!classReader.ArrayDimOf(member.Address).TryUnwrap(out int arrayDim, out error))
            {
                return error;
            }

            if (arrayDim != 1)
            {
                return Result.Ok<IReadOnlyList<UnrealProperty>?>(null);
            }

            members.Add(member);
        }

        if (members.Count == 0)
        {
            return Result.Ok<IReadOnlyList<UnrealProperty>?>(null);
        }

        members.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Offset != i * members[0].ElementSize)
            {
                return Result.Ok<IReadOnlyList<UnrealProperty>?>(null);
            }
        }

        return Result.Ok<IReadOnlyList<UnrealProperty>?>(members);
    }
}

file sealed class NumberStructFactory(
    UnrealProperty property,
    UnrealClass structType,
    ClassReader classReader,
    IReadOnlyList<UnrealProperty> members) : AccessVisitor<Result<PropertyAccess>>
{
    public override Result<PropertyAccess> VisitOther(PropertyAccess access)
    {
        return new StructAccess(property, structType, classReader);
    }

    public override Result<PropertyAccess> VisitValue<T>(ValueAccess<T> access)
    {
        return new NumberStructAccess<T>(property, structType, access, members);
    }
}

file sealed class StructAccess(
    UnrealProperty property,
    UnrealClass structType,
    ClassReader classReader) : PropertyAccess(property)
{
    private readonly ClassReader _classReader = classReader;
    private readonly UnrealClass _structType = structType;

    private StructMembers? _members;
    private (Type Type, ValueError Error)? _wrongTypeError;

    // A member that fails every read fails every read of the whole struct too.
    public override bool AlwaysFails => _members is { AlwaysFails: true };

    public override bool IsStruct => true;

    public override ResultError NotValueOf(Type type)
    {
        if (_wrongTypeError is { } kept && kept.Type == type)
        {
            return kept.Error;
        }

        var error = ValueError.WrongType(Property.Name, Property.ClassName, nameof(StructValue), type.Name);
        _wrongTypeError = (type, error);

        return error;
    }

    public override Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last)
    {
        return ReadStruct(memory, container, last as StructValue).TryUnwrap(out var value, out var error) ? Result.Ok<object?>(value) : error;
    }

    public override Result<StructValue> ReadStruct(IMemoryReader memory, Address container, StructValue? last)
    {
        if (_members is null)
        {
            List<UnrealProperty> members = [];

            foreach (var item in _classReader.PropertiesOf(_structType.Address))
            {
                if (!item.TryUnwrap(out var member, out var error))
                {
                    return error;
                }

                members.Add(member);
            }

            if (!StructMembers.Bind(_structType.Name, members).TryUnwrap(out _members, out var bindError))
            {
                return bindError;
            }
        }

        return _members.Read(memory, container + Property.Offset, last);
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
    IReadOnlyList<UnrealProperty> members) : ValueAccess<T[]>(property)
{
    private readonly ElementBuffer<T> _buffer = new();
    private readonly StructLift _lift = FindLift(structType.Name, members);
    private readonly T[] _parts = new T[3];

    private StructMembers? _members;

    public override bool IsStruct => true;

    public override bool AlsoReadsAs<TValue>()
    {
        return base.AlsoReadsAs<TValue>()
            || (typeof(TValue) == typeof(Vector) && _lift == StructLift.Vector)
            || (typeof(TValue) == typeof(Rotator) && _lift == StructLift.Rotator);
    }

    public override Result<TValue> ReadAs<TValue>(IMemoryReader memory, Address container, TValue? last)
        where TValue : default
    {
        if (typeof(TValue) == typeof(Vector) && _lift == StructLift.Vector)
        {
            return ReadParts(memory, container).TryUnwrap(out var parts, out var error)
                ? (TValue)(object)new Vector(parts.First, parts.Second, parts.Third)
                : error;
        }

        if (typeof(TValue) == typeof(Rotator) && _lift == StructLift.Rotator)
        {
            return ReadParts(memory, container).TryUnwrap(out var parts, out var error)
                ? (TValue)(object)new Rotator(parts.First, parts.Second, parts.Third)
                : error;
        }

        return base.ReadAs(memory, container, last);
    }

    public override Result<T[]> Read(IMemoryReader memory, Address container, T[]? last)
    {
        return _buffer.Read(memory, element, container + Property.Offset, members.Count, members[0].ElementSize, last);
    }

    public override Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last)
    {
        return ReadStruct(memory, container, last as StructValue).TryUnwrap(out var value, out var error) ? Result.Ok<object?>(value) : error;
    }

    public override Result<StructValue> ReadStruct(IMemoryReader memory, Address container, StructValue? last)
    {
        if (_members is null && !StructMembers.Bind(structType.Name, members).TryUnwrap(out _members, out var error))
        {
            return error;
        }

        return _members!.Read(memory, container + Property.Offset, last);
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container + Property.Offset, structType);
    }

    // Lift only Vector and Rotator with float or double members in their order, because only those are FVector and FRotator of the engine.
    private static StructLift FindLift(string name, IReadOnlyList<UnrealProperty> members)
    {
        if (members.Count != 3 || (typeof(T) != typeof(float) && typeof(T) != typeof(double)))
        {
            return StructLift.None;
        }

        return name switch
        {
            "Vector" when HasNames(members, "X", "Y", "Z") => StructLift.Vector,
            "Rotator" when HasNames(members, "Pitch", "Yaw", "Roll") => StructLift.Rotator,
            _ => StructLift.None,
        };
    }

    private static bool HasNames(IReadOnlyList<UnrealProperty> members, string first, string second, string third)
    {
        return members[0].Name == first && members[1].Name == second && members[2].Name == third;
    }

    private static double ToDouble(T part)
    {
        return typeof(T) == typeof(float) ? (float)(object)part! : (double)(object)part!;
    }

    // Read the 3 members in 1 read into a kept buffer, so a lifted read doesn't allocate and doesn't mix 2 frames of the game.
    private Result<(double First, double Second, double Third)> ReadParts(IMemoryReader memory, Address container)
    {
        if (element.ReadMany(memory, container + Property.Offset, members[0].ElementSize, _parts, default).TryUnwrapErr(out var error))
        {
            return error;
        }

        return (ToDouble(_parts[0]), ToDouble(_parts[1]), ToDouble(_parts[2]));
    }
}

file enum StructLift
{
    None,
    Vector,
    Rotator,
}

// Read the members of 1 struct type boxed, with the names shared by every value it reads.
file sealed class StructMembers
{
    private readonly PropertyAccess[] _accesses;
    private readonly string[] _names;
    private readonly object?[] _scratch;
    private readonly string _typeName;

    private StructMembers(string typeName, string[] names, PropertyAccess[] accesses)
    {
        _typeName = typeName;
        _names = names;
        _accesses = accesses;
        _scratch = new object?[accesses.Length];
        AlwaysFails = Array.Exists(accesses, static access => access.AlwaysFails);
    }

    public bool AlwaysFails { get; }

    // Bind each member in offset order. A member of a kind the library doesn't read binds to an access that fails each read.
    public static Result<StructMembers> Bind(string typeName, IReadOnlyList<UnrealProperty> members)
    {
        var ordered = members.OrderBy(static member => member.Offset).ToArray();
        var accesses = new PropertyAccess[ordered.Length];

        for (int i = 0; i < ordered.Length; i++)
        {
            if (!ordered[i].Bind().TryUnwrap(out accesses[i]!, out var error))
            {
                return error;
            }
        }

        return new StructMembers(typeName, [.. ordered.Select(static member => member.Name)], accesses);
    }

    public Result<StructValue> Read(IMemoryReader memory, Address structAddress, StructValue? last)
    {
        // Pass on the boxes of last only when it comes from this struct type, because a class change can bring another struct.
        var lastValues = last is not null && ReferenceEquals(last.Names, _names) ? last.Values : null;
        bool same = lastValues is not null;

        for (int i = 0; i < _accesses.Length; i++)
        {
            object? lastValue = lastValues?[i];

            if (!_accesses[i].ReadBoxed(memory, structAddress, lastValue).TryUnwrap(out object? value, out var error))
            {
                return error;
            }

            _scratch[i] = value;
            same &= ReferenceEquals(value, lastValue);
        }

        return same ? last! : new StructValue(_typeName, _names, [.. _scratch]);
    }
}
