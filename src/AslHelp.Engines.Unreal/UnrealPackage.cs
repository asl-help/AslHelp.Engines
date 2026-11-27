using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Represents a loaded package of a running game, which contains classes.
/// </summary>
/// <remarks>
///     <see cref="UnrealGame"/> replaces the instance when the game frees the package and another package takes its address.
///     Isn't thread-safe.
/// </remarks>
public sealed class UnrealPackage
{
    private readonly ClassReader _classReader;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnrealPackage"/> class.
    /// </summary>
    /// <param name="classReader">The reader that lists the classes.</param>
    /// <param name="address">The address of the package.</param>
    /// <param name="name">The name of the package.</param>
    internal UnrealPackage(ClassReader classReader, Address address, string name)
    {
        _classReader = classReader;
        Address = address;
        Name = name;
    }

    /// <summary>
    ///     Gets the address of the package.
    /// </summary>
    public Address Address { get; }

    /// <summary>
    ///     Gets the name of the package, like <c>/Script/Engine</c> or <c>Engine</c> on Unreal Engine 3.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Finds the loaded class of the package called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally, like <see cref="UnrealGame.FindClass"/>.
    /// </remarks>
    /// <param name="name">The name of the class without a prefix, like <c>Actor</c>.</param>
    /// <returns>
    ///     The class or a failure when no loaded class of the package has the name or a read of the search fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealClass> FindClass(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!GetClasses().TryUnwrap(out var classes, out var error))
        {
            return error;
        }

        foreach (var klass in classes)
        {
            if (string.Equals(klass.Name, name, StringComparison.Ordinal))
            {
                return klass;
            }
        }

        return PackageError.ClassNotFound(Name, name);
    }

    /// <summary>
    ///     Lists the loaded classes of the package, which are the classes whose outer object is the package.
    /// </summary>
    /// <remarks>
    ///     Searches the object array like <see cref="UnrealGame.FindClass"/> and reads the outer object of each class once.
    /// </remarks>
    /// <returns>
    ///     The classes in array order or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealClass>> GetClasses()
    {
        return _classReader.ClassesOf(Address);
    }

    /// <summary>
    ///     Returns the name of the package.
    /// </summary>
    /// <returns>
    ///     The name.
    /// </returns>
    public override string ToString()
    {
        return Name;
    }
}
