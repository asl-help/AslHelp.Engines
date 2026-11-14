using AslHelp.Engines.Unreal.Errors;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal;

/// <summary>
///     Provides the read of a name entry that stores its own index, which <see cref="FlatNameTable"/> and <see cref="IndirectNameTable"/> share.
/// </summary>
internal static class NameEntry
{
    private const int WideBit = 1;

    /// <summary>
    ///     Reads the text of the entry at <paramref name="entry"/> and checks its index against <paramref name="id"/>.
    /// </summary>
    /// <remarks>
    ///     Reads the text up to its terminator, because the engine allocates an entry only as long as its name.
    /// </remarks>
    /// <param name="memory">The memory to read from.</param>
    /// <param name="id">The id of the name.</param>
    /// <param name="entry">The address of the entry.</param>
    /// <param name="layout">The offsets inside the entry.</param>
    /// <param name="maxLength">The largest number of chars in front of the terminator.</param>
    /// <returns>
    ///     The text or a failure with the id, the entry and the failed read when <paramref name="entry"/> is <see cref="Address.Zero"/>,
    ///     a read fails or the entry has another id.
    /// </returns>
    public static Result<string> Read(IMemoryReader memory, int id, Address entry, NameEntryLayout layout, int maxLength)
    {
        if (entry == Address.Zero)
        {
            return NameTableError.EntryNull(id);
        }

        var indexAddress = entry + layout.Index;

        if (!memory.Read<int>(indexAddress).TryUnwrap(out int index, out var error))
        {
            return NameTableError.IndexUnreadable(id, entry, indexAddress, layout.Index, error);
        }

        if ((index >> 1) != id)
        {
            return NameTableError.IndexMismatch(id, entry, index, layout.Index);
        }

        var type = (index & WideBit) != 0 ? StringType.Utf16 : StringType.Ascii;
        var textAddress = entry + layout.Name;

        return memory.ReadString(textAddress, type, maxLength).TryUnwrap(out string? text, out error)
            ? text
            : NameTableError.TextUnreadable(id, entry, textAddress, layout.Name, error);
    }
}
