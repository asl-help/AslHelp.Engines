using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of the properties of a struct where a property isn't an object, which is how Unreal Engine 4.25 and later store properties.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the game can free a property and reuse its address.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="nameTable">The table that the name ids of the properties and their field classes point into, in any storage shape.</param>
/// <param name="structLayout">The offsets inside a struct.</param>
/// <param name="fieldLayout">The offsets inside a property that come from its base field.</param>
/// <param name="classLayout">The offsets inside a field class.</param>
/// <param name="propertyLayout">The offsets inside a property.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/>, <paramref name="nameTable"/>, <paramref name="structLayout"/>, <paramref name="fieldLayout"/>,
///     <paramref name="classLayout"/> or <paramref name="propertyLayout"/> is <see langword="null"/>.
/// </exception>
public sealed class FieldPropertyReader(
    IMemoryReader memory,
    INameTable nameTable,
    FieldStructLayout structLayout,
    FieldPropertyLayout fieldLayout,
    FieldClassLayout classLayout,
    PropertyLayout propertyLayout) : IPropertyReader
{
    private const int MaxProperties = 4096;

    private readonly FieldClassLayout _classLayout = classLayout ?? throw new ArgumentNullException(nameof(classLayout));
    private readonly FieldPropertyLayout _fieldLayout = fieldLayout ?? throw new ArgumentNullException(nameof(fieldLayout));
    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly INameTable _nameTable = nameTable ?? throw new ArgumentNullException(nameof(nameTable));
    private readonly PropertyLayout _propertyLayout = propertyLayout ?? throw new ArgumentNullException(nameof(propertyLayout));
    private readonly FieldStructLayout _structLayout = structLayout ?? throw new ArgumentNullException(nameof(structLayout));

    /// <summary>
    ///     Enumerates the properties that <paramref name="type"/> declares itself in chain order.
    /// </summary>
    /// <remarks>
    ///     Reads only the next pointers and skips the class check, because ChildProperties lists properties only.
    ///     Fails after 4096 properties, because a damaged chain can loop.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The addresses of the properties, then a failure if a read failed or the chain goes on past 4096 properties.
    /// </returns>
    public IEnumerable<Result<Address>> PropertiesOf(Address type)
    {
        var listAddress = type + _structLayout.ChildProperties;

        if (!_memory.ReadPointer(listAddress).TryUnwrap(out var property, out var error))
        {
            yield return PropertyError.ListUnreadable(type, listAddress, _structLayout.ChildProperties, error);
            yield break;
        }

        for (int count = 0; count < MaxProperties && property != Address.Zero; count++)
        {
            yield return property;

            var nextAddress = property + _fieldLayout.Next;

            if (!_memory.ReadPointer(nextAddress).TryUnwrap(out var next, out error))
            {
                yield return PropertyError.NextUnreadable(property, nextAddress, _fieldLayout.Next, error);
                yield break;
            }

            property = next;
        }

        if (property != Address.Zero)
        {
            yield return PropertyError.ListPastLimit(type, MaxProperties, _fieldLayout.Next);
        }
    }

    /// <summary>
    ///     Reads the name of <paramref name="prop"/> as the engine prints it.
    /// </summary>
    /// <remarks>
    ///     Returns the base name for name number 0 and the base name, <c>_</c> and the number minus 1 for a number above 0.
    /// </remarks>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The name or a failure with the property and the failed read when a read fails or the name number is negative.
    /// </returns>
    public Result<string> NameOf(Address prop)
    {
        var address = prop + _fieldLayout.Name;

        return ReadName(address).TryUnwrap(out string? name, out var error)
            ? name
            : PropertyError.NameUnreadable(prop, address, _fieldLayout.Name, error);
    }

    /// <summary>
    ///     Reads the name of the field class of <paramref name="prop"/>, such as <c>IntProperty</c>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The class name or a failure with the property and the failed read when a read fails, the class pointer is 0 or the name number is negative.
    /// </returns>
    public Result<string> ClassNameOf(Address prop)
    {
        var classAddress = prop + _fieldLayout.Class;

        if (!_memory.ReadPointer(classAddress).TryUnwrap(out var klass, out var error))
        {
            return PropertyError.ClassUnreadable(prop, classAddress, _fieldLayout.Class, error);
        }

        if (klass == Address.Zero)
        {
            return PropertyError.ClassNull(prop, _fieldLayout.Class);
        }

        var nameAddress = klass + _classLayout.Name;

        return ReadName(nameAddress).TryUnwrap(out string? className, out error)
            ? className
            : PropertyError.ClassNameUnreadable(prop, klass, nameAddress, _classLayout.Name, error);
    }

    /// <inheritdoc/>
    public Result<int> ArrayDimOf(Address prop)
    {
        return PropertyMembers.ReadArrayDim(_memory, prop, _propertyLayout);
    }

    /// <inheritdoc/>
    public Result<int> ElementSizeOf(Address prop)
    {
        return PropertyMembers.ReadElementSize(_memory, prop, _propertyLayout);
    }

    /// <inheritdoc/>
    public Result<int> OffsetOf(Address prop)
    {
        return PropertyMembers.ReadOffset(_memory, prop, _propertyLayout);
    }

    private Result<string> ReadName(Address address)
    {
        if (!NameMembers.Read(_memory, address).TryUnwrap(out var name, out var error))
        {
            return error;
        }

        (int id, int number) = name;

        if (number < 0)
        {
            return PropertyError.NameNumberNegative(address, number);
        }

        return _nameTable.Read(id).TryUnwrap(out string? text, out error)
            ? NameMembers.Join(text, number)
            : error;
    }
}
