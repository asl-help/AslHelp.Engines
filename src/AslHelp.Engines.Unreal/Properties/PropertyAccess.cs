using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the reads of 1 property after its kind read what it needs, like the mask of a bool.
/// </summary>
/// <remarks>
///     A value kind derives from <see cref="ValueAccess{T}"/>. A kind that a path goes on through overrides <see cref="Step"/>.
///     A collection steps into a <see cref="Container"/> with its <see cref="CollectionAccess"/>, which an index or <c>Count</c> reads.
///     Keeps the failures that don't depend on memory, like a wrong type, because a watcher fails the same way on every tick.
///     Isn't thread-safe.
/// </remarks>
/// <param name="property">The property, for the messages.</param>
internal abstract class PropertyAccess(
    UnrealProperty property)
{
    private ValueError? _notContainerError;

    /// <summary>
    ///     Gets the property that this access reads.
    /// </summary>
    protected UnrealProperty Property { get; } = property;

    /// <summary>
    ///     Calls the method of <paramref name="visitor"/> for this access, so a collection creates the typed reads of its elements.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of a visit.</typeparam>
    /// <param name="visitor">The visitor.</param>
    /// <returns>
    ///     The result of the visit.
    /// </returns>
    public virtual TResult Accept<TResult>(AccessVisitor<TResult> visitor)
    {
        return visitor.VisitOther(this);
    }

    /// <summary>
    ///     Returns the failure for a read of this property as a value of <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type of the read.</param>
    /// <returns>
    ///     The failure, the same instance for the same type.
    /// </returns>
    public abstract ResultError NotValueOf(Type type);

    /// <summary>
    ///     Reads the value of this property in <paramref name="container"/> as the type of its kind.
    /// </summary>
    /// <remarks>
    ///     Returns <paramref name="last"/> itself when the value equals it, so a value that doesn't change doesn't allocate a box.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the value.</param>
    /// <param name="last">The value of the last read in its own type or <see langword="null"/>.</param>
    /// <returns>
    ///     The value or a failure.
    /// </returns>
    public abstract Result<object?> ReadBoxed(IMemoryReader memory, Address container, object? last);

    /// <summary>
    ///     Moves a path from <paramref name="container"/> into the value of this property.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the value.</param>
    /// <returns>
    ///     The object or struct that the value is or a failure when the kind doesn't have members or a read fails.
    /// </returns>
    public virtual Result<Container> Step(IMemoryReader memory, Address container)
    {
        return _notContainerError ??= ValueError.NotContainer(Property.Name, Property.ClassName);
    }
}
