using AslHelp.Engines.Unreal.Errors;
using AslHelp.Engines.Unreal.Structs;
using AslHelp.Memory;
using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Represents the kind of a <c>TMap</c>, which reads as a <see cref="Dictionary{TKey, TValue}"/> when its keys and values read as values.
/// </summary>
/// <remarks>
///     A key after the map reads its value or steps into it, like <c>Dict["two"]</c> or <c>CoordDict[5].X</c>.
///     The key and the value of a pair sit at the offsets of the key and value properties. Only the used slots of the sparse array count.
/// </remarks>
internal sealed class MapKind : LayoutKind<MapPropertyLayout>
{
    private const string ClassName = "MapProperty";

    /// <inheritdoc/>
    public override IReadOnlyList<string> ClassNames { get; } = [ClassName];

    /// <inheritdoc/>
    public override TResult Accept<TResult>(PropertyVisitor<TResult> visitor, UnrealProperty property)
    {
        return visitor.VisitCollection(property);
    }

    /// <summary>
    ///     Reads the offsets of the key and value properties, the map layout and the set of pairs.
    /// </summary>
    /// <param name="profile">The offsets of the document.</param>
    /// <returns>
    ///     The layout or a failure with the missing offsets.
    /// </returns>
    public override Result<MapPropertyLayout> Read(KindProfile profile)
    {
        var offsets = profile.For(ClassName);
        int keyProp = offsets.ReadProperty(ClassName, "KeyProp");
        int valueProp = offsets.ReadProperty(ClassName, "ValueProp");
        int mapLayout = offsets.ReadProperty(ClassName, "MapLayout");
        int setLayout = offsets.Read("FScriptMapLayout", "SetLayout");
        int pairs = offsets.Read("FScriptMap", "Pairs");

        return offsets.Finish(new MapPropertyLayout(keyProp, valueProp, mapLayout, setLayout, pairs, SetLayout.Read(offsets)));
    }

    /// <summary>
    ///     Reads the key and value properties and the slot size of <paramref name="property"/> and creates the access of the map.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="classReader">The reader of the game.</param>
    /// <param name="layout">The offsets of the map.</param>
    /// <returns>
    ///     The access or a failure when a member doesn't read or the slot size isn't above 0.
    ///     Keys that don't read as values give an access that fails every read.
    /// </returns>
    protected override Result<PropertyAccess> Bind(UnrealProperty property, ClassReader classReader, MapPropertyLayout layout)
    {
        if (!InnerProperty.Read(classReader, property, "KeyProp", layout.KeyProp).TryUnwrap(out var keyProperty, out var error)
            || !keyProperty.Bind().TryUnwrap(out var key, out error)
            || !InnerProperty.Read(classReader, property, "ValueProp", layout.ValueProp).TryUnwrap(out var valueProperty, out error)
            || !valueProperty.Bind().TryUnwrap(out var value, out error)
            || !layout.Set.ReadSlotSize(classReader, property, layout.MapLayout + layout.SetLayout).TryUnwrap(out int slotSize, out error))
        {
            return error;
        }

        SparseArray sparseArray = new(property, layout.Set.SparseArray, layout.Pairs + layout.Set.Elements);

        return key.Accept(new MapKeyFactory(property, sparseArray, slotSize, keyProperty.ClassName, value, valueProperty.ClassName));
    }
}

file sealed class MapKeyFactory(
    UnrealProperty property,
    SparseArray sparseArray,
    int slotSize,
    string keyClass,
    PropertyAccess value,
    string valueClass) : AccessVisitor<Result<PropertyAccess>>
{
    public override Result<PropertyAccess> VisitOther(PropertyAccess access)
    {
        return new FailedAccess(property, CollectionError.KeyNotValue(property.Name, keyClass));
    }

    public override Result<PropertyAccess> VisitValue<TKey>(ValueAccess<TKey> access)
    {
        MapElements<TKey> elements = new(property, sparseArray, new SparseLookup<TKey>(sparseArray, access, slotSize), value);

        return value.Accept(new MapValueFactory<TKey>(property, elements, access, valueClass));
    }
}

file sealed class MapValueFactory<TKey>(
    UnrealProperty property,
    MapElements<TKey> elements,
    ValueAccess<TKey> key,
    string valueClass) : AccessVisitor<Result<PropertyAccess>>
{
    public override Result<PropertyAccess> VisitOther(PropertyAccess access)
    {
        return new CollectionStepAccess(property, elements, valueClass);
    }

    public override Result<PropertyAccess> VisitValue<TValue>(ValueAccess<TValue> access)
    {
        return new MapValueAccess<TKey, TValue>(property, elements, key, access);
    }
}

file sealed class MapElements<TKey>(
    UnrealProperty property,
    SparseArray sparseArray,
    SparseLookup<TKey> lookup,
    PropertyAccess value) : CollectionAccess(property)
{
    public override ValueAccess<int> Count { get; } = sparseArray.Count;

    public override PropertyAccess Element { get; } = value;

    public SparseLookup<TKey> Lookup { get; } = lookup;

    public SparseArray SparseArray { get; } = sparseArray;

    public override Result<Address> FindElement(IMemoryReader memory, Address container, ElementKey key)
    {
        if (!key.As<TKey>(this, Property.Name).TryUnwrap(out var value, out var error)
            || !Lookup.Find(memory, container, value, key).TryUnwrap(out var slot, out error))
        {
            return error;
        }

        return slot == Address.Zero ? key.NotFound(Property.Name) : slot;
    }
}

file sealed class MapValueAccess<TKey, TValue>(
    UnrealProperty property,
    MapElements<TKey> elements,
    ValueAccess<TKey> key,
    ValueAccess<TValue> value) : ValueAccess<Dictionary<TKey, TValue>>(property)
{
    // Keep the pairs of this read by key, because a torn read can show 1 key in 2 slots.
    // Keep the key of each slot, so a string key keeps its text while it doesn't change.
    // The access is shared by every object of the class, so a key of another object only costs a new text.
    private readonly Dictionary<TKey, TValue> _pairs = [];
    private TKey[] _keys = [];

    public override Result<Dictionary<TKey, TValue>> Read(IMemoryReader memory, Address container, Dictionary<TKey, TValue>? last)
    {
        var sparseArray = elements.SparseArray;

        if (!sparseArray.ReadSlots(memory, container).TryUnwrap(out var slots, out var error))
        {
            return error;
        }

        if (_keys.Length < slots.SlotCount)
        {
            _keys = new TKey[ValueBounds.MaxElements];
        }

        _pairs.Clear();

        for (int slot = 0; slot < slots.SlotCount; slot++)
        {
            if (!sparseArray.IsUsed(slot))
            {
                continue;
            }

            var pair = slots.Data + ((long)slot * elements.Lookup.SlotSize);

            if (!key.Read(memory, pair, _keys[slot]).TryUnwrap(out var pairKey, out error))
            {
                return error;
            }

            // Pass the value of the key in the caller's last read, so an equal string or array comes back as the same instance.
            TValue? lastValue = default;
            _ = last?.TryGetValue(pairKey, out lastValue);

            if (!value.Read(memory, pair, lastValue).TryUnwrap(out var pairValue, out error))
            {
                return error;
            }

            _keys[slot] = pairKey;
            _pairs[pairKey] = pairValue;
        }

        return last is not null && SamePairs(last) ? last : [with(_pairs)];
    }

    public override Result<Container> Step(IMemoryReader memory, Address container)
    {
        return new Container(container, null, elements);
    }

    private bool SamePairs(Dictionary<TKey, TValue> last)
    {
        if (last.Count != _pairs.Count)
        {
            return false;
        }

        var comparer = EqualityComparer<TValue>.Default;

        foreach (var pair in _pairs)
        {
            if (!last.TryGetValue(pair.Key, out var lastValue) || !comparer.Equals(lastValue, pair.Value))
            {
                return false;
            }
        }

        return true;
    }
}
