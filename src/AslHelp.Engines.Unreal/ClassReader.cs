using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides the classes of a running game and the properties of a class and its super classes.
/// </summary>
/// <remarks>
///     Keeps every class it finds by address, so each class gets 1 <see cref="UnrealClass"/> for the game.
///     Keeps the name of every class of the object array too, so a search only reads the objects in array slots that changed.
///     Checks the name of a kept class on each hit, because the game can free a Blueprint class and reuse its address.
///     Isn't thread-safe.
/// </remarks>
/// <param name="objectReader">The reader of the names and the classes of objects.</param>
/// <param name="structReader">The reader of the super chains of classes.</param>
/// <param name="propertyReader">The reader of the properties in the storage shape of the game.</param>
/// <param name="objectArray">The object array to search for classes.</param>
internal sealed class ClassReader(
    ObjectReader objectReader,
    StructReader structReader,
    IPropertyReader propertyReader,
    IObjectArray objectArray)
{
    // Stop after 4 classes, because the class of a Blueprint class is 2 steps from Class and a wrong offset can loop.
    private const int MaxMetaclassDepth = 4;

    private const int MaxSuperDepth = 64;

    // The engine calls the class of every class Class. Class is its own class.
    private const string MetaclassName = "Class";

    // Keep the slots of the Blueprint classes, because the engine frees Blueprint classes
    // and a search has to check that such a slot still has its class.
    private readonly HashSet<int> _blueprintSlots = [];

    private readonly Dictionary<Address, UnrealClass> _classes = [];

    // Keep the address and the slot of the 1st class that a search found for each name.
    private readonly Dictionary<string, (Address Address, int Slot)> _classesByName = [];

    // Keep the name of the class in each slot, so a slot that changes takes its name out of the kept names.
    private readonly Dictionary<int, string> _classSlots = [];

    // Keep whether the class of an object derives from Class, because only classes of C++ code derive from Class
    // and the engine doesn't free them.
    private readonly Dictionary<Address, bool> _derivesFromMetaclass = [];

    private Address _metaclass;
    private readonly IObjectArray _objectArray = objectArray;
    private readonly ObjectReader _objectReader = objectReader;
    private readonly IPropertyReader _propertyReader = propertyReader;

    // Keep the names that 2 or more classes have, because the search has to read every slot again when the 1st of them is gone.
    private readonly HashSet<string> _sharedNames = [];

    private int _slotCount;

    // Keep the object of each array slot from the last search, because a search only has to read the slots that changed.
    private Address[] _slots = [];

    private bool _slotsOutdated;
    private readonly StructReader _structReader = structReader;

    /// <summary>
    ///     Reads the class of <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The class or a failure when <paramref name="obj"/> is <see cref="Address.Zero"/>, its class pointer is 0 or a read fails.
    /// </returns>
    public Result<UnrealClass> ClassOf(Address obj)
    {
        if (!_objectReader.ClassOf(obj).TryUnwrap(out var klass, out var error))
        {
            return error;
        }

        if (klass == Address.Zero)
        {
            return ClassError.Null(obj, _objectReader.ClassOffset);
        }

        return _objectReader.NameOf(klass).TryUnwrap(out string? name, out error)
            ? Keep(klass, name)
            : error;
    }

    /// <summary>
    ///     Finds the loaded class called <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    ///     Compares ordinally. Returns a class a search found before while its name still matches and its class still derives from <c>Class</c>.
    ///     Otherwise reads the array and, for each slot whose object changed since the last search, the class pointer of the object
    ///     and the name of each object whose class is <c>Class</c> or derives from it, as <c>BlueprintGeneratedClass</c> does.
    ///     Keeps the 1st class it finds for each name, in array order within 1 search,
    ///     and reads the name of each kept Blueprint class again on every search.
    ///     Reads every slot again when a kept class doesn't check out or a kept class that shared its name is gone.
    ///     Skips an object that doesn't read, reads it again on the next search and fails with that read only when no class matches.
    ///     Doesn't keep a miss, because the class can load later.
    ///     Doesn't find a new class that takes both the slot and the address of a freed object that wasn't a Blueprint class
    ///     until a search reads every slot again.
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

        // Skip the kept class while every slot has to be read again, because the name can give another class after that.
        if (!_slotsOutdated && _classesByName.TryGetValue(name, out var kept))
        {
            if (_objectReader.NameOf(kept.Address).TryUnwrap(out string? keptName, out _)
                && string.Equals(keptName, name, StringComparison.Ordinal)
                && _objectReader.ClassOf(kept.Address).TryUnwrap(out var keptClass, out _)
                && DerivesFromMetaclass(keptClass).TryUnwrap(out bool keptIsClass, out _)
                && keptIsClass)
            {
                return Keep(kept.Address, name);
            }

            // Read every slot again, because the kept class didn't check out and its slot can still have its address.
            _slotsOutdated = true;
        }

        var error = Search(out var skipped);

        // Return a class from the slots before a failed read of the array, so the result doesn't depend on an earlier call.
        if (_classesByName.TryGetValue(name, out var found))
        {
            return Keep(found.Address, name);
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
                if (!item.TryUnwrap(out var prop, out error) || !Read(prop).TryUnwrap(out var property, out error))
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

    // Read the slots of the array that changed since the last search and keep the classes in them by name.
    // Return the error when the array doesn't read. Leave the slot of an object that doesn't read open, so the next search reads it again.
    private ResultError? Search(out ResultError? skipped)
    {
        skipped = null;

        if (_slotsOutdated)
        {
            _blueprintSlots.Clear();
            _classesByName.Clear();
            _classSlots.Clear();
            _sharedNames.Clear();
            Array.Clear(_slots, 0, _slotCount);
            _slotCount = 0;
            _slotsOutdated = false;
        }

        if (!_objectArray.ReadCount().TryUnwrap(out int count, out var error))
        {
            return error;
        }

        // Clear the slots past the count, because the array can grow into them again with other objects.
        if (count >= 0 && count < _slotCount)
        {
            Array.Clear(_slots, count, _slotCount - count);
            _slotCount = count;
        }

        CheckBlueprintClasses();

        int slot = -1;

        foreach (var item in _objectArray.Read(count))
        {
            slot++;

            if (!item.TryUnwrap(out var obj, out error))
            {
                return error;
            }

            // Grow with the slots that read, because a wrong count can be far above the real one.
            if (slot >= _slots.Length)
            {
                Array.Resize(ref _slots, Math.Max(1024, _slots.Length * 2));
            }

            if (slot >= _slotCount)
            {
                _slotCount = slot + 1;
            }

            if (obj == _slots[slot])
            {
                continue;
            }

            if (_classSlots.TryGetValue(slot, out string? oldName))
            {
                Drop(slot, oldName);
            }

            // Leave the slot open until the object reads, so the next search reads the object again after a failed read.
            _slots[slot] = Address.Zero;

            if (obj == Address.Zero)
            {
                continue;
            }

            if (!_objectReader.ClassOf(obj).TryUnwrap(out var klass, out error))
            {
                skipped ??= error;
                continue;
            }

            // Leave the slot of an object whose class pointer is 0 open, because the engine can still be creating the object.
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

            if (!isClass)
            {
                _slots[slot] = obj;
                continue;
            }

            if (!_objectReader.NameOf(obj).TryUnwrap(out string? name, out error))
            {
                skipped ??= error;
                continue;
            }

            _slots[slot] = obj;

            _classSlots[slot] = name;

            if (klass != _metaclass)
            {
                _blueprintSlots.Add(slot);
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

        return null;
    }

    // Read the name of each kept Blueprint class again and open its slot when the name changed,
    // because another class can take both the slot and the address of a freed Blueprint class.
    private void CheckBlueprintClasses()
    {
        List<int>? gone = null;

        foreach (int slot in _blueprintSlots)
        {
            if (slot >= _slotCount
                || !_objectReader.NameOf(_slots[slot]).TryUnwrap(out string? name, out _)
                || !string.Equals(name, _classSlots[slot], StringComparison.Ordinal))
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
            Drop(slot, _classSlots[slot]);

            if (slot < _slotCount)
            {
                _slots[slot] = Address.Zero;
            }
        }
    }

    // Remove the class of a slot from the kept names.
    private void Drop(int slot, string name)
    {
        _blueprintSlots.Remove(slot);
        _classSlots.Remove(slot);

        if (_classesByName.TryGetValue(name, out var kept) && kept.Slot == slot)
        {
            _classesByName.Remove(name);

            // Read every slot again on the next search, because the other class with the name isn't kept.
            if (_sharedNames.Contains(name))
            {
                _slotsOutdated = true;
            }
        }
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

    // Follow the super pointers of the class of an object up to Class, which only the class of a class reaches.
    private Result<bool> DerivesFromMetaclass(Address klass)
    {
        if (_derivesFromMetaclass.TryGetValue(klass, out bool kept))
        {
            return kept;
        }

        var current = klass;

        for (int depth = 0; depth < MaxSuperDepth && current != Address.Zero; depth++)
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
            return StructError.ChainPastLimit(klass, MaxSuperDepth, _structReader.SuperOffset);
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

    private Result<UnrealProperty> Read(Address prop)
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
            ? new UnrealProperty(name, className, offset, elementSize)
            : error;
    }
}
