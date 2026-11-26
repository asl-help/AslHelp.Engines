using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides reads of the properties of a struct where a property is an object, which is how Unreal Engine 4.24 and earlier store properties.
/// </summary>
/// <remarks>
///     Reads the memory on every call, because the game can free a property and reuse its address.
/// </remarks>
/// <param name="memory">The memory to read from.</param>
/// <param name="objectReader">The reader of the names and the classes of the fields.</param>
/// <param name="structReader">The reader of the fields and the super chains of their classes.</param>
/// <param name="propertyLayout">The offsets inside a property.</param>
/// <exception cref="ArgumentNullException">
///     <paramref name="memory"/>, <paramref name="objectReader"/>, <paramref name="structReader"/> or <paramref name="propertyLayout"/> is <see langword="null"/>.
/// </exception>
public sealed class ObjectPropertyReader(
    IMemoryReader memory,
    ObjectReader objectReader,
    StructReader structReader,
    PropertyLayout propertyLayout) : IPropertyReader
{
    // Use the name of UProperty, which every property class derives from.
    private const string PropertyClass = "Property";

    private readonly IMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ObjectReader _objectReader = objectReader ?? throw new ArgumentNullException(nameof(objectReader));
    private readonly PropertyLayout _propertyLayout = propertyLayout ?? throw new ArgumentNullException(nameof(propertyLayout));
    private readonly StructReader _structReader = structReader ?? throw new ArgumentNullException(nameof(structReader));

    /// <summary>
    ///     Enumerates the properties that <paramref name="type"/> declares itself in chain order.
    /// </summary>
    /// <remarks>
    ///     Enumerates <see cref="StructReader.ChildrenOf"/> and keeps a field when its class derives from <c>Property</c>,
    ///     because the chain also has functions and other fields that aren't properties.
    ///     Checks each class once per enumeration, because the fields of a struct share a few classes.
    ///     Fails at a class pointer of 0. Every field has a class, so the 0 comes from a wrong offset or a freed field.
    /// </remarks>
    /// <param name="type">The address of the struct.</param>
    /// <returns>
    ///     The addresses of the properties, then a failure if a read failed.
    /// </returns>
    public IEnumerable<Result<Address>> PropertiesOf(Address type)
    {
        int position = 0;

        // Keep the checks for this enumeration only, because the game can free a class and reuse its address between calls.
        // Create the map at the 1st class, because a failed probe would pay for it on every call.
        Dictionary<Address, bool>? propertyClasses = null;

        foreach (var child in _structReader.ChildrenOf(type))
        {
            position++;

            if (!child.TryUnwrap(out var field))
            {
                yield return child;
                yield break;
            }

            if (!_objectReader.ClassOf(field).TryUnwrap(out var klass, out var error))
            {
                yield return PropertyError.FieldClassUnreadable(type, position, error);
                yield break;
            }

            if (klass == Address.Zero)
            {
                yield return PropertyError.FieldClassNull(type, position, _objectReader.ClassOffset);
                yield break;
            }

            propertyClasses ??= [];

            if (!propertyClasses.TryGetValue(klass, out bool isProperty))
            {
                if (!_structReader.DerivesFrom(klass, PropertyClass).TryUnwrap(out isProperty, out error))
                {
                    yield return PropertyError.FieldClassUnreadable(type, position, error);
                    yield break;
                }

                propertyClasses[klass] = isProperty;
            }

            if (isProperty)
            {
                yield return field;
            }
        }
    }

    /// <inheritdoc/>
    public Result<string> NameOf(Address prop)
    {
        return _objectReader.NameOf(prop);
    }

    /// <summary>
    ///     Reads the name of the class of <paramref name="prop"/>, such as <c>IntProperty</c>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The class name or a failure with the property and the failed read when a read fails or the class pointer is 0.
    /// </returns>
    public Result<string> ClassNameOf(Address prop)
    {
        if (!_objectReader.ClassOf(prop).TryUnwrap(out var klass, out var error))
        {
            return PropertyError.ObjectClassUnreadable(prop, error);
        }

        return _objectReader.NameOf(klass).TryUnwrap(out string? className, out error)
            ? className
            : PropertyError.ObjectClassUnreadable(prop, error);
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
}
