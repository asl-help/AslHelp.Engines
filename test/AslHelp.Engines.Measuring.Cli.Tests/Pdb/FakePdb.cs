using AsmResolver;
using AsmResolver.Symbols.Pdb.Leaves;
using AsmResolver.Symbols.Pdb.Metadata.Dbi;
using AsmResolver.Symbols.Pdb.Metadata.Info;
using AsmResolver.Symbols.Pdb.Metadata.Tpi;
using AsmResolver.Symbols.Pdb.Msf;
using AsmResolver.Symbols.Pdb.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

/// <summary>
///     Writes a small PDB file with the bytes MSVC writes, so tests can read a file through the real reader without a compiler.
/// </summary>
/// <param name="guid">The GUID of the PDB.</param>
/// <param name="sectionRvas">The RVAs for the section header stream or <see langword="null"/> to leave the stream out.</param>
internal sealed class FakePdb(
    Guid guid,
    IReadOnlyList<uint>? sectionRvas)
{
    private const int ExtraStreamCount = 11;
    private const uint FirstTypeIndex = 0x1000;
    private const ushort IntType = 0x0074;
    private const ushort MemberAccessPublic = 3;
    private const ushort NoStream = 0xFFFF;
    private const uint PropertyPast16Bits = 0x1_0000;
    private const int SectionHeaderSize = 40;
    private const int SectionHeaderSlot = 5;
    private const uint TpiHeaderSize = 56;
    private const ushort UnknownKind = 0x1499;
    private const int VirtualAddressAt = 12;

    private readonly List<byte> _symbols = [];
    private readonly List<byte> _types = [];

    private uint _typeCount;

    /// <summary>
    ///     Gets the size of each block of the file.
    /// </summary>
    public uint BlockSize { get; init; } = 4096;

    /// <summary>
    ///     Gets the age of the info stream.
    /// </summary>
    public uint InfoAge { get; init; } = 1;

    /// <summary>
    ///     Gets the age of the DBI stream.
    /// </summary>
    public uint DbiAge { get; init; } = 1;

    /// <summary>
    ///     Gets a value indicating whether the stream directory lists a nil stream right before the section header stream.
    /// </summary>
    public bool NilStreamBeforeSections { get; init; }

    /// <summary>
    ///     Gets the slot of the DBI stream to list an OMAP stream in or <see langword="null"/> to leave both slots empty.
    /// </summary>
    public int? OmapSlot { get; init; }

    /// <summary>
    ///     Gets the number of bytes to leave off the end of the section header stream.
    /// </summary>
    public int SectionStreamCut { get; init; }

    /// <summary>
    ///     Creates a member entry of a field list.
    /// </summary>
    /// <param name="name">The name of the member.</param>
    /// <param name="offset">The offset of the member.</param>
    /// <param name="type">The type index of the member, <c>int</c> by default.</param>
    /// <returns>
    ///     The bytes of the entry.
    /// </returns>
    public static byte[] Member(string name, ulong offset, uint type = IntType)
    {
        return Entry(writer =>
        {
            writer.Write((ushort)CodeViewLeafKind.Member);
            writer.Write(MemberAccessPublic);
            writer.Write(type);
            WriteNumeric(writer, offset);
            WriteName(writer, name);
        });
    }

    /// <summary>
    ///     Creates a base class entry of a field list.
    /// </summary>
    /// <param name="type">The type index of the base class.</param>
    /// <param name="offset">The offset of the base class.</param>
    /// <returns>
    ///     The bytes of the entry.
    /// </returns>
    public static byte[] BaseClass(uint type, ulong offset)
    {
        return Entry(writer =>
        {
            writer.Write((ushort)CodeViewLeafKind.BClass);
            writer.Write(MemberAccessPublic);
            writer.Write(type);
            WriteNumeric(writer, offset);
        });
    }

    /// <summary>
    ///     Creates a value entry of the field list of an enum.
    /// </summary>
    /// <param name="name">The name of the value.</param>
    /// <param name="value">The value.</param>
    /// <returns>
    ///     The bytes of the entry.
    /// </returns>
    public static byte[] Enumerate(string name, ulong value)
    {
        return Entry(writer =>
        {
            writer.Write((ushort)CodeViewLeafKind.Enumerate);
            writer.Write(MemberAccessPublic);
            WriteNumeric(writer, value);
            WriteName(writer, name);
        });
    }

    /// <summary>
    ///     Creates the entry that ends a field list and points at the list with the next members.
    /// </summary>
    /// <param name="next">The type index of the next list.</param>
    /// <returns>
    ///     The bytes of the entry.
    /// </returns>
    public static byte[] Continuation(uint next)
    {
        return Entry(writer =>
        {
            writer.Write((ushort)CodeViewLeafKind.Index);
            writer.Write((ushort)0);
            writer.Write(next);
        });
    }

    /// <summary>
    ///     Creates a field list entry of an unknown kind.
    /// </summary>
    /// <returns>
    ///     The bytes of the entry.
    /// </returns>
    public static byte[] UnknownEntry()
    {
        return Entry(static writer =>
        {
            writer.Write(UnknownKind);
            writer.Write((ushort)0);
        });
    }

    /// <summary>
    ///     Finds the stream directory in the bytes of a file.
    /// </summary>
    /// <param name="file">The bytes of the file.</param>
    /// <returns>
    ///     The file offset of the 1st block of the directory, which starts with the stream count and the size of each stream.
    /// </returns>
    public static int DirectoryAt(byte[] file)
    {
        const int BlockSizeAt = 32;
        const int DirectoryMapAt = 52;

        int blockSize = BitConverter.ToInt32(file, BlockSizeAt);
        int map = BitConverter.ToInt32(file, DirectoryMapAt) * blockSize;

        return BitConverter.ToInt32(file, map) * blockSize;
    }

    /// <summary>
    ///     Finds a stream in the bytes of a file, for a directory that fits 1 block.
    /// </summary>
    /// <param name="file">The bytes of the file.</param>
    /// <param name="stream">The index of the stream, which can't be empty.</param>
    /// <returns>
    ///     The file offset of the 1st block of the stream.
    /// </returns>
    public static int StreamAt(byte[] file, int stream)
    {
        const int BlockSizeAt = 32;

        int blockSize = BitConverter.ToInt32(file, BlockSizeAt);
        int directory = DirectoryAt(file);
        int blockAt = directory + sizeof(uint) + (BitConverter.ToInt32(file, directory) * sizeof(uint));

        for (int index = 0; index < stream; index++)
        {
            int size = BitConverter.ToInt32(file, directory + sizeof(uint) + (index * sizeof(uint)));
            blockAt += (size + blockSize - 1) / blockSize * sizeof(uint);
        }

        return BitConverter.ToInt32(file, blockAt) * blockSize;
    }

    /// <summary>
    ///     Adds a field list.
    /// </summary>
    /// <param name="entries">The bytes of the entries.</param>
    /// <returns>
    ///     The type index of the list.
    /// </returns>
    public uint AddFieldList(params byte[][] entries)
    {
        return AddType(CodeViewLeafKind.FieldList, writer =>
        {
            foreach (byte[] entry in entries)
            {
                writer.Write(entry);
            }
        });
    }

    /// <summary>
    ///     Adds a complete struct.
    /// </summary>
    /// <param name="name">The name of the struct.</param>
    /// <param name="size">The size of the struct.</param>
    /// <param name="fieldList">The type index of the field list.</param>
    public void AddStruct(string name, ulong size, uint fieldList)
    {
        _ = AddType(CodeViewLeafKind.Structure, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(fieldList);
            writer.Write(0u);
            writer.Write(0u);
            WriteNumeric(writer, size);
            WriteName(writer, name);
        });
    }

    /// <summary>
    ///     Adds an enum with an <c>int</c> base.
    /// </summary>
    /// <param name="name">The name of the enum.</param>
    /// <param name="fieldList">The type index of the field list.</param>
    /// <param name="attributes">The flags of the enum.</param>
    public void AddEnum(string name, uint fieldList, StructureAttributes attributes = default)
    {
        _ = AddType(CodeViewLeafKind.Enum, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)attributes);
            writer.Write((uint)IntType);
            writer.Write(fieldList);
            WriteName(writer, name);
        });
    }

    /// <summary>
    ///     Adds a forward reference to a struct.
    /// </summary>
    /// <param name="name">The name of the struct.</param>
    /// <returns>
    ///     The type index of the reference.
    /// </returns>
    public uint AddForward(string name)
    {
        return AddType(CodeViewLeafKind.Structure, writer =>
        {
            writer.Write((ushort)0);
            writer.Write((ushort)StructureAttributes.FwdRef);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            WriteNumeric(writer, 0);
            WriteName(writer, name);
        });
    }

    /// <summary>
    ///     Adds a class record with a 32-bit property word, with a bit set past the 16 bits of the older form.
    /// </summary>
    /// <param name="kind">The kind of the record.</param>
    /// <param name="name">The name of the type.</param>
    /// <param name="size">The size of the type.</param>
    /// <param name="fieldList">The type index of the field list.</param>
    /// <param name="attributes">The flags in the low 16 bits of the property word.</param>
    /// <param name="memberCount">The member count.</param>
    /// <returns>
    ///     The type index of the record.
    /// </returns>
    public uint AddClass2(
        CodeViewLeafKind kind,
        string name,
        ulong size,
        uint fieldList,
        StructureAttributes attributes = default,
        ulong memberCount = 1)
    {
        return AddType(kind, writer =>
        {
            writer.Write(PropertyPast16Bits | (uint)attributes);
            writer.Write(fieldList);

            if (kind != CodeViewLeafKind.Union2)
            {
                writer.Write(0u);
                writer.Write(0u);
            }

            WriteNumeric(writer, memberCount);
            WriteNumeric(writer, size);
            WriteName(writer, name);
        });
    }

    /// <summary>
    ///     Adds a type record that the reader only keeps as raw bytes.
    /// </summary>
    /// <param name="kind">The kind of the record.</param>
    /// <param name="body">The bytes after the kind.</param>
    public void AddOpaqueType(CodeViewLeafKind kind, params byte[] body)
    {
        _ = AddType(kind, writer => writer.Write(body));
    }

    /// <summary>
    ///     Adds a public symbol.
    /// </summary>
    /// <param name="name">The decorated name.</param>
    /// <param name="section">The 1-based section.</param>
    /// <param name="offset">The offset in the section.</param>
    /// <param name="isFunction">Whether the symbol is a function.</param>
    public void AddPublic(string name, ushort section, uint offset, bool isFunction)
    {
        byte[] record = Record((ushort)CodeViewSymbolType.Pub32, writer =>
        {
            writer.Write((uint)(isFunction ? PublicSymbolAttributes.Function : PublicSymbolAttributes.None));
            writer.Write(offset);
            writer.Write(section);
            WriteName(writer, name);
        });

        _symbols.AddRange(record);
    }

    /// <summary>
    ///     Adds a local data record, typed <c>int</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="section">The 1-based section.</param>
    /// <param name="offset">The offset in the section.</param>
    public void AddData(string name, ushort section, uint offset)
    {
        byte[] record = Record((ushort)CodeViewSymbolType.LData32, writer =>
        {
            writer.Write((uint)IntType);
            writer.Write(offset);
            writer.Write(section);
            WriteName(writer, name);
        });

        _symbols.AddRange(record);
    }

    /// <summary>
    ///     Adds a constant symbol, typed <c>int</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The bytes of the value, which start with its kind from 0x8000 on.</param>
    public void AddConstant(string name, params byte[] value)
    {
        byte[] record = Record((ushort)CodeViewSymbolType.Constant, writer =>
        {
            writer.Write((uint)IntType);
            writer.Write(value);
            WriteName(writer, name);
        });

        _symbols.AddRange(record);
    }

    /// <summary>
    ///     Writes the file.
    /// </summary>
    /// <param name="path">The file.</param>
    public void Write(string path)
    {
        const int NilStream = 5;

        ushort sectionStream = NilStreamBeforeSections ? (ushort)(NilStream + 1) : (ushort)NilStream;
        ushort symbolStream = (ushort)(sectionStream + 1);

        MsfFile msf = new(BlockSize);
        msf.Streams.Add(new MsfStream([]));
        msf.Streams.Add(new MsfStream(new InfoStream { UniqueId = guid, Age = InfoAge, Signature = 1 }.WriteIntoArray()));
        msf.Streams.Add(new MsfStream(TypeStream([.. _types], _typeCount)));
        msf.Streams.Add(new MsfStream(Dbi(sectionStream, symbolStream).WriteIntoArray()));
        msf.Streams.Add(new MsfStream(TypeStream([], 0)));

        if (NilStreamBeforeSections)
        {
            msf.Streams.Add(new MsfStream([]));
        }

        msf.Streams.Add(new MsfStream(SectionHeaders()));
        msf.Streams.Add(new MsfStream([.. _symbols]));
        msf.Write(path);

        if (NilStreamBeforeSections)
        {
            byte[] file = File.ReadAllBytes(path);
            int sizeAt = DirectoryAt(file) + sizeof(uint) + (NilStream * sizeof(uint));
            BitConverter.TryWriteBytes(file.AsSpan(sizeAt), uint.MaxValue);
            File.WriteAllBytes(path, file);
        }
    }

    private static byte[] TypeStream(byte[] records, uint count)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write((uint)TpiStreamVersion.V80);
        writer.Write(TpiHeaderSize);
        writer.Write(FirstTypeIndex);
        writer.Write(FirstTypeIndex + count);
        writer.Write((uint)records.Length);
        writer.Write(NoStream);
        writer.Write(NoStream);

        // Write zeros for the hash key size, the bucket count and the 3 offset and length pairs, because the reader doesn't use the hash stream.
        for (int index = 0; index < 8; index++)
        {
            writer.Write(0u);
        }

        writer.Write(records);
        writer.Flush();

        return stream.ToArray();
    }

    private static byte[] Entry(Action<BinaryWriter> body)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        body(writer);
        Pad(writer, stream);
        writer.Flush();

        return stream.ToArray();
    }

    private static byte[] Record(ushort kind, Action<BinaryWriter> body)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write((ushort)0);
        writer.Write(kind);
        body(writer);
        Pad(writer, stream);
        writer.Flush();

        byte[] record = stream.ToArray();
        BitConverter.TryWriteBytes(record, (ushort)(record.Length - sizeof(ushort)));

        return record;
    }

    private static void Pad(BinaryWriter writer, MemoryStream stream)
    {
        writer.Flush();

        while (stream.Length % 4 != 0)
        {
            writer.Write((byte)(0xF0 + (4 - (stream.Length % 4))));
            writer.Flush();
        }
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        writer.Write(Encoding.UTF8.GetBytes(name));
        writer.Write((byte)0);
    }

    private static void WriteNumeric(BinaryWriter writer, ulong value)
    {
        if (value < (ulong)CodeViewLeafKind.Numeric)
        {
            writer.Write((ushort)value);
        }
        else if (value <= ushort.MaxValue)
        {
            writer.Write((ushort)CodeViewLeafKind.UShort);
            writer.Write((ushort)value);
        }
        else if (value <= uint.MaxValue)
        {
            writer.Write((ushort)CodeViewLeafKind.ULong);
            writer.Write((uint)value);
        }
        else
        {
            writer.Write((ushort)CodeViewLeafKind.UQuadWord);
            writer.Write(value);
        }
    }

    private uint AddType(CodeViewLeafKind kind, Action<BinaryWriter> body)
    {
        _types.AddRange(Record((ushort)kind, body));

        return FirstTypeIndex + _typeCount++;
    }

    private DbiStream Dbi(ushort sectionStream, ushort symbolStream)
    {
        DbiStream dbi = new()
        {
            Age = DbiAge,
            GlobalStreamIndex = NoStream,
            PublicStreamIndex = NoStream,
            SymbolRecordStreamIndex = symbolStream,
        };

        for (int slot = 0; slot < ExtraStreamCount; slot++)
        {
            bool listed = slot == OmapSlot || (slot == SectionHeaderSlot && sectionRvas is not null);

            dbi.ExtraStreamIndices.Add(listed ? sectionStream : NoStream);
        }

        return dbi;
    }

    private byte[] SectionHeaders()
    {
        if (sectionRvas is null)
        {
            return [];
        }

        byte[] headers = new byte[sectionRvas.Count * SectionHeaderSize];

        for (int index = 0; index < sectionRvas.Count; index++)
        {
            BitConverter.TryWriteBytes(headers.AsSpan((index * SectionHeaderSize) + VirtualAddressAt), sectionRvas[index]);
        }

        return headers[..^SectionStreamCut];
    }
}
