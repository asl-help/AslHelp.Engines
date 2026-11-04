using System;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Fixtures.Tests;

/// <summary>
///     Builds an executable whose 1st import DLL does not exist.
/// </summary>
/// <remarks>
///     The loader then stops the process with STATUS_DLL_NOT_FOUND before any of its code runs. A process with the
///     dialogs on shows a hard-error dialog first and ends the same way once someone clicks it.
/// </remarks>
internal static class MissingImportExecutable
{
    private const int ImportDescriptorNameOffset = 12;

    /// <summary>
    ///     Copies the system ping.exe to <c>player.exe</c> in <paramref name="folder"/> and overwrites the base name of
    ///     its 1st import DLL with '-' characters.
    /// </summary>
    /// <param name="folder">The folder to create the executable in.</param>
    /// <returns>
    ///     The path of the executable.
    /// </returns>
    /// <exception cref="InvalidDataException">The import table points outside every section.</exception>
    public static string Create(string folder)
    {
        string path = Path.Combine(folder, "player.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), path);

        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite);

        PEHeaders headers;
        using (PEReader reader = new(stream, PEStreamOptions.LeaveOpen))
        {
            headers = reader.PEHeaders;
        }

        int descriptorOffset = ToFileOffset(headers, headers.PEHeader!.ImportTableDirectory.RelativeVirtualAddress);

        Span<byte> nameRvaBytes = stackalloc byte[4];
        stream.Position = descriptorOffset + ImportDescriptorNameOffset;
        stream.ReadExactly(nameRvaBytes);

        int nameOffset = ToFileOffset(headers, BitConverter.ToInt32(nameRvaBytes));

        // Overwrite the base name only, so the loader still looks for a DLL with the same length of name and the
        // import table stays valid.
        stream.Position = nameOffset;
        int length = 0;
        while (stream.ReadByte() is int value and not '.' and not 0 and not -1)
        {
            length++;
        }

        stream.Position = nameOffset;
        for (int i = 0; i < length; i++)
        {
            stream.WriteByte((byte)'-');
        }

        return path;
    }

    private static int ToFileOffset(PEHeaders headers, int rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize)
            {
                return section.PointerToRawData + rva - section.VirtualAddress;
            }
        }

        throw new InvalidDataException($"No section of the executable contains the address 0x{rva:X}. Pass an executable with an import table.");
    }
}
