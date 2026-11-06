using AslHelp.Engines.Unreal;
using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Describes the name array that <see cref="UdkArrays.FindNames"/> found.
/// </summary>
/// <param name="Rvas">The RVAs of the headers of the array, of which there can be several for 1 data pointer.</param>
/// <param name="Layout">The offsets inside a name entry.</param>
/// <param name="Table">The reader of the array.</param>
internal sealed record UdkNameArray(
    IReadOnlyList<uint> Rvas,
    NameEntryLayout Layout,
    FlatNameTable Table);

/// <summary>
///     Describes the object array that <see cref="UdkArrays.FindObjects"/> found.
/// </summary>
/// <param name="Rvas">The RVAs of the headers of the array, of which there can be several for 1 data pointer.</param>
/// <param name="Array">The reader of the array.</param>
/// <param name="IndexOffset">The offset in an object of its position in the array.</param>
/// <param name="Pointers">The pointers in the array at the time of the search, without its empty positions.</param>
internal sealed record UdkObjectArray(
    IReadOnlyList<uint> Rvas,
    FlatObjectArray Array,
    int IndexOffset,
    IReadOnlyList<Address> Pointers);

/// <summary>
///     Finds the name array and the object array of a UDK player by what they contain.
/// </summary>
internal static class UdkArrays
{
    private const int EntryHeadLength = 0x40;
    private const int LargestOffset = 0x100;
    private const int MostNames = 1_000_000;
    private const int MostObjects = 4_000_000;
    private const int NotFound = -1;
    private const int SampleLength = 512;
    private const int SmallestArray = 16;

    /// <summary>
    ///     Finds the array whose first entries are <paramref name="firstNames"/>.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="moduleBase">The base of the executable in the process.</param>
    /// <param name="sections">The sections of the executable without code.</param>
    /// <param name="firstNames">The first names of the array in order.</param>
    /// <returns>
    ///     The name array.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     No array fits, 2 arrays with different data fit or the array has another name at a position of <paramref name="firstNames"/>.
    /// </exception>
    public static UdkNameArray FindNames(
        IMemoryReader memory,
        Address moduleBase,
        IReadOnlyList<UdkSection> sections,
        IReadOnlyList<string> firstNames)
    {
        var candidates = FindNameCandidates(memory, sections, firstNames).ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(UdkSR.FormatNameArrayNotFound(firstNames[0], firstNames[1]));
        }

        var (firstRva, data, layout) = candidates[0];
        int other = candidates.FindIndex(candidate => candidate.Data != data);

        if (other >= 0)
        {
            throw new InvalidOperationException(UdkSR.FormatNameArrayAmbiguous(Hex(firstRva), Hex(candidates[other].Rva)));
        }

        FlatNameTable table = new(memory, moduleBase + firstRva, layout);

        for (int id = 0; id < firstNames.Count; id++)
        {
            string? name = table.Read(id);

            if (!string.Equals(name, firstNames[id], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(UdkSR.FormatNameArrayWrongName(Hex(firstRva), name, id, firstNames[id]));
            }
        }

        return new UdkNameArray([.. candidates.Select(static candidate => candidate.Rva)], layout, table);
    }

    /// <summary>
    ///     Finds the array whose objects contain their own position at 1 offset.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="moduleBase">The base of the executable in the process.</param>
    /// <param name="sections">The sections of the executable without code.</param>
    /// <param name="nameRvas">The RVAs of the name array, which also reads as an array of pointers.</param>
    /// <returns>
    ///     The object array.
    /// </returns>
    /// <exception cref="InvalidOperationException">No array fits or the longest 2 arrays have different data and the same count.</exception>
    public static UdkObjectArray FindObjects(
        IMemoryReader memory,
        Address moduleBase,
        IReadOnlyList<UdkSection> sections,
        IReadOnlyList<uint> nameRvas)
    {
        var candidates = UdkSections.Arrays(sections, memory.Layout, SmallestArray, MostObjects)
            .Where(array => !nameRvas.Contains(array.Rva))
            .Select(array => (array.Rva, array.Data, array.Count, IndexOffset: FindIndexOffset(memory, new FlatObjectArray(memory, moduleBase + array.Rva))))
            .Where(static candidate => candidate.IndexOffset != NotFound)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(UdkSR.ObjectArrayNotFound);
        }

        // Take the longest array, because a shorter array that starts with the same objects also fits the sample.
        var (firstRva, data, count, indexOffset) = candidates.MaxBy(static candidate => candidate.Count);
        int other = candidates.FindIndex(candidate => candidate.Count == count && candidate.Data != data);

        if (other >= 0)
        {
            throw new InvalidOperationException(UdkSR.FormatObjectArrayAmbiguous(Hex(firstRva), Hex(candidates[other].Rva)));
        }

        FlatObjectArray array = new(memory, moduleBase + firstRva);

        return new UdkObjectArray(
            [.. candidates.Where(candidate => candidate.Data == data).Select(static candidate => candidate.Rva)],
            array,
            indexOffset,
            [.. array.Read(array.ReadCount()).Where(static obj => obj != Address.Zero)]);
    }

    /// <summary>
    ///     Lists the arrays that have room for <paramref name="firstNames"/> and whose 1st 2 entries are the 1st 2 of them.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="sections">The sections of the executable without code.</param>
    /// <param name="firstNames">The first names of the array in order.</param>
    /// <returns>
    ///     The RVA of each array, its data pointer and the offsets inside its entries.
    /// </returns>
    private static IEnumerable<(uint Rva, Address Data, NameEntryLayout Layout)> FindNameCandidates(
        IMemoryReader memory,
        IReadOnlyList<UdkSection> sections,
        IReadOnlyList<string> firstNames)
    {
        int least = Math.Max(SmallestArray, firstNames.Count);

        foreach ((uint rva, var data, _) in UdkSections.Arrays(sections, memory.Layout, least, MostNames))
        {
            if (FindEntryLayout(memory, data, firstNames[0], firstNames[1]) is { } layout)
            {
                yield return (rva, data, layout);
            }
        }
    }

    /// <summary>
    ///     Finds the index and name offsets that the 1st 2 entries at <paramref name="data"/> share.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="data">The data pointer of the array.</param>
    /// <param name="first">The name of the 1st entry.</param>
    /// <param name="second">The name of the 2nd entry.</param>
    /// <returns>
    ///     The offsets or <see langword="null"/> if the array does not fit.
    /// </returns>
    private static NameEntryLayout? FindEntryLayout(IMemoryReader memory, Address data, string first, string second)
    {
        if (!memory.ReadPointer(data).TryUnwrap(out var firstEntry)
            || !memory.ReadPointer(data + memory.Layout.PointerSize).TryUnwrap(out var secondEntry)
            || firstEntry == Address.Zero
            || secondEntry == Address.Zero
            || !memory.ReadArray<byte>(firstEntry, EntryHeadLength).TryUnwrap(out byte[]? firstHead)
            || !memory.ReadArray<byte>(secondEntry, EntryHeadLength).TryUnwrap(out byte[]? secondHead))
        {
            return null;
        }

        int name = IndexOfText(firstHead, first);

        if (name < 0 || IndexOfText(secondHead, second) != name)
        {
            return null;
        }

        // Search from the text down, because the int pair that sits nearest the text is the index.
        for (int at = (name - sizeof(int)) & -sizeof(int); at >= 0; at -= sizeof(int))
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(firstHead.AsSpan(at)) == 0
                && BinaryPrimitives.ReadInt32LittleEndian(secondHead.AsSpan(at)) == 1 << 1)
            {
                return new NameEntryLayout(at, name);
            }
        }

        return null;
    }

    /// <summary>
    ///     Finds the offset at which every sampled object of <paramref name="array"/> reads its own position.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="array">The array to sample.</param>
    /// <returns>
    ///     The offset or -1 if the array does not fit.
    /// </returns>
    private static int FindIndexOffset(IMemoryReader memory, FlatObjectArray array)
    {
        var sample = array
            .Read(SampleLength)
            .Select(static (obj, position) => (Object: obj, Position: position))
            .Where(static pair => pair.Object != Address.Zero)
            .ToList();

        if (sample.Count < SmallestArray)
        {
            return NotFound;
        }

        // Read 1 whole object first, because most arrays in the sections are false matches and trying each offset on them costs 60 reads.
        var (last, lastPosition) = sample[^1];

        if (!memory.ReadArray<int>(last, LargestOffset / sizeof(int)).TryUnwrap(out int[]? head))
        {
            return NotFound;
        }

        for (int at = sizeof(int); at < LargestOffset; at += sizeof(int))
        {
            int offset = at;

            if (head[at / sizeof(int)] == lastPosition
                && sample.TrueForAll(pair => memory.Read<int>(pair.Object + offset).UnwrapOr(-1) == pair.Position))
            {
                return at;
            }
        }

        return NotFound;
    }

    private static string Hex(uint rva)
    {
        return rva.ToString("X", CultureInfo.InvariantCulture);
    }

    private static int IndexOfText(byte[] bytes, string text)
    {
        return bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes($"{text}\0"));
    }
}
