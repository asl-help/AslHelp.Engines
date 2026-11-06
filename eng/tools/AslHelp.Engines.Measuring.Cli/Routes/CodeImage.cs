using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents the code of an executable file.
/// </summary>
/// <param name="Is64Bit">Whether the code is x64 and not x86.</param>
/// <param name="ImageBase">The preferred image base, which the absolute addresses in the code assume.</param>
/// <param name="ImageSize">The size of the image in memory.</param>
/// <param name="Sections">The executable sections in file order.</param>
internal sealed record CodeImage(
    bool Is64Bit,
    ulong ImageBase,
    uint ImageSize,
    IReadOnlyList<CodeSection> Sections)
{
    /// <summary>
    ///     Reads the executable sections and the headers of the file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>
    ///     The code of the file.
    /// </returns>
    /// <exception cref="BadImageFormatException">The file is not a PE file or not an x86 or x64 executable.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static CodeImage Read(string path)
    {
        using PEReader reader = new(File.OpenRead(path));

        var headers = ReadHeaders(reader, path);
        var peHeader = headers.PEHeader!;

        bool is64Bit = headers.CoffHeader.Machine switch
        {
            Machine.I386 => false,
            Machine.Amd64 => true,
            _ => throw new BadImageFormatException(SR.FormatExecutableMachineUnsupported(path), path),
        };

        List<CodeSection> sections = [];

        foreach (var section in headers.SectionHeaders)
        {
            if ((section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0)
            {
                byte[] bytes = ImmutableCollectionsMarshal.AsArray(reader.GetSectionData(section.VirtualAddress).GetContent())!;

                sections.Add(new CodeSection((uint)section.VirtualAddress, bytes));
            }
        }

        return new CodeImage(is64Bit, peHeader.ImageBase, (uint)peHeader.SizeOfImage, sections);
    }

    private static PEHeaders ReadHeaders(PEReader reader, string path)
    {
        try
        {
            // Check the optional header, because PEReader reads a file without the PE signature as an object file with no error.
            return reader.PEHeaders.PEHeader is null ? throw new BadImageFormatException(SR.FormatExecutableNotPe(path), path) : reader.PEHeaders;
        }
        catch (BadImageFormatException exception) when (exception.FileName is null)
        {
            // Add the path, because the reason ends up in the report without any other context.
            throw new BadImageFormatException(SR.FormatExecutableNotPe(path), path, exception);
        }
    }
}
