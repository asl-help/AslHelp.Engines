using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the reads of the elements of 1 array, set or map property, which a path reads after the collection.
/// </summary>
/// <remarks>
///     An index or a key finds the address of 1 element, which <see cref="Element"/> then reads or steps into.
///     The member <c>Count</c> reads the number of elements. Isn't thread-safe.
/// </remarks>
/// <param name="property">The collection property, for the messages.</param>
internal abstract class CollectionAccess(
    UnrealProperty property)
{
    /// <summary>
    ///     The name of the member after a collection that reads its number of elements.
    /// </summary>
    public const string CountMember = "Count";

    /// <summary>
    ///     Gets the read of the number of elements, at the address of the object or struct with the collection.
    /// </summary>
    public abstract ValueAccess<int> Count { get; }

    /// <summary>
    ///     Gets the reads of 1 element at the address that <see cref="FindElement"/> returns.
    /// </summary>
    public abstract PropertyAccess Element { get; }

    /// <summary>
    ///     Gets the collection property.
    /// </summary>
    public UnrealProperty Property { get; } = property;

    /// <summary>
    ///     Finds the element of <paramref name="key"/> in the collection in <paramref name="container"/>.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the collection.</param>
    /// <param name="key">The index of an array element or the key of a set or map element.</param>
    /// <returns>
    ///     The address of the element, <see cref="Address.Zero"/> for a key that a set doesn't have or a failure.
    /// </returns>
    public abstract Result<Address> FindElement(IMemoryReader memory, Address container, ElementKey key);
}
