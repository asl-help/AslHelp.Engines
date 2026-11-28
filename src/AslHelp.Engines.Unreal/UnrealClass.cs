using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a class of a running game with its own and its inherited properties.
/// </summary>
/// <remarks>
///     Reads the properties at the 1st <see cref="FindProperty"/> or <see cref="Properties"/> and keeps them,
///     because the engine doesn't change a loaded class.
///     <see cref="UnrealGame"/> replaces the instance when the game frees the class and another class takes its address.
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealClass
{
    private readonly ClassReader _classReader;
    private Dictionary<string, UnrealProperty>? _properties;
    private Dictionary<string, UnrealProperty>? _propertiesIgnoringCase;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealClass"/> class.
    /// </summary>
    /// <param name="classReader">The reader that enumerates the properties.</param>
    /// <param name="address">The address of the class.</param>
    /// <param name="name">The name of the class.</param>
    internal UnrealClass(ClassReader classReader, Address address, string name)
    {
        _classReader = classReader;
        Address = address;
        Name = name;
    }

    /// <summary>
    ///     Gets the address of the class.
    /// </summary>
    public Address Address { get; }

    /// <summary>
    ///     Gets the name of the class, like <c>Actor</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the properties that this class declares itself, without the properties of its super classes, in declaration
    ///     order or a failure when a read of the properties fails.
    /// </summary>
    /// <remarks>
    ///     <see cref="FindProperty"/> also finds the properties of the super classes. Keeps the list once it read and reads
    ///     again after a failure.
    /// </remarks>
    public Result<IReadOnlyList<UnrealProperty>> Properties => field is { IsOk: true, Value: not null } ? field : field = ReadProperties();

    /// <summary>
    ///     Finds the property called <paramref name="name"/> on this class or a class up its super chain.
    /// </summary>
    /// <remarks>
    ///     Ignores case like the engine, but a property with the exact name wins.
    ///     Returns the property of the most derived class when 2 classes declare the same name.
    /// </remarks>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    ///     The property or a failure when no class on the chain declares it or a read of the properties fails.
    ///     Doesn't keep a failed read, so the next call reads again.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealProperty> FindProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!FindPropertyOrNull(name).TryUnwrap(out var property, out var error))
        {
            return error;
        }

        return property is null ? ClassError.PropertyNotFound(Name, name) : property;
    }

    /// <summary>
    ///     Lists the live objects of exactly this class.
    /// </summary>
    /// <remarks>
    ///     Leaves out objects of derived classes and the class default object, whose name starts with <c>Default__</c>.
    ///     Searches the object array like <see cref="UnrealGame.FindClass"/>, then reads the class pointer and the name of each object of the class.
    ///     A path from an object fails once the game frees it.
    /// </remarks>
    /// <returns>
    ///     The objects in array order, an empty list for a class without objects
    ///     or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealObject>> GetInstances()
    {
        return _classReader.InstancesOf(this);
    }

    /// <summary>
    ///     Lists the loaded classes that derive from this class directly or over other classes, without this class.
    /// </summary>
    /// <remarks>
    ///     Searches the object array like <see cref="UnrealGame.FindClass"/> and reads the super class of each class once.
    /// </remarks>
    /// <returns>
    ///     The classes in array order or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealClass>> GetDerivedClasses()
    {
        return _classReader.DerivedFrom(Address);
    }

    /// <summary>
    ///     Returns the name of the class.
    /// </summary>
    /// <returns>
    ///     The name.
    /// </returns>
    public override string ToString()
    {
        return Name;
    }

    /// <summary>
    ///     Finds the property called <paramref name="name"/> on this class or a class up its super chain.
    /// </summary>
    /// <param name="name">The name of the property. A name that only matches in another case finds the property too.</param>
    /// <returns>
    ///     The property, <see langword="null"/> when no class on the chain declares it or a failure when a read of the properties fails.
    /// </returns>
    internal Result<UnrealProperty?> FindPropertyOrNull(string name)
    {
        if (_properties is null || _propertiesIgnoringCase is null)
        {
            Dictionary<string, UnrealProperty> properties = [];
            Dictionary<string, UnrealProperty> propertiesIgnoringCase = [with(StringComparer.OrdinalIgnoreCase)];

            foreach (var item in _classReader.PropertiesOf(Address))
            {
                if (!item.TryUnwrap(out var property, out var error))
                {
                    return error;
                }

                // Keep the 1st, because the enumeration starts at the most derived class.
                if (!properties.ContainsKey(property.Name))
                {
                    properties[property.Name] = property;
                }

                if (!propertiesIgnoringCase.ContainsKey(property.Name))
                {
                    propertiesIgnoringCase[property.Name] = property;
                }
            }

            (_properties, _propertiesIgnoringCase) = (properties, propertiesIgnoringCase);
        }

        return _properties.TryGetValue(name, out var found) || _propertiesIgnoringCase.TryGetValue(name, out found) ? found : null;
    }

    private Result<IReadOnlyList<UnrealProperty>> ReadProperties()
    {
        List<UnrealProperty> properties = [];

        foreach (var item in _classReader.OwnPropertiesOf(Address))
        {
            if (!item.TryUnwrap(out var property, out var error))
            {
                return error;
            }

            properties.Add(property);
        }

        return (UnrealProperty[])[.. properties];
    }
}
