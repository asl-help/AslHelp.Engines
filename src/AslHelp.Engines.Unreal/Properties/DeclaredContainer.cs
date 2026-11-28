namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the class, the struct type or the collection that a property declares, which a step checks when the object isn't there.
/// </summary>
/// <param name="Type">The class or the struct type or <see langword="null"/> for a collection.</param>
/// <param name="IsObject">
///     <see langword="true"/> for the class of an object pointer, whose object can be of a derived class,
///     <see langword="false"/> for a struct type or a collection.
/// </param>
/// <param name="Collection">The collection that an index or <c>Count</c> reads or <see langword="null"/> for a class or a struct type.</param>
internal readonly record struct DeclaredContainer(
    UnrealClass? Type,
    bool IsObject,
    CollectionAccess? Collection = null);
