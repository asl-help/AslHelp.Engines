using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a <c>TSet</c>, which reads as a <see cref="HashSet{T}"/> of the type of its element property.
/// </summary>
/// <remarks>
///     A key after the set reads whether the set has it, like <c>IntSet[22]</c>.
///     A set of objects takes the position among the used slots instead and steps into the object, like <c>ObjectSet[1].Hp</c>.
/// </remarks>
internal sealed class SetKind : LayoutKind<SetPropertyLayout>
{
    private const string ClassName = "SetProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitCollection(property);
    }

    /// <summary>
    ///     Reads the offsets of the element property, the set layout and the set.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the missing offsets.
    /// </returns>
    public override Result<SetPropertyLayout> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);
        int elementProp = offsets.ReadProperty(ClassName, "ElementProp");
        int setLayout = offsets.ReadProperty(ClassName, "SetLayout");

        return offsets.Finish(new SetPropertyLayout(elementProp, setLayout, SetLayout.Read(offsets)));
    }

    /// <summary>
    ///     Reads the element property and the slot size of <paramref name="property"/> and creates the access of the set.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="layout">The offsets of the set.</param>
    /// <returns>
    ///     The access or a failure when a member doesn't read or the slot size isn't above 0.
    ///     Elements that don't read as values give an access that fails every read.
    /// </returns>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, SetPropertyLayout layout)
    {
        if (!InnerProperty.Read(classReader, property, "ElementProp", layout.ElementProp).TryUnwrap(out var elementProperty, out var error)
            || !elementProperty.Bind().TryUnwrap(out var element, out error)
            || !layout.Set.ReadSlotSize(classReader, property, layout.SetLayout).TryUnwrap(out int slotSize, out error))
        {
            return error;
        }

        SparseArray sparseArray = new(property.Name, layout.Set.SparseArray, property.Offset + layout.Set.Elements, classReader.Bounds.MaxElements);

        return element.Accept(new SetAccessFactory(property, sparseArray, slotSize, elementProperty.ClassName));
    }
}

file sealed class SetAccessFactory(
    UnrealProperty property,
    SparseArray sparseArray,
    int slotSize,
    string elementClass) : AccessVisitor<Result<PropertyAccess>>
{
    public override Result<PropertyAccess> VisitOther(PropertyAccess access)
    {
        return new FailedAccess(property, CollectionError.KeyNotValue(property.Name, elementClass));
    }

    public override Result<PropertyAccess> VisitValue<T>(ValueAccess<T> access)
    {
        SparseLookup<T> lookup = new(sparseArray, access, slotSize);

        return new SetValueAccess<T>(property, sparseArray, lookup, access);
    }
}

// Find an object of a set of objects by its position, because a path key can't be an address.
file sealed class SetElements<T>(
    UnrealProperty property,
    SparseArray sparseArray,
    SparseLookup<T> lookup,
    ValueAccess<T> element) : CollectionAccess(property)
{
    public override ValueAccess<int> Count { get; } = sparseArray.CreateCount(property);

    public override PropertyAccess Element { get; } = typeof(T) == typeof(Address) ? element : new ContainsAccess(property);

    public override Result CheckKey(ElementKey key)
    {
        return typeof(T) == typeof(Address) ? key.Check<int>(sparseArray, Property.Name) : key.Check<T>(this, Property.Name);
    }

    public override Result<Address> FindElement(IMemoryReader memory, Address container, ElementKey key)
    {
        if (typeof(T) == typeof(Address))
        {
            return sparseArray.FindUsed(memory, container, lookup.SlotSize, key);
        }

        if (key.As<T>(this, Property.Name).TryUnwrap(out var value, out var error))
        {
            return lookup.Find(memory, container, value, key);
        }

        // Return 0 for a number that doesn't fit the element type, because the set can't have it.
        return error is CollectionError { IsKeyTypeMismatch: true } ? error : Address.Zero;
    }
}

// Read whether the set has the key, which FindElement gives as an address of 0 when it doesn't.
file sealed class ContainsAccess(
    UnrealProperty property) : ValueAccess<bool>(property)
{
    public override Result<bool> Read(IMemoryReader memory, Address container, bool last)
    {
        return container != Address.Zero;
    }
}

file sealed class SetValueAccess<T>(
    UnrealProperty property,
    SparseArray sparseArray,
    SparseLookup<T> lookup,
    ValueAccess<T> element) : ValueAccess<HashSet<T>>(property)
{
    private readonly SetElements<T> _elements = new(property, sparseArray, lookup, element);

    // Keep the elements of this read in a set, because a torn read can show 1 element in 2 slots.
    // Keep the value of each slot, so a string element keeps its text while it doesn't change.
    // The access is shared by every object of the class, so an element of another object only costs a new text.
    private readonly HashSet<T> _items = [];
    private T[] _slots = [];

    public override Result<HashSet<T>> Read(IMemoryReader memory, Address container, HashSet<T>? last)
    {
        if (!sparseArray.ReadSlots(memory, container).TryUnwrap(out var slots, out var error))
        {
            return error;
        }

        if (_slots.Length < slots.SlotCount)
        {
            _slots = new T[Math.Max(slots.SlotCount, (int)Math.Min(_slots.Length * 2L, int.MaxValue))];
        }

        _items.Clear();

        for (int slot = 0; slot < slots.SlotCount; slot++)
        {
            if (!sparseArray.IsUsed(slot))
            {
                continue;
            }

            if (!element.Read(memory, slots.Data + ((long)slot * lookup.SlotSize), _slots[slot]).TryUnwrap(out var item, out error))
            {
                return error;
            }

            _slots[slot] = item;
            _items.Add(item);
        }

        if (last is not null && SameItems(last))
        {
            return last;
        }

        HashSet<T> items = [.. _items];

        return items;
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container, null, _elements);
    }

    private bool SameItems(HashSet<T> last)
    {
        if (last.Count != _items.Count)
        {
            return false;
        }

        foreach (var item in _items)
        {
            if (!last.Contains(item))
            {
                return false;
            }
        }

        return true;
    }
}
