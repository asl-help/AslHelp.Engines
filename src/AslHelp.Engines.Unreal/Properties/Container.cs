using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the object, the struct in an object or the collection that a path reads the next member from.
/// </summary>
/// <param name="Address">
///     The address of the object or the struct. For a collection it's the address of the object or struct with the collection.
/// </param>
/// <param name="StructType">The type of the struct or <see langword="null"/> for an object or a collection.</param>
/// <param name="Collection">The collection that an index or <c>Count</c> reads or <see langword="null"/> for an object or a struct.</param>
internal readonly record struct Container(
    Address Address,
    UnrealClass? StructType,
    CollectionAccess? Collection = null);
