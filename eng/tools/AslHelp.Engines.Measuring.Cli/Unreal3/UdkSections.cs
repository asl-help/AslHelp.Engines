using AslHelp.Memory;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Reads the sections of <c>UDK.exe</c> without code from the running process and lists what they contain.
/// </summary>
internal static class UdkSections
{
    private const int PageSize = 4096;

    /// <summary>
    ///     Reads every section of <paramref name="executablePath"/> without code from <paramref name="memory"/>.
    /// </summary>
    /// <param name="executablePath">The file of the running executable, for its section table.</param>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="moduleBase">The base of the executable in the process.</param>
    /// <returns>
    ///     The sections in file order.
    /// </returns>
    /// <exception cref="BadImageFormatException">The file is not a PE file.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static IReadOnlyList<UdkSection> Read(string executablePath, IMemoryReader memory, Address moduleBase)
    {
        using PEReader reader = new(File.OpenRead(executablePath));

        // Check the optional header, because PEReader reads a file without the PE signature as an object file with no error.
        if (reader.PEHeaders.PEHeader is null)
        {
            throw new BadImageFormatException(UdkSR.FormatExecutableNotPe(executablePath));
        }

        List<UdkSection> sections = [];

        foreach (var section in reader.PEHeaders.SectionHeaders)
        {
            if ((section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0)
            {
                byte[] bytes = new byte[section.VirtualSize];
                ReadPages(memory, moduleBase + section.VirtualAddress, bytes);

                sections.Add(new UdkSection((uint)section.VirtualAddress, bytes));
            }
        }

        return sections;
    }

    /// <summary>
    ///     Lists every pointer-aligned position in <paramref name="sections"/> that reads as an array, which is a data pointer,
    ///     a count in range and a capacity no smaller than the count.
    /// </summary>
    /// <param name="sections">The sections.</param>
    /// <param name="layout">The layout of the memory.</param>
    /// <param name="least">The smallest count.</param>
    /// <param name="most">The largest count.</param>
    /// <returns>
    ///     The RVA of each array, its data pointer and its count, in the order of <paramref name="sections"/>.
    /// </returns>
    public static IEnumerable<(uint Rva, Address Data, int Count)> Arrays(
        IReadOnlyList<UdkSection> sections,
        MemoryLayout layout,
        int least,
        int most)
    {
        int pointerSize = layout.PointerSize;

        foreach (var section in sections)
        {
            byte[] bytes = section.Bytes;

            for (int at = 0; at + pointerSize + (2 * sizeof(int)) <= bytes.Length; at += pointerSize)
            {
                var data = ReadPointer(bytes, at, pointerSize);
                int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + pointerSize));
                int capacity = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + pointerSize + sizeof(int)));

                if (data != Address.Zero && count >= least && count <= most && capacity >= count)
                {
                    yield return (section.Rva + (uint)at, data, count);
                }
            }
        }
    }

    /// <summary>
    ///     Lists every pointer-aligned position in <paramref name="sections"/> that is not 0.
    /// </summary>
    /// <param name="sections">The sections.</param>
    /// <param name="layout">The layout of the memory.</param>
    /// <returns>
    ///     The RVA and the value of each position, in the order of <paramref name="sections"/>.
    /// </returns>
    public static IEnumerable<(uint Rva, Address Value)> Pointers(IReadOnlyList<UdkSection> sections, MemoryLayout layout)
    {
        int pointerSize = layout.PointerSize;

        foreach (var section in sections)
        {
            byte[] bytes = section.Bytes;

            for (int at = 0; at + pointerSize <= bytes.Length; at += pointerSize)
            {
                var value = ReadPointer(bytes, at, pointerSize);

                if (value != Address.Zero)
                {
                    yield return (section.Rva + (uint)at, value);
                }
            }
        }
    }

    /// <summary>
    ///     Fills <paramref name="bytes"/> from <paramref name="start"/> page by page.
    /// </summary>
    /// <param name="memory">The memory of the process.</param>
    /// <param name="start">The address of the 1st byte.</param>
    /// <param name="bytes">The buffer, whose bytes stay 0 for a page that does not read.</param>
    private static void ReadPages(IMemoryReader memory, Address start, byte[] bytes)
    {
        for (int at = 0; at < bytes.Length; at += PageSize)
        {
            // Ignore a failed read, because a section often has pages that the process has not committed.
            _ = memory.Read(start + at, bytes.AsSpan(at, Math.Min(PageSize, bytes.Length - at)));
        }
    }

    private static Address ReadPointer(byte[] bytes, int at, int pointerSize)
    {
        return new Address(
            pointerSize == sizeof(ulong)
                ? BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at))
                : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)));
    }
}
