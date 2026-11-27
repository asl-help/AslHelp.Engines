using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the reads of a collection whose elements don't read as values, like an array of structs,
///     which a path only goes on through with an index or <c>Count</c>.
/// </summary>
/// <param name="property">The collection property.</param>
/// <param name="collection">The reads of the elements.</param>
/// <param name="elementClass">The class of the elements, like <c>StructProperty</c>, for the messages.</param>
internal sealed class CollectionStepAccess(
    UnrealProperty property,
    CollectionAccess collection,
    string elementClass) : PropertyAccess(property)
{
    private readonly CollectionAccess _collection = collection;
    private readonly string _elementClass = elementClass;

    private CollectionError? _notValueError;

    /// <inheritdoc/>
    public override ResultError NotValueOf(Type type)
    {
        return _notValueError ??= CollectionError.ElementsNotValue(Property.Name, Property.ClassName, _elementClass);
    }

    /// <inheritdoc/>
    public override Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last)
    {
        return NotValueOf(typeof(object));
    }

    /// <inheritdoc/>
    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container, null, _collection);
    }
}
