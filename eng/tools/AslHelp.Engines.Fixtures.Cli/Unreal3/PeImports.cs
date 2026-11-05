using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Unreal3;

/// <summary>
///     Reads the DLL names of the static import table of a PE file.
/// </summary>
/// <remarks>
///     Only the headers, the import descriptors and the names are read, so a large executable costs a few KB of reads.
///     Delay imports are not read.
/// </remarks>
internal static class PeImports
{
    private const int DescriptorNameOffset = 12;
    private const int DescriptorSize = 20;
    private const int MaxNameLength = 260;

    /// <summary>
    ///     Reads the DLL names of the import table of the PE file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The executable or DLL, such as <c>player.exe</c>.</param>
    /// <returns>
    ///     The DLL names in table order, as the file spells them. Empty for a file with no import directory.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist.</exception>
    /// <exception cref="InvalidDataException">
    ///     The file is not a PE file, the table or a name lies outside every section, the table has no null descriptor
    ///     within its directory size or a name is longer than 260 bytes.
    /// </exception>
    public static IReadOnlyList<string> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);

        PEHeaders headers;

        try
        {
            headers = new PEHeaders(file);
        }
        catch (BadImageFormatException exception)
        {
            throw Damaged(path, exception);
        }

        // Refuse a file without a PE header, because PEHeaders reads a file without the MZ signature as a COFF object.
        if (headers.PEHeader is not { } peHeader)
        {
            throw Damaged(path, null);
        }

        var directory = peHeader.ImportTableDirectory;

        if (directory.RelativeVirtualAddress == 0)
        {
            return [];
        }

        // Refuse a negative offset, because a damaged section header can point the table before the file.
        if (!headers.TryGetDirectoryOffset(directory, out int directoryOffset) || directoryOffset < 0)
        {
            throw Damaged(path, null);
        }

        // End the table at its directory size, at the raw end of its section and at the end of the file, so a table
        // without its null descriptor throws instead of reading the bytes after it as names.
        var section = headers.SectionHeaders[headers.GetContainingSectionIndex(directory.RelativeVirtualAddress)];
        long end = Math.Min((long)directoryOffset + directory.Size, (long)section.PointerToRawData + section.SizeOfRawData);
        end = Math.Min(end, file.Length);
        long offset = directoryOffset;

        List<string> names = [];
        Span<byte> descriptor = stackalloc byte[DescriptorSize];
        Span<byte> name = stackalloc byte[MaxNameLength];

        try
        {
            while (true)
            {
                if (offset + DescriptorSize > end)
                {
                    throw Damaged(path, null);
                }

                file.Position = offset;
                file.ReadExactly(descriptor);

                int nameRva = BinaryPrimitives.ReadInt32LittleEndian(descriptor[DescriptorNameOffset..]);

                if (nameRva == 0)
                {
                    return names;
                }

                file.Position = ToFileOffset(headers, nameRva, file.Length, path);
                int read = file.ReadAtLeast(name, 1, throwOnEndOfStream: false);
                int length = name[..read].IndexOf((byte)0);

                if (length < 0)
                {
                    throw Damaged(path, null);
                }

                names.Add(Encoding.ASCII.GetString(name[..length]));
                offset += DescriptorSize;
            }
        }
        catch (EndOfStreamException exception)
        {
            throw Damaged(path, exception);
        }
    }

    /// <summary>
    ///     Maps <paramref name="rva"/> to its offset in the file through the section that has its bytes.
    /// </summary>
    /// <param name="headers">The headers of the file.</param>
    /// <param name="rva">The relative virtual address.</param>
    /// <param name="length">The length of the file.</param>
    /// <param name="path">The path of the file, for messages.</param>
    /// <returns>
    ///     The file offset.
    /// </returns>
    /// <exception cref="InvalidDataException">No section has the bytes of <paramref name="rva"/> in the file.</exception>
    private static long ToFileOffset(PEHeaders headers, int rva, long length, string path)
    {
        int index = headers.GetContainingSectionIndex(rva);

        if (index < 0)
        {
            throw Damaged(path, null);
        }

        var section = headers.SectionHeaders[index];
        int relative = rva - section.VirtualAddress;
        long offset = (long)section.PointerToRawData + relative;

        // Check the raw size and the file too, because a section can be longer in memory than in the file and a damaged
        // section header can point anywhere.
        return relative < section.SizeOfRawData && offset >= 0 && offset < length
            ? offset
            : throw Damaged(path, null);
    }

    /// <summary>
    ///     Creates the exception for a file whose headers or import table cannot be read.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="inner">The exception of the failed read, if any.</param>
    /// <returns>
    ///     The exception.
    /// </returns>
    private static InvalidDataException Damaged(string path, Exception? inner)
    {
        return new InvalidDataException(SR.Format(UdkSR.PeImportsUnreadable, path), inner);
    }
}
