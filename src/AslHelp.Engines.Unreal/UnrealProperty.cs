using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a property of a class and the offset of its value in an object of that class.
/// </summary>
public sealed class UnrealProperty
{
    // A TSubclassOf property stores a pointer to the class object.
    private const string ClassPropertyClass = "ClassProperty";

    // 5.0 to 5.3 print TObjectPtr properties as ClassPtrProperty and ObjectPtrProperty.
    private const string ClassPtrPropertyClass = "ClassPtrProperty";

    private const string IntPropertyClass = "IntProperty";
    private const string ObjectPropertyClass = "ObjectProperty";
    private const string ObjectPtrPropertyClass = "ObjectPtrProperty";

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealProperty"/> class.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="className">The class of the property without a prefix, like <c>IntProperty</c>.</param>
    /// <param name="offset">The offset of the value in an object.</param>
    /// <param name="elementSize">The size in bytes of 1 element of the value.</param>
    internal UnrealProperty(string name, string className, int offset, int elementSize)
    {
        Name = name;
        ClassName = className;
        Offset = offset;
        ElementSize = elementSize;
        Kind = className switch
        {
            IntPropertyClass => PropertyKind.Int32,
            ClassPropertyClass or ClassPtrPropertyClass or ObjectPropertyClass or ObjectPtrPropertyClass => PropertyKind.Object,
            _ => PropertyKind.Unsupported,
        };
    }

    /// <summary>
    ///     Gets the class of the property without a prefix, like <c>IntProperty</c>.
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    ///     Gets the size in bytes of 1 element of the value.
    /// </summary>
    public int ElementSize { get; }

    /// <summary>
    ///     Gets the name of the property.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the offset of the value in an object.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    ///     Gets the kind of the value, mapped from <see cref="ClassName"/>.
    /// </summary>
    internal PropertyKind Kind { get; }

    /// <summary>
    ///     Calls the method of <paramref name="visitor"/> for the kind of this property.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of a visit.</typeparam>
    /// <param name="visitor">The visitor.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="visitor"/> is <see langword="null"/>.
    /// </exception>
    public TResult Accept<TResult>(IPropertyVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return Kind switch
        {
            PropertyKind.Int32 => visitor.VisitInt32(this),
            PropertyKind.Object => visitor.VisitObject(this),
            _ => visitor.VisitUnsupported(this),
        };
    }

    /// <summary>
    ///     Reads the value of this property in <paramref name="obj"/> as a 4-byte signed integer.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The value or a failure when the property isn't an <c>IntProperty</c>, <paramref name="obj"/> is <see cref="Address.Zero"/>
    ///     or the read fails.
    /// </returns>
    internal Result<int> ReadInt32(IMemoryReader memory, Address obj)
    {
        if (Kind != PropertyKind.Int32)
        {
            return ValueError.WrongKind(Name, ClassName, IntPropertyClass);
        }

        if (obj == Address.Zero)
        {
            return ObjectError.NullObject();
        }

        var address = obj + Offset;

        return memory.Read<int>(address).TryUnwrap(out int value, out var error)
            ? value
            : ValueError.Unreadable(obj, Name, address, error);
    }

    /// <summary>
    ///     Reads the value of this property in <paramref name="obj"/> as an object pointer.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The address of the object it points at or a failure when the property isn't an object property,
    ///     <paramref name="obj"/> is <see cref="Address.Zero"/> or the read fails. A pointer of 0 returns <see cref="Address.Zero"/>.
    /// </returns>
    internal Result<Address> ReadObject(IMemoryReader memory, Address obj)
    {
        if (Kind != PropertyKind.Object)
        {
            return ValueError.WrongKind(Name, ClassName, ObjectPropertyClass);
        }

        if (obj == Address.Zero)
        {
            return ObjectError.NullObject();
        }

        var address = obj + Offset;

        return memory.ReadPointer(address).TryUnwrap(out var target, out var error)
            ? target
            : ValueError.Unreadable(obj, Name, address, error);
    }
}
