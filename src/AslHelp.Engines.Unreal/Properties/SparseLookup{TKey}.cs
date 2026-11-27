using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Properties;

/// <summary>
///     Finds the slot of a key in the sparse array of a set or a map.
/// </summary>
/// <remarks>
///     Compares the key of each used slot, starting with the slot that the key matched last,
///     because a lookup without the hash of the engine can't jump to the slot.
/// </remarks>
/// <typeparam name="TKey">The type of a key.</typeparam>
/// <param name="sparseArray">The sparse array of the set or the map.</param>
/// <param name="keyAccess">The read of the key in a slot.</param>
/// <param name="slotSize">The size of 1 slot in bytes.</param>
internal sealed class SparseLookup<TKey>(
    SparseArray sparseArray,
    ValueAccess<TKey> keyAccess,
    int slotSize)
{
    private readonly ValueAccess<TKey> _keyAccess = keyAccess;
    private readonly SparseArray _sparseArray = sparseArray;

    /// <summary>
    ///     Gets the size of 1 slot in bytes.
    /// </summary>
    public int SlotSize { get; } = slotSize;

    /// <summary>
    ///     Finds the used slot whose key equals <paramref name="key"/>.
    /// </summary>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="container">The address of the object or struct with the set or the map.</param>
    /// <param name="key">The key in the key type.</param>
    /// <param name="elementKey">The key as the path wrote it, which keeps the slot of the match.</param>
    /// <returns>
    ///     The address of the slot, <see cref="Address.Zero"/> when the key isn't in a used slot or a failure when a read fails.
    /// </returns>
    public Result<Address> Find(IMemoryReader memory, Address container, TKey key, ElementKey elementKey)
    {
        if (!_sparseArray.ReadSlots(memory, container).TryUnwrap(out var slots, out var error))
        {
            return error;
        }

        int hint = elementKey.SlotHint;

        if (hint >= 0 && hint < slots.SlotCount && _sparseArray.IsUsed(hint))
        {
            var address = slots.Data + ((long)hint * SlotSize);

            if (!_keyAccess.Matches(memory, address, key).TryUnwrap(out bool matches, out error))
            {
                return error;
            }

            if (matches)
            {
                return address;
            }
        }

        for (int slot = 0; slot < slots.SlotCount; slot++)
        {
            if (slot == hint || !_sparseArray.IsUsed(slot))
            {
                continue;
            }

            var address = slots.Data + ((long)slot * SlotSize);

            if (!_keyAccess.Matches(memory, address, key).TryUnwrap(out bool matches, out error))
            {
                return error;
            }

            if (matches)
            {
                elementKey.SlotHint = slot;

                return address;
            }
        }

        return Address.Zero;
    }
}
