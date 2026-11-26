using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a class of a running game with its own and its inherited properties.
/// </summary>
/// <remarks>
///     Reads the properties at the 1st <see cref="FindProperty"/> and keeps them, because the engine doesn't change a loaded class.
///     <see cref="UnrealGame"/> replaces the instance when the game frees the class and another class takes its address.
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealClass
{
    private readonly ClassReader _classReader;
    private Dictionary<string, UnrealProperty>? _properties;

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
    ///     Finds the property called <paramref name="name"/> on this class or a class up its super chain.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally. Returns the property of the most derived class when 2 classes declare the same name.
    /// </remarks>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    ///     The property or a failure when no class on the chain declares it or a read of the properties fails.
    ///     A failed read doesn't get kept, so the next call reads again.
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
    ///     Finds the property called <paramref name="name"/> on this class or a class up its super chain.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    ///     The property, <see langword="null"/> when no class on the chain declares it or a failure when a read of the properties fails.
    /// </returns>
    internal Result<UnrealProperty?> FindPropertyOrNull(string name)
    {
        if (_properties is null)
        {
            Dictionary<string, UnrealProperty> properties = [];

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
            }

            _properties = properties;
        }

        return _properties.TryGetValue(name, out var found) ? found : null;
    }
}
