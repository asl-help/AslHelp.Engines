using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Writes a small PE32 file by hand with an import table of the given DLL names.
/// </summary>
/// <remarks>
///     The file is shaped like a linker output, with 1 <c>.idata</c> section at RVA 0x1000 and file offset 0x200, so
///     <see cref="System.Reflection.PortableExecutable.PEHeaders"/> parses it. It doesn't have code and never runs.
/// </remarks>
internal static class FakeExecutable
{
    /// <summary>
    ///     Represents the offset of the name RVA in an import descriptor.
    /// </summary>
    public const int DescriptorNameOffset = 12;

    /// <summary>
    ///     Represents the size of an import descriptor.
    /// </summary>
    public const int DescriptorSize = 20;

    /// <summary>
    ///     Represents the file offset of the 1st import descriptor.
    /// </summary>
    public const int ImportTableOffset = SectionOffset;

    /// <summary>
    ///     Represents the file offset of the PointerToRawData field of the only section header.
    /// </summary>
    public const int SectionPointerOffset = SectionHeaderOffset + 20;

    private const int FileAlignment = 0x200;
    private const int OptionalHeaderOffset = PeOffset + 4 + 20;
    private const int OptionalHeaderSize = 0xE0;
    private const int PeOffset = 0x40;
    private const int SectionAlignment = 0x1000;
    private const int SectionHeaderOffset = OptionalHeaderOffset + OptionalHeaderSize;
    private const int SectionOffset = 0x200;
    private const int SectionRva = 0x1000;

    /// <summary>
    ///     Writes a PE32 file at <paramref name="path"/> that imports <paramref name="imports"/> in that order.
    /// </summary>
    /// <param name="path">The file to create. Missing folders are created.</param>
    /// <param name="imports">The DLL names of the import table. None writes a zero import directory.</param>
    public static void Write(string path, params string[] imports)
    {
        // Lay out the section as descriptors, the null descriptor, 1 shared empty thunk table and the names.
        int thunkOffset = (imports.Length + 1) * DescriptorSize;
        int namesOffset = thunkOffset + 4;
        int[] nameOffsets = new int[imports.Length];
        int dataSize = namesOffset;

        for (int i = 0; i < imports.Length; i++)
        {
            nameOffsets[i] = dataSize;
            dataSize += Encoding.ASCII.GetByteCount(imports[i]) + 1;
        }

        int rawSize = Align(dataSize, FileAlignment);
        byte[] file = new byte[SectionOffset + rawSize];
        var bytes = file.AsSpan();

        // Write the DOS header with e_lfanew, then the PE signature.
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes[0x3C..], PeOffset);
        "PE\0\0"u8.CopyTo(bytes[PeOffset..]);

        // Write the COFF header: I386, 1 section, EXECUTABLE_IMAGE | 32BIT_MACHINE.
        var coff = bytes[(PeOffset + 4)..];
        BinaryPrimitives.WriteUInt16LittleEndian(coff, 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[16..], OptionalHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[18..], 0x0102);

        var optional = bytes[OptionalHeaderOffset..];
        BinaryPrimitives.WriteUInt16LittleEndian(optional, 0x10B);
        BinaryPrimitives.WriteInt32LittleEndian(optional[8..], rawSize);
        BinaryPrimitives.WriteInt32LittleEndian(optional[24..], SectionRva);
        BinaryPrimitives.WriteInt32LittleEndian(optional[28..], 0x400000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[32..], SectionAlignment);
        BinaryPrimitives.WriteInt32LittleEndian(optional[36..], FileAlignment);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[40..], 4);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[48..], 4);
        BinaryPrimitives.WriteInt32LittleEndian(optional[56..], SectionRva + Align(dataSize, SectionAlignment));
        BinaryPrimitives.WriteInt32LittleEndian(optional[60..], SectionOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(optional[68..], 3);
        BinaryPrimitives.WriteInt32LittleEndian(optional[72..], 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[76..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[80..], 0x100000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[84..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(optional[92..], 16);

        if (imports.Length > 0)
        {
            // Point data directory 1, the import table, at the descriptors.
            BinaryPrimitives.WriteInt32LittleEndian(optional[104..], SectionRva);
            BinaryPrimitives.WriteInt32LittleEndian(optional[108..], thunkOffset);
        }

        var section = bytes[SectionHeaderOffset..];
        ".idata"u8.CopyTo(section);
        BinaryPrimitives.WriteInt32LittleEndian(section[8..], dataSize);
        BinaryPrimitives.WriteInt32LittleEndian(section[12..], SectionRva);
        BinaryPrimitives.WriteInt32LittleEndian(section[16..], rawSize);
        BinaryPrimitives.WriteInt32LittleEndian(section[20..], SectionOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(section[36..], 0xC0000040);

        var data = bytes[SectionOffset..];

        for (int i = 0; i < imports.Length; i++)
        {
            var descriptor = data[(i * DescriptorSize)..];
            BinaryPrimitives.WriteInt32LittleEndian(descriptor, SectionRva + thunkOffset);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[DescriptorNameOffset..], SectionRva + nameOffsets[i]);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[16..], SectionRva + thunkOffset);
            _ = Encoding.ASCII.GetBytes(imports[i], data[nameOffsets[i]..]);
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, file);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
