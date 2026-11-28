using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Names;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Engines.Unreal.Properties;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Structs;

/// <summary>
///     Provides the classes, structs and packages of a running game, the properties of a class and its super classes and the live objects of a class.
/// </summary>
/// <remarks>
///     Keeps every class and package it finds by address, so each gets 1 <see cref="UnrealClass"/> or <see cref="UnrealPackage"/> for the game.
///     Finds classes and packages by name through a <see cref="ClassIndex"/> of the object array.
///     Isn't thread-safe.
/// </remarks>
/// <param name="memory">The memory of the game.</param>
/// <param name="nameTable">The name table of the game.</param>
/// <param name="objectReader">The reader of the names and the classes of objects.</param>
/// <param name="structReader">The reader of the super chains of classes.</param>
/// <param name="propertyReader">The reader of the properties in the storage shape of the game.</param>
/// <param name="objectArray">The object array to search for classes, which weak pointers also read.</param>
/// <param name="kindLayouts">The offsets that the value kinds read.</param>
/// <param name="bounds">The largest string and collection that a read takes. The default is <see cref="ValueBounds.Default"/>.</param>
/// <param name="serialNumberBlocks">The serial numbers apart from the object array, which weak pointers read on 4.0 to 4.10.</param>
internal sealed class ClassReader(
    IMemoryReader memory,
    INameTable nameTable,
    ObjectReader objectReader,
    StructReader structReader,
    IPropertyReader propertyReader,
    IObjectArray objectArray,
    KindLayouts kindLayouts,
    ValueBounds? bounds = null,
    SerialNumberBlocks? serialNumberBlocks = null)
{
    /// <summary>
    ///     The number of levels of a super chain after which a read fails, because a damaged chain can loop.
    /// </summary>
    internal const int MaxSuperDepth = 64;

    private readonly Dictionary<Address, UnrealClass> _classes = [];
    private readonly ClassIndex _classIndex = new(objectReader, structReader, objectArray);
    private readonly Dictionary<Address, UnrealPackage> _packages = [];
    private readonly IPropertyReader _propertyReader = propertyReader;
    private readonly StructReader _structReader = structReader;

    // Keep the version of the loaded classes at which no derived class declared a member, because a failed step isn't kept
    // and a script that tries it again would list and read every derived class again.
    private readonly Dictionary<(Address Class, string Member), int> _undeclaredAt = [];

    /// <summary>
    ///     Gets the largest string and collection that a read takes.
    /// </summary>
    public ValueBounds Bounds { get; } = bounds ?? ValueBounds.Default;

    /// <summary>
    ///     Gets the offsets that the value kinds read.
    /// </summary>
    public KindLayouts KindLayouts { get; } = kindLayouts;

    /// <summary>
    ///     Gets the memory of the game.
    /// </summary>
    public IMemoryReader Memory { get; } = memory;

    /// <summary>
    ///     Gets the name table of the game.
    /// </summary>
    public INameTable NameTable { get; } = nameTable;

    /// <summary>
    ///     Gets the object array of the game.
    /// </summary>
    public IObjectArray ObjectArray { get; } = objectArray;

    /// <summary>
    ///     Gets the reader of the names, the classes and the outer objects of objects.
    /// </summary>
    public ObjectReader ObjectReader { get; } = objectReader;

    /// <summary>
    ///     Gets the serial numbers apart from the object array or <see langword="null"/> when the object array has them or the game has none.
    /// </summary>
    public SerialNumberBlocks? SerialNumberBlocks { get; } = serialNumberBlocks;

    /// <summary>
    ///     Reads the number of elements of the property at <paramref name="prop"/>, like 3 for <c>float V[3]</c>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The number or a failure when the read fails.
    /// </returns>
    public Result<int> ArrayDimOf(Address prop)
    {
        return _propertyReader.ArrayDimOf(prop);
    }

    /// <summary>
    ///     Reads the class of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The class or a failure when <paramref name="obj"/> is <see cref="Address.Zero"/>, its class pointer is 0 or a read fails.
    /// </returns>
    public Result<UnrealClass> ClassOf(Address obj)
    {
        if (!ObjectReader.ClassOf(obj).TryUnwrap(out var klass, out var error))
        {
            return error;
        }

        if (klass == Address.Zero)
        {
            return ClassError.Null(obj, ObjectReader.ClassOffset);
        }

        return ObjectReader.NameOf(klass).TryUnwrap(out string? name, out error)
            ? Keep(klass, name)
            : error;
    }

    /// <summary>
    ///     Finds the loaded class called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Ignores case and lets the exact name win. <see cref="ClassIndex.FindClass"/> lists what a search reads.
    /// </remarks>
    /// <param name="name">The name of the class, like <c>Actor</c> or <c>BP_Player_C</c>.</param>
    /// <returns>
    ///     The class or a failure when no loaded class has the name or a read of the search fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealClass> Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _classIndex.FindClass(name).TryUnwrap(out var klass, out var error) ? Keep(klass.Address, klass.Name) : error;
    }

    /// <summary>
    ///     Finds the loaded package called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Ignores case, like the engine. Searches like <see cref="Find"/>.
    /// </remarks>
    /// <param name="name">The name of the package, like <c>/Script/Engine</c> or <c>Engine</c>.</param>
    /// <returns>
    ///     The package or a failure when no loaded package has the name or a read of the search fails.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    public Result<UnrealPackage> FindPackage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _classIndex.FindPackage(name).TryUnwrap(out var package, out var error) ? KeepPackage(package.Address, package.Name) : error;
    }

    /// <summary>
    ///     Lists the loaded packages in array order.
    /// </summary>
    /// <returns>
    ///     The packages or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealPackage>> GetPackages()
    {
        if (!_classIndex.Packages().TryUnwrap(out var packages, out var error))
        {
            return error;
        }

        return Result.Ok<IReadOnlyList<UnrealPackage>>([.. packages.Select(package => KeepPackage(package.Address, package.Name))]);
    }

    /// <summary>
    ///     Lists the loaded classes of <paramref name="package"/> in array order.
    /// </summary>
    /// <param name="package">The address of the package.</param>
    /// <returns>
    ///     The classes or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealClass>> ClassesOf(Address package)
    {
        return Classes(_classIndex.ClassesOf(package));
    }

    /// <summary>
    ///     Lists the loaded classes that derive from <paramref name="klass"/> in array order.
    /// </summary>
    /// <param name="klass">The address of the class.</param>
    /// <returns>
    ///     The classes or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealClass>> DerivedFrom(Address klass)
    {
        return Classes(_classIndex.DerivedFrom(klass));
    }

    /// <summary>
    ///     Checks whether a loaded class that derives from <paramref name="klass"/> declares <paramref name="member"/> itself.
    /// </summary>
    /// <remarks>
    ///     Keeps a <see langword="false"/> until the loaded classes change.
    /// </remarks>
    /// <param name="klass">The class.</param>
    /// <param name="member">The name of the property, compared without case.</param>
    /// <returns>
    ///     Whether a derived class declares the member or <see langword="null"/> when a read fails.
    /// </returns>
    public bool? DerivedDeclares(UnrealClass klass, string member)
    {
        if (!_classIndex.ReadVersion().TryUnwrap(out int version, out _))
        {
            return null;
        }

        var key = (klass.Address, member);

        if (_undeclaredAt.TryGetValue(key, out int undeclaredAt) && undeclaredAt == version)
        {
            return false;
        }

        if (!DerivedFrom(klass.Address).TryUnwrap(out var derived, out _))
        {
            return null;
        }

        foreach (var derivedClass in derived)
        {
            if (!derivedClass.Properties.TryUnwrap(out var properties, out _))
            {
                return null;
            }

            foreach (var property in properties)
            {
                if (string.Equals(property.Name, member, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        _undeclaredAt[key] = version;

        return false;
    }

    /// <summary>
    ///     Lists the live objects of exactly <paramref name="klass"/> in array order.
    /// </summary>
    /// <param name="klass">The class.</param>
    /// <returns>
    ///     The objects or a failure when the array doesn't read or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<UnrealObject>> InstancesOf(UnrealClass klass)
    {
        if (!_classIndex.InstancesOf(klass.Address).TryUnwrap(out var instances, out var error))
        {
            return error;
        }

        UnrealObject[] objects = [.. instances.Select(instance => new UnrealObject(this, instance.Address, klass, instance.Name))];

        return Result.Ok<IReadOnlyList<UnrealObject>>(objects);
    }

    /// <summary>
    ///     Enumerates the properties that the class at <paramref name="type"/> declares itself, without the properties of its super classes.
    /// </summary>
    /// <remarks>
    ///     Reads each property when the enumeration moves to it. Ends at the 1st read that fails, with the failure as the last item.
    /// </remarks>
    /// <param name="type">The address of the class.</param>
    /// <returns>
    ///     The properties in declaration order, then a failure if a read failed.
    /// </returns>
    public IEnumerable<Result<UnrealProperty>> OwnPropertiesOf(Address type)
    {
        foreach (var item in _propertyReader.PropertiesOf(type))
        {
            if (!item.TryUnwrap(out var prop, out var error) || !ReadProperty(prop).TryUnwrap(out var property, out error))
            {
                yield return error;
                yield break;
            }

            yield return property;
        }
    }

    /// <summary>
    ///     Enumerates the properties of the class at <paramref name="type"/>, then the properties of each class up its super chain.
    /// </summary>
    /// <remarks>
    ///     Reads each property when the enumeration moves to it, so a caller can stop early.
    ///     Stops at a super pointer of 0 and fails after 64 levels, because a damaged chain can loop.
    ///     Ends at the 1st read that fails, with the failure as the last item.
    /// </remarks>
    /// <param name="type">The address of the class.</param>
    /// <returns>
    ///     The properties, then a failure if a read failed or the chain goes on past 64 levels.
    /// </returns>
    public IEnumerable<Result<UnrealProperty>> PropertiesOf(Address type)
    {
        var current = type;

        for (int depth = 0; depth < MaxSuperDepth && current != Address.Zero; depth++)
        {
            ResultError? error;

            foreach (var item in _propertyReader.PropertiesOf(current))
            {
                if (!item.TryUnwrap(out var prop, out error) || !ReadProperty(prop).TryUnwrap(out var property, out error))
                {
                    yield return error;
                    yield break;
                }

                yield return property;
            }

            if (!_structReader.SuperOf(current).TryUnwrap(out current, out error))
            {
                yield return error;
                yield break;
            }
        }

        if (current != Address.Zero)
        {
            yield return StructError.ChainPastLimit(type, MaxSuperDepth, _structReader.SuperOffset);
        }
    }

    /// <summary>
    ///     Reads the class that the object property <paramref name="property"/> declares, like <c>GameModeBase</c> for <c>AuthorityGameMode</c>.
    /// </summary>
    /// <param name="property">The object or weak object property.</param>
    /// <param name="offset">The offset of the pointer to the declared class in the property.</param>
    /// <returns>
    ///     The class or <see langword="null"/> when the pointer is 0 or a read fails.
    /// </returns>
    public UnrealClass? PropertyClassOf(UnrealProperty property, int offset)
    {
        return Memory.ReadPointer(property.Address + offset).TryUnwrap(out var klass, out _)
            && klass != Address.Zero
            && StructAt(klass).TryUnwrap(out var declared, out _)
                ? declared
                : null;
    }

    /// <summary>
    ///     Reads the property at <paramref name="prop"/>.
    /// </summary>
    /// <param name="prop">The address of the property.</param>
    /// <returns>
    ///     The property or a failure when a member of the property doesn't read.
    /// </returns>
    public Result<UnrealProperty> ReadProperty(Address prop)
    {
        if (!_propertyReader.NameOf(prop).TryUnwrap(out string? name, out var error))
        {
            return error;
        }

        if (!_propertyReader.ClassNameOf(prop).TryUnwrap(out string? className, out error))
        {
            return error;
        }

        if (!_propertyReader.OffsetOf(prop).TryUnwrap(out int offset, out error))
        {
            return error;
        }

        return _propertyReader.ElementSizeOf(prop).TryUnwrap(out int elementSize, out error)
            ? new UnrealProperty(this, prop, name, className, offset, elementSize)
            : error;
    }

    /// <summary>
    ///     Reads the struct type at <paramref name="type"/>, like the type of a struct property.
    /// </summary>
    /// <remarks>
    ///     Keeps each struct type by address like a class, so the properties of a struct get read once.
    /// </remarks>
    /// <param name="type">The address of the struct type.</param>
    /// <returns>
    ///     The struct type or a failure when its name doesn't read.
    /// </returns>
    public Result<UnrealClass> StructAt(Address type)
    {
        return ObjectReader.NameOf(type).TryUnwrap(out string? name, out var error)
            ? Keep(type, name)
            : error;
    }

    // Return the kept class at the address while its name matches, otherwise keep a new one in its place.
    private UnrealClass Keep(Address klass, string name)
    {
        if (_classes.TryGetValue(klass, out var kept) && string.Equals(kept.Name, name, StringComparison.Ordinal))
        {
            return kept;
        }

        UnrealClass added = new(this, klass, name);
        _classes[klass] = added;

        return added;
    }

    // Return the kept package at the address while its name matches, otherwise keep a new one in its place.
    private UnrealPackage KeepPackage(Address package, string name)
    {
        if (_packages.TryGetValue(package, out var kept) && string.Equals(kept.Name, name, StringComparison.Ordinal))
        {
            return kept;
        }

        UnrealPackage added = new(this, package, name);
        _packages[package] = added;

        return added;
    }

    private Result<IReadOnlyList<UnrealClass>> Classes(Result<IReadOnlyList<(Address Address, string Name)>> listed)
    {
        if (!listed.TryUnwrap(out var classes, out var error))
        {
            return error;
        }

        return Result.Ok<IReadOnlyList<UnrealClass>>([.. classes.Select(klass => Keep(klass.Address, klass.Name))]);
    }
}
