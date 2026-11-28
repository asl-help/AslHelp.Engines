using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Objects;
using AslHelp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Engines.Unreal.Structs;

/// <summary>
///     Provides the classes and the packages of the object array of a running game and the live objects of a class.
/// </summary>
/// <remarks>
///     Keeps the object and its class of every array slot, so a search reads the array and only the objects in slots that changed.
///     Keeps the name of every class and every package. Reads the outer object and the super class of a class once, when a listing needs it.
///     Checks the name of a kept class or package each time a lookup returns it, because the game can free an object and reuse its address.
///     Isn't thread-safe.
/// </remarks>
/// <param name="objectReader">The reader of the names, the classes and the outer objects of objects.</param>
/// <param name="structReader">The reader of the super chains of classes.</param>
/// <param name="objectArray">The object array to search.</param>
internal sealed class ClassIndex(
    ObjectReader objectReader,
    StructReader structReader,
    IObjectArray objectArray)
{
    // Skip the class default objects, because the engine creates 1 per class and they aren't objects of the game.
    private const string DefaultObjectPrefix = "Default__";

    // Stop after 4 classes, because the class of a Blueprint class is 2 steps from Class and a wrong offset can loop.
    private const int MaxMetaclassDepth = 4;

    // Compare with Class, because the engine calls the class of every class Class and Class is its own class.
    private const string MetaclassName = "Class";

    private const string PackageClassName = "Package";

    // Keep the slots of the Blueprint classes, because the engine frees Blueprint classes
    // and a search has to check that such a slot still has its class.
    private readonly HashSet<int> _blueprintSlots = [];

    // Keep the address and the slot of the 1st class that a search found for each name.
    private readonly Dictionary<string, (Address Address, int Slot)> _classesByName = [];

    // Keep the outer object and the super class of each class slot once a listing read them, because the engine doesn't change a loaded class.
    private readonly Dictionary<int, (Address Outer, Address Super)> _classLinks = [];

    // Keep the name of the class in each slot, so a slot that changes takes its name out of the kept names.
    private readonly Dictionary<int, string> _classNamesBySlot = [];

    // Keep whether the class of an object derives from Class, because only classes of C++ code derive from Class
    // and the engine doesn't free them.
    private readonly Dictionary<Address, bool> _derivesFromMetaclass = [];

    private Address _metaclass;
    private readonly IObjectArray _objectArray = objectArray;
    private readonly ObjectReader _objectReader = objectReader;
    private Address _packageClass;

    // Keep the name of the package in each slot, so a slot that changes takes its name out of the kept names.
    private readonly Dictionary<int, string> _packageNamesBySlot = [];

    // Keep the address and the slot of the 1st package for each name, compared without case like the engine compares package names.
    private readonly Dictionary<string, (Address Address, int Slot)> _packagesByName = [with(StringComparer.OrdinalIgnoreCase)];

    // Read the slots in front of the class Package again for packages, because packages are in front of it in the array.
    private bool _packagesPending;

    // Keep the names that 2 or more classes have, because the search has to read every slot again when the 1st of them is gone.
    private readonly HashSet<string> _sharedNames = [];

    // Keep the class of the object of each slot, so a search for packages or live objects doesn't read the class pointers again.
    private Address[] _slotClasses = [];

    private int _slotCount;

    // Keep the object of each array slot from the last search, because a search only has to read the slots that changed.
    private Address[] _slotObjects = [];

    private bool _slotsOutdated;

    // Count each change of the kept classes, so a caller can keep what it found on them until they change.
    private int _version;
    private readonly StructReader _structReader = structReader;

    /// <summary>
    ///     Finds the loaded class called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Ignores case like the engine. A class with the exact name wins. Otherwise returns the class in the lowest slot whose name matches
    ///     in another case. Searches again on each call with such a name, because the index only keeps exact names.
    ///     Returns a class a search found before while its name still matches and its class still derives from <c>Class</c>.
    ///     Otherwise reads the array and, for each slot whose object changed since the last search, the class pointer of the object
    ///     and the name of each object whose class is <c>Class</c> or derives from it, as <c>BlueprintGeneratedClass</c> does.
    ///     Keeps the 1st class it finds for each name, in array order within 1 search,
    ///     and reads the name of each kept Blueprint class again on every search.
    ///     Reads every slot again when the name or the class of a kept class doesn't match or a kept class that shared its name is gone.
    ///     Skips an object that doesn't read, reads it again on the next search and fails with that read only when no class matches.
    ///     Doesn't keep a miss, because the class can load later.
    ///     Doesn't find a new class that takes both the slot and the address of a freed object that wasn't a Blueprint class
    ///     until a search reads every slot again.
    /// </remarks>
    /// <param name="name">The name of the class, like <c>Actor</c> or <c>BP_Player_C</c>.</param>
    /// <returns>
    ///     The address and the name of the class or a failure when no loaded class has the name or a read of the search fails.
    /// </returns>
    public Result<(Address Address, string Name)> FindClass(string name)
    {
        // Skip the kept class while every slot has to be read again, because the name can give another class after that.
        if (!_slotsOutdated && _classesByName.TryGetValue(name, out var kept))
        {
            if (_objectReader.NameOf(kept.Address).TryUnwrap(out string? keptName, out _)
                && string.Equals(keptName, name, StringComparison.Ordinal)
                && _objectReader.ClassOf(kept.Address).TryUnwrap(out var keptClass, out _)
                && DerivesFromMetaclass(keptClass).TryUnwrap(out bool keptIsClass, out _)
                && keptIsClass)
            {
                return (kept.Address, name);
            }

            // Read every slot again, because the kept class doesn't match and its slot can still have its address.
            _slotsOutdated = true;
        }

        var error = Search(out var skipped);

        // Return a class from the slots before a failed read of the array, so the result doesn't depend on an earlier call.
        if (_classesByName.TryGetValue(name, out var found) || FindClassIgnoringCase(name, out found))
        {
            return (found.Address, _classNamesBySlot[found.Slot]);
        }

        if (error is not null)
        {
            return ClassError.SearchFailed(name, error);
        }

        return skipped is null
            ? ClassError.NotFound(name)
            : ClassError.SearchFailed(name, skipped);
    }

    /// <summary>
    ///     Finds the loaded package called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Ignores case. Checks a kept package and searches like <see cref="FindClass"/>.
    ///     On a miss reads the name of every kept package again, because a package can take both the slot and the address of a freed package.
    /// </remarks>
    /// <param name="name">The name of the package, like <c>/Script/Engine</c> or <c>Engine</c>.</param>
    /// <returns>
    ///     The address and the name of the package or a failure when no loaded package has the name or a read of the search fails.
    /// </returns>
    public Result<(Address Address, string Name)> FindPackage(string name)
    {
        if (!_slotsOutdated && _packagesByName.TryGetValue(name, out var kept))
        {
            if (_objectReader.NameOf(kept.Address).TryUnwrap(out string? keptName, out _)
                && string.Equals(keptName, name, StringComparison.OrdinalIgnoreCase)
                && _objectReader.ClassOf(kept.Address).TryUnwrap(out var keptClass, out _)
                && keptClass == _packageClass)
            {
                return (kept.Address, keptName);
            }

            _slotsOutdated = true;
        }

        var error = Search(out var skipped);

        if (!_packagesByName.ContainsKey(name) && CheckPackages())
        {
            error = Search(out skipped);
        }

        if (_packagesByName.TryGetValue(name, out var found) || KeepFirstPackage(name, out found))
        {
            return (found.Address, _packageNamesBySlot[found.Slot]);
        }

        if (error is not null)
        {
            return PackageError.SearchFailed(name, error);
        }

        return skipped is null
            ? PackageError.NotLoaded(name)
            : PackageError.SearchFailed(name, skipped);
    }

    /// <summary>
    ///     Lists the loaded packages in array order.
    /// </summary>
    /// <remarks>
    ///     Lists the objects of the class <c>Package</c> without an outer object, so the groups inside an Unreal Engine 3 package don't show up.
    ///     Reads the name of every kept package again, like a miss of <see cref="FindPackage"/>.
    /// </remarks>
    /// <returns>
    ///     The address and the name of each package or a failure when the array doesn't read
    ///     or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<(Address Address, string Name)>> Packages()
    {
        var error = Search(out var skipped);

        if (error is null && CheckPackages())
        {
            error = Search(out skipped);
        }

        if (error is not null)
        {
            return error;
        }

        List<(Address, string)> packages =
            [.. _packageNamesBySlot.Keys.OrderBy(static slot => slot).Select(slot => (_slotObjects[slot], _packageNamesBySlot[slot]))];

        return ToResult(packages, skipped);
    }

    /// <summary>
    ///     Lists the loaded classes whose outer object is <paramref name="package"/>, in array order.
    /// </summary>
    /// <param name="package">The address of the package.</param>
    /// <returns>
    ///     The address and the name of each class or a failure when the array doesn't read
    ///     or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<(Address Address, string Name)>> ClassesOf(Address package)
    {
        if (Search(out var skipped) is { } error)
        {
            return error;
        }

        List<(Address, string)> classes = [];

        foreach (int slot in _classNamesBySlot.Keys.OrderBy(static slot => slot))
        {
            if (!LinksOf(slot).TryUnwrap(out var links, out error))
            {
                skipped ??= error;
                continue;
            }

            if (links.Outer == package)
            {
                classes.Add((_slotObjects[slot], _classNamesBySlot[slot]));
            }
        }

        return ToResult(classes, skipped);
    }

    /// <summary>
    ///     Searches like <see cref="FindClass"/> and reads a number that changes each time the loaded classes change.
    /// </summary>
    /// <returns>
    ///     The number or a failure when the array doesn't read.
    /// </returns>
    public Result<int> ReadVersion()
    {
        if (Search(out _) is { } error)
        {
            return error;
        }

        return _version;
    }

    /// <summary>
    ///     Lists the loaded classes that derive from <paramref name="klass"/> directly or over other classes, in array order.
    /// </summary>
    /// <param name="klass">The address of the class, which the list leaves out.</param>
    /// <returns>
    ///     The address and the name of each class or a failure when the array doesn't read
    ///     or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<(Address Address, string Name)>> DerivedFrom(Address klass)
    {
        if (Search(out var skipped) is { } error)
        {
            return error;
        }

        int[] slots = [.. _classNamesBySlot.Keys.OrderBy(static slot => slot)];
        Dictionary<Address, Address> supers = [with(slots.Length)];

        foreach (int slot in slots)
        {
            if (LinksOf(slot).TryUnwrap(out var links, out error))
            {
                supers[_slotObjects[slot]] = links.Super;
            }
            else
            {
                skipped ??= error;
            }
        }

        List<(Address, string)> derived = [];

        foreach (int slot in slots)
        {
            if (supers.TryGetValue(_slotObjects[slot], out var current) && Reaches(supers, current, klass))
            {
                derived.Add((_slotObjects[slot], _classNamesBySlot[slot]));
            }
        }

        return ToResult(derived, skipped);
    }

    /// <summary>
    ///     Lists the objects of the array whose class is exactly <paramref name="klass"/>, in array order.
    /// </summary>
    /// <remarks>
    ///     Searches like <see cref="FindClass"/>, then reads the class pointer and the name of each object whose kept class matches.
    ///     Leaves out objects of derived classes and the class default object, whose name starts with <c>Default__</c>.
    ///     Leaves out an object whose class pointer doesn't match its kept class and reads its slot again on the next search.
    ///     Doesn't find a new object that takes both the slot and the address of a freed object of another class
    ///     until a search reads every slot again.
    /// </remarks>
    /// <param name="klass">The address of the class.</param>
    /// <returns>
    ///     The address, the name and the slot of each object or a failure when the array doesn't read
    ///     or an object doesn't read and the list is empty.
    /// </returns>
    public Result<IReadOnlyList<(Address Address, string Name, int Slot)>> InstancesOf(Address klass)
    {
        if (Search(out var skipped) is { } error)
        {
            return error;
        }

        List<(Address, string, int)> instances = [];

        for (int slot = 0; slot < _slotCount; slot++)
        {
            var obj = _slotObjects[slot];

            if (obj == Address.Zero || _slotClasses[slot] != klass)
            {
                continue;
            }

            if (!_objectReader.ClassOf(obj).TryUnwrap(out var objectClass, out error)
                || !_objectReader.NameOf(obj).TryUnwrap(out string? name, out error))
            {
                skipped ??= error;
                continue;
            }

            // Clear the slot of an object whose class changed, so the next search reads the object at the address again.
            if (objectClass != klass)
            {
                RemoveNames(slot);
                _slotObjects[slot] = Address.Zero;
                continue;
            }

            if (!name.StartsWith(DefaultObjectPrefix, StringComparison.Ordinal))
            {
                instances.Add((obj, name, slot));
            }
        }

        return ToResult(instances, skipped);
    }

    /// <summary>
    ///     Checks whether the slot at <paramref name="slot"/> of the array still has the object at <paramref name="obj"/>.
    /// </summary>
    /// <remarks>
    ///     Reads the slot and not the object, because the game clears the slot when it frees the object
    ///     but the memory of the object keeps its class pointer until the game uses it again.
    /// </remarks>
    /// <param name="slot">The slot of the object, 0 or above.</param>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     <see langword="true"/> when the slot has the object or a failure when the array doesn't read.
    /// </returns>
    public Result<bool> IsInSlot(int slot, Address obj)
    {
        return _objectArray.ReadItem(slot).TryUnwrap(out var item, out var error)
            ? item.Address == obj
            : error;
    }

    // Return the 1st skipped read in place of an empty list, so a broken profile doesn't look like a class without objects.
    private static Result<IReadOnlyList<T>> ToResult<T>(List<T> items, ResultError? skipped)
    {
        return items.Count == 0 && skipped is not null ? skipped : Result.Ok<IReadOnlyList<T>>(items);
    }

    // Follow the kept super classes from a class up to the target, which stops at a class outside the array or after 64 levels.
    private static bool Reaches(Dictionary<Address, Address> supers, Address current, Address target)
    {
        for (int depth = 0; depth < ClassReader.MaxSuperDepth && current != Address.Zero; depth++)
        {
            if (current == target)
            {
                return true;
            }

            if (!supers.TryGetValue(current, out current))
            {
                return false;
            }
        }

        return false;
    }

    // Read the slots of the array that changed since the last search and keep the classes and the packages in them by name.
    // Return the error when the array doesn't read.
    // Don't keep the object of a slot when the object doesn't read, so the next search reads it again.
    private ResultError? Search(out ResultError? skipped)
    {
        skipped = null;

        if (_slotsOutdated)
        {
            _blueprintSlots.Clear();
            _classesByName.Clear();
            _classLinks.Clear();
            _classNamesBySlot.Clear();
            _version++;
            _derivesFromMetaclass.Clear();
            _packagesByName.Clear();
            _packageNamesBySlot.Clear();
            _sharedNames.Clear();
            Array.Clear(_slotObjects, 0, _slotCount);
            _slotCount = 0;
            _slotsOutdated = false;
        }

        if (!_objectArray.ReadCount().TryUnwrap(out int count, out var error))
        {
            return error;
        }

        // Clear the slots past the count, because the array can grow into them again with other objects.
        if (count < _slotCount)
        {
            for (int slot = count; slot < _slotCount; slot++)
            {
                RemoveNames(slot);
            }

            Array.Clear(_slotObjects, count, _slotCount - count);
            _slotCount = count;
        }

        CheckBlueprintClasses();

        error = ReadSlots(count, ref skipped);

        // Read the packages in front of the class Package also after a failed read of the array, because their slots don't change again.
        if (_packagesPending)
        {
            AddPendingPackages(ref skipped);
        }

        return error;
    }

    // Return the 1st failure only when no slot reads, because the other slots of an array with 1 unreadable block still have their objects.
    private ResultError? ReadSlots(int count, ref ResultError? skipped)
    {
        int slot = -1;
        bool anyRead = false;
        ResultError? failure = null;

        foreach (var item in _objectArray.Read(count))
        {
            slot++;

            if (!item.TryUnwrap(out var obj, out var error))
            {
                failure ??= error;

                if (slot < _slotCount)
                {
                    RemoveNames(slot);
                    _slotObjects[slot] = Address.Zero;
                }

                continue;
            }

            anyRead = true;

            // Grow with the slots that read, because a wrong count can be far above the real one.
            if (slot >= _slotObjects.Length)
            {
                int length = Math.Max(1024, _slotObjects.Length * 2);
                Array.Resize(ref _slotObjects, length);
                Array.Resize(ref _slotClasses, length);
            }

            if (slot >= _slotCount)
            {
                _slotCount = slot + 1;
            }

            if (obj == _slotObjects[slot])
            {
                continue;
            }

            RemoveNames(slot);

            // Don't keep the object of the slot until it reads, so the next search reads it again after a failed read.
            _slotObjects[slot] = Address.Zero;

            if (obj == Address.Zero)
            {
                continue;
            }

            if (!_objectReader.ClassOf(obj).TryUnwrap(out var klass, out error))
            {
                skipped ??= error;
                continue;
            }

            // Don't keep an object whose class pointer is 0, because the engine can still be creating the object.
            if (klass == Address.Zero)
            {
                continue;
            }

            if (_metaclass == Address.Zero)
            {
                if (!FindMetaclass(obj, klass).TryUnwrap(out var metaclass, out error))
                {
                    skipped ??= error;
                    continue;
                }

                _metaclass = metaclass;
            }

            if (!DerivesFromMetaclass(klass).TryUnwrap(out bool isClass, out error))
            {
                skipped ??= error;
                continue;
            }

            _slotClasses[slot] = klass;

            if (!isClass)
            {
                if (klass == _packageClass && !AddPackage(slot, obj, ref skipped))
                {
                    continue;
                }

                _slotObjects[slot] = obj;
                continue;
            }

            if (!_objectReader.NameOf(obj).TryUnwrap(out string? name, out error))
            {
                skipped ??= error;
                continue;
            }

            _slotObjects[slot] = obj;

            _classNamesBySlot[slot] = name;
            _version++;

            if (klass != _metaclass)
            {
                _blueprintSlots.Add(slot);
            }
            else if (_packageClass == Address.Zero && string.Equals(name, PackageClassName, StringComparison.Ordinal))
            {
                _packageClass = obj;
                _packagesPending = true;
            }

            // Keep the 1st class of a name, so the name gives the same class while that class is loaded.
            if (_classesByName.ContainsKey(name))
            {
                _sharedNames.Add(name);
            }
            else
            {
                _classesByName[name] = (obj, slot);
            }
        }

        if (failure is not null && !anyRead)
        {
            return failure;
        }

        skipped ??= failure;

        return null;
    }

    // Keep the objects of the class Package in kept slots that a search read before it found the class.
    private void AddPendingPackages(ref ResultError? skipped)
    {
        _packagesPending = false;

        for (int slot = 0; slot < _slotCount; slot++)
        {
            if (_slotObjects[slot] != Address.Zero
                && _slotClasses[slot] == _packageClass
                && !_packageNamesBySlot.ContainsKey(slot)
                && !AddPackage(slot, _slotObjects[slot], ref skipped))
            {
                // Clear the slot, so the next search reads the package again.
                _slotObjects[slot] = Address.Zero;
            }
        }
    }

    // Keep a package without an outer object by name. Return false when a read fails.
    private bool AddPackage(int slot, Address obj, ref ResultError? skipped)
    {
        if (!_objectReader.OuterOf(obj).TryUnwrap(out var outer, out var error))
        {
            skipped ??= error;
            return false;
        }

        if (outer != Address.Zero)
        {
            return true;
        }

        if (!_objectReader.NameOf(obj).TryUnwrap(out string? name, out error))
        {
            skipped ??= error;
            return false;
        }

        _packageNamesBySlot[slot] = name;

        if (!_packagesByName.ContainsKey(name))
        {
            _packagesByName[name] = (obj, slot);
        }

        return true;
    }

    // Read the name of each kept package again and clear its slot when the name changed,
    // because another package can take both the slot and the address of a freed package. Return true when a slot changed.
    private bool CheckPackages()
    {
        List<int>? gone = null;

        foreach (var package in _packageNamesBySlot)
        {
            if (!_objectReader.NameOf(_slotObjects[package.Key]).TryUnwrap(out string? name, out _)
                || !string.Equals(name, package.Value, StringComparison.Ordinal))
            {
                (gone ??= []).Add(package.Key);
            }
        }

        if (gone is null)
        {
            return false;
        }

        foreach (int slot in gone)
        {
            RemoveNames(slot);
            _slotObjects[slot] = Address.Zero;
        }

        return true;
    }

    // Keep the 1st kept package with the name, because the package that a search kept for the name can be gone while another stays loaded.
    private bool KeepFirstPackage(string name, out (Address Address, int Slot) found)
    {
        found = default;
        int first = int.MaxValue;

        foreach (var package in _packageNamesBySlot)
        {
            if (package.Key < first && string.Equals(package.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                first = package.Key;
            }
        }

        if (first == int.MaxValue)
        {
            return false;
        }

        found = (_slotObjects[first], first);
        _packagesByName[name] = found;

        return true;
    }

    // Find the kept class in the lowest slot whose name matches in another case. Don't keep it, because _classesByName only keeps exact names.
    private bool FindClassIgnoringCase(string name, out (Address Address, int Slot) found)
    {
        found = default;
        int first = int.MaxValue;

        foreach (var klass in _classNamesBySlot)
        {
            if (klass.Key < first && string.Equals(klass.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                first = klass.Key;
            }
        }

        if (first == int.MaxValue)
        {
            return false;
        }

        found = (_slotObjects[first], first);

        return true;
    }

    // Read the outer object and the super class of the class in a slot once.
    private Result<(Address Outer, Address Super)> LinksOf(int slot)
    {
        if (_classLinks.TryGetValue(slot, out var kept))
        {
            return kept;
        }

        if (!_objectReader.OuterOf(_slotObjects[slot]).TryUnwrap(out var outer, out var error)
            || !_structReader.SuperOf(_slotObjects[slot]).TryUnwrap(out var super, out error))
        {
            return error;
        }

        return _classLinks[slot] = (outer, super);
    }

    // Read the name of each kept Blueprint class again and clear its slot when the name changed,
    // because another class can take both the slot and the address of a freed Blueprint class.
    private void CheckBlueprintClasses()
    {
        List<int>? gone = null;

        foreach (int slot in _blueprintSlots)
        {
            if (!_objectReader.NameOf(_slotObjects[slot]).TryUnwrap(out string? name, out _)
                || !string.Equals(name, _classNamesBySlot[slot], StringComparison.Ordinal))
            {
                (gone ??= []).Add(slot);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (int slot in gone)
        {
            RemoveNames(slot);
            _slotObjects[slot] = Address.Zero;
        }
    }

    // Remove the class or the package of a slot from the kept names.
    private void RemoveNames(int slot)
    {
        _classLinks.Remove(slot);

        if (_classNamesBySlot.TryGetValue(slot, out string? className))
        {
            _blueprintSlots.Remove(slot);
            _classNamesBySlot.Remove(slot);
            _version++;

            if (_classesByName.TryGetValue(className, out var kept) && kept.Slot == slot)
            {
                _classesByName.Remove(className);

                // Read every slot again on the next search, because the other class with the name isn't kept.
                if (_sharedNames.Contains(className))
                {
                    _slotsOutdated = true;
                }
            }
        }

        if (_packageNamesBySlot.TryGetValue(slot, out string? packageName))
        {
            _packageNamesBySlot.Remove(slot);

            if (_packagesByName.TryGetValue(packageName, out var kept) && kept.Slot == slot)
            {
                _packagesByName.Remove(packageName);
            }
        }
    }

    // Follow the super pointers of the class of an object up to Class, which only the class of a class reaches.
    private Result<bool> DerivesFromMetaclass(Address klass)
    {
        if (_derivesFromMetaclass.TryGetValue(klass, out bool kept))
        {
            return kept;
        }

        var current = klass;

        for (int depth = 0; depth < ClassReader.MaxSuperDepth && current != Address.Zero; depth++)
        {
            if (current == _metaclass)
            {
                _derivesFromMetaclass[klass] = true;

                return true;
            }

            if (!_structReader.SuperOf(current).TryUnwrap(out current, out var error))
            {
                return error;
            }
        }

        if (current != Address.Zero)
        {
            return StructError.ChainPastLimit(klass, ClassReader.MaxSuperDepth, _structReader.SuperOffset);
        }

        _derivesFromMetaclass[klass] = false;

        return false;
    }

    // Follow the class pointers up to the class that is its own class, which is Class when the class offset is right.
    private Result<Address> FindMetaclass(Address obj, Address klass)
    {
        var current = klass;

        for (int depth = 0; depth < MaxMetaclassDepth && current != Address.Zero; depth++)
        {
            if (!_objectReader.ClassOf(current).TryUnwrap(out var next, out var error))
            {
                return error;
            }

            if (next == current)
            {
                if (!_objectReader.NameOf(current).TryUnwrap(out string? name, out error))
                {
                    return error;
                }

                return string.Equals(name, MetaclassName, StringComparison.Ordinal)
                    ? current
                    : ClassError.MetaclassNotFound(obj, _objectReader.ClassOffset);
            }

            current = next;
        }

        return ClassError.MetaclassNotFound(obj, _objectReader.ClassOffset);
    }
}
