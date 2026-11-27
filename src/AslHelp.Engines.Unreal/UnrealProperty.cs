using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a property of a class or struct and the offset of its value in an object of that class.
/// </summary>
/// <remarks>
///     Reads what its kind needs, like the mask of a bool, at the 1st read of a value and keeps it,
///     because the engine doesn't change a loaded property.
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealProperty
{
    private readonly ClassReader _classReader;

    private PropertyAccess? _access;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealProperty"/> class.
    /// </summary>
    /// <param name="classReader">The reader of the game, which the kind reads its members with.</param>
    /// <param name="address">The address of the property.</param>
    /// <param name="name">The name of the property.</param>
    /// <param name="className">The class of the property without a prefix, like <c>IntProperty</c>.</param>
    /// <param name="offset">The offset of the value in an object.</param>
    /// <param name="elementSize">The size in bytes of 1 element of the value.</param>
    internal UnrealProperty(
        ClassReader classReader,
        Address address,
        string name,
        string className,
        int offset,
        int elementSize)
    {
        _classReader = classReader;
        Address = address;
        Name = name;
        ClassName = className;
        Offset = offset;
        ElementSize = elementSize;
        Kind = PropertyKinds.Find(className);
    }

    /// <summary>
    ///     Gets the address of the property.
    /// </summary>
    public Address Address { get; }

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
    ///     Gets the kind of the value, found by <see cref="ClassName"/>.
    /// </summary>
    internal PropertyKind Kind { get; }

    /// <summary>
    ///     Calls the method of <paramref name="visitor"/> for the kind of this property.
    /// </summary>
    /// <remarks>
    ///     Doesn't read memory.
    /// </remarks>
    /// <typeparam name="TResult">The type of the result of a visit.</typeparam>
    /// <param name="visitor">The visitor.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="visitor"/> is <see langword="null"/>.
    /// </exception>
    public TResult Accept<TResult>(PropertyVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return Kind.Accept(visitor, this);
    }

    /// <summary>
    ///     Returns the kind, the name, the offset and the element size of the property.
    /// </summary>
    /// <returns>
    ///     The text, like <c>IntProperty Hp @ 0x88 (4 bytes)</c>.
    /// </returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{ClassName} {Name} @ 0x{Offset:X} ({ElementSize} bytes)");
    }

    /// <summary>
    ///     Returns the access of the kind of this property.
    /// </summary>
    /// <remarks>
    ///     Keeps the access, also one that fails every read for a missing part of the profile.
    ///     Doesn't keep a failed read of a member of the property, so the next call reads again.
    /// </remarks>
    /// <returns>
    ///     The access or a failure when a member of the property doesn't read.
    /// </returns>
    internal Result<PropertyAccess> Bind()
    {
        if (_access is not null)
        {
            return _access;
        }

        if (!Kind.Bind(this, _classReader).TryUnwrap(out var access, out var error))
        {
            return error;
        }

        _access = access;

        return access;
    }

    /// <summary>
    ///     Reads the value of this property in <paramref name="obj"/> as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The value or a failure when the kind reads another type, <paramref name="obj"/> is <see cref="Address.Zero"/> or a read fails.
    /// </returns>
    internal Result<T> Read<T>(IMemoryReader memory, Address obj)
    {
        if (obj == Address.Zero)
        {
            return ObjectError.NullObject();
        }

        if (!Bind().TryUnwrap(out var access, out var error))
        {
            return error;
        }

        return access is ValueAccess<T> valueAccess
            ? valueAccess.Read(memory, obj, default)
            : access.NotValueOf(typeof(T));
    }
}
