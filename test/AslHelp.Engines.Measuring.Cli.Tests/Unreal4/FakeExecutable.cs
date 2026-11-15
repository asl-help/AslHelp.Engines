using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

/// <summary>
///     Writes a PE file with code and a CodeView entry that points at a PDB, so a test can read a player without a real build.
/// </summary>
internal static class FakeExecutable
{
    private const int DataPointerAt = 24;
    private const int DataRvaAt = 20;
    private const int DataSizeAt = 16;
    private const int DebugDirectoryAt32 = 0xE8;
    private const int DebugDirectoryAt64 = 0xF8;
    private const int EntrySize = 28;
    private const int SectionRawAt = 0x200;
    private const int TypeAt = 12;

    /// <summary>
    ///     Writes the file with the code in its only section, followed by the debug directory.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="code">The builder with the code at the default section RVA, which gets the debug directory added.</param>
    /// <param name="guid">The GUID of the PDB.</param>
    /// <param name="age">The age of the PDB.</param>
    /// <param name="machine">The machine in the COFF header or <see langword="null"/> for the machine of the code.</param>
    public static void Write(string path, CodeBuilder code, Guid guid, int age, Machine? machine = null)
    {
        int entryAt = code.Section().Bytes.Length;

        _ = code.Raw(Entry(entryAt, guid, age));

        var image = code.Build();
        CodeBuilder.WritePe(path, image, machine);

        byte[] file = File.ReadAllBytes(path);
        int directoryAt = image.Is64Bit ? DebugDirectoryAt64 : DebugDirectoryAt32;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(directoryAt), CodeBuilder.CodeRva + (uint)entryAt);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(directoryAt + sizeof(uint)), EntrySize);
        File.WriteAllBytes(path, file);
    }

    private static byte[] Entry(int entryAt, Guid guid, int age)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write(Encoding.ASCII.GetBytes("RSDS"));
        writer.Write(guid.ToByteArray());
        writer.Write(age);
        writer.Write(Encoding.UTF8.GetBytes("file.pdb"));
        writer.Write((byte)0);
        writer.Flush();

        byte[] data = stream.ToArray();
        byte[] entry = new byte[EntrySize + data.Length];
        int dataAt = entryAt + EntrySize;

        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(TypeAt), (int)DebugDirectoryEntryType.CodeView);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(DataSizeAt), data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(DataRvaAt), CodeBuilder.CodeRva + (uint)dataAt);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(DataPointerAt), SectionRawAt + dataAt);
        data.CopyTo(entry, EntrySize);

        return entry;
    }
}
