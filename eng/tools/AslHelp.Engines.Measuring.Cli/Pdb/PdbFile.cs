using AslHelp.Engines.Measuring.Cli.Files.Errors;
using AslHelp.Engines.Measuring.Cli.Pdb.Errors;
using AsmResolver;
using AsmResolver.IO;
using AsmResolver.Symbols.Pdb;
using AsmResolver.Symbols.Pdb.Leaves;
using AsmResolver.Symbols.Pdb.Metadata.Dbi;
using AsmResolver.Symbols.Pdb.Metadata.Tpi;
using AsmResolver.Symbols.Pdb.Msf;
using System;
using System.Collections.Generic;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Reads the layouts, constants and addresses of a native MSVC PDB, for x64 and x86 images.
/// </summary>
/// <remarks>
///     Maps the file instead of reading it, because a PDB can have 500 MB or more. Each index gets 1 pass over its streams when the file opens.
/// </remarks>
internal sealed class PdbFile : IDisposable
{
    // Covers the length and the kind of a type record, because every record starts with both.
    private const int MinimumRecordSize = 2 * sizeof(ushort);

    private readonly ConstantIndex _constants;
    private readonly MemoryMappedInputFile _file;
    private readonly LayoutIndex _layouts;
    private readonly SymbolIndex _symbols;

    private bool _disposed;

    private PdbFile(
        MemoryMappedInputFile file,
        PdbIdentity identity,
        LayoutIndex layouts,
        SymbolIndex symbols,
        ConstantIndex constants)
    {
        _constants = constants;
        _file = file;
        _layouts = layouts;
        _symbols = symbols;
        Identity = identity;
    }

    /// <summary>
    ///     Gets the build of the PDB, to compare with an executable.
    /// </summary>
    public PdbIdentity Identity { get; }

    /// <summary>
    ///     Opens the PDB at <paramref name="path"/> and builds its indexes.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>
    ///     The PDB or a failure with <paramref name="path"/> when the file doesn't open, is not a valid PDB,
    ///     doesn't have valid section headers or has OMAP streams.
    /// </returns>
    public static Result<PdbFile> Open(string path)
    {
        MemoryMappedInputFile? file = null;

        try
        {
            // Refuse an empty file, because mapping one throws and leaves the file open until a finalizer runs.
            if (new FileInfo(path).Length == 0)
            {
                return PdbError.NotPdb(path, SR.PdbEmpty);
            }

            file = new MemoryMappedInputFile(path);
            var result = Read(file, path);

            if (result.IsOk)
            {
                // Keep the mapping open, because field lists read from it after this returns.
                file = null;
            }

            return result;
        }
        catch (Exception exception) when (file is null && exception is ArgumentException or NotSupportedException)
        {
            // Report a path that the file APIs refuse like a missing file, because the mapping is not open yet.
            return FileError.Unreadable(path, exception.Message);
        }
        catch (Exception exception) when (IsBadData(exception))
        {
            return PdbError.NotPdb(path, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileError.Unreadable(path, exception.Message);
        }
        finally
        {
            file?.Dispose();
        }
    }

    /// <summary>
    ///     Checks for an exception that bad bytes in a PDB cause, in the reader or in the checked arithmetic on its values.
    /// </summary>
    /// <remarks>
    ///     AsmResolver indexes its block array with values from the file, throws <see cref="ArgumentException"/> for a stream size that wraps
    ///     and <see cref="NotSupportedException"/> for a stream version or a block size it doesn't read.
    ///     Running out of memory never counts, because <see cref="Read"/> checks the type count before AsmResolver sizes an array by it.
    /// </remarks>
    /// <param name="exception">The exception.</param>
    /// <returns>
    ///     <see langword="true"/> if bad bytes in the file caused the exception.
    /// </returns>
    public static bool IsBadData(Exception exception)
    {
        return exception is ArgumentException
            or BadImageFormatException
            or EndOfStreamException
            or IndexOutOfRangeException
            or NotSupportedException
            or OverflowException;
    }

    /// <inheritdoc cref="ConstantIndex.Find(string)"/>
    /// <exception cref="ObjectDisposedException">The file is closed.</exception>
    public Result<long> ConstantValue(string name)
    {
        // Refuse a closed file, because enum field lists read from the mapping and an unmapped address ends the process.
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _constants.Find(name);
    }

    /// <inheritdoc cref="LayoutIndex.Find(string)"/>
    /// <exception cref="ObjectDisposedException">The file is closed.</exception>
    public Result<IReadOnlyList<TypeLayout>> FindLayouts(string typeName)
    {
        // Refuse a closed file, because field lists read from the mapping and an unmapped address ends the process.
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _layouts.Find(typeName);
    }

    /// <inheritdoc cref="SymbolIndex.FunctionRva(string)"/>
    public Result<uint> FunctionRva(string name)
    {
        return _symbols.FunctionRva(name);
    }

    /// <inheritdoc cref="SymbolIndex.GlobalRva(string)"/>
    public Result<uint> GlobalRva(string name)
    {
        return _symbols.GlobalRva(name);
    }

    /// <inheritdoc cref="SymbolIndex.HasGlobalAt(uint)"/>
    public bool HasGlobalAt(uint rva)
    {
        return _symbols.HasGlobalAt(rva);
    }

    /// <summary>
    ///     Closes the mapping of the file.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _file.Dispose();
    }

    private static Result<PdbFile> Read(MemoryMappedInputFile file, string path)
    {
        var msf = MsfFile.FromFile(file);
        NilStreams.Restore(msf, file);

        var image = PdbImage.FromFile(msf, new PdbReaderParameters(new StrictErrorListener()));

        // Read the DBI stream again, because PdbImage only has the age of the info stream.
        var dbi = DbiStream.FromReader(msf.Streams[DbiStream.StreamIndex].CreateReader());
        uint[]? sectionRvas = SectionHeaders.ReadRvas(msf, dbi);

        if (sectionRvas is null)
        {
            return PdbError.NoSectionHeaders(path);
        }

        // Refuse OMAP, because symbol offsets then point at the image from before a post-link tool rewrote it and each RVA would be wrong.
        if (SectionHeaders.HasOmap(dbi))
        {
            return PdbError.OmapUnsupported(path);
        }

        var types = TpiStream.FromReader(msf.Streams[TpiStream.TpiStreamIndex].CreateReader());

        // Refuse more types than the records have room for, because AsmResolver sizes an array by the count and a bad count runs out of memory.
        if (types.TypeIndexEnd - (long)types.TypeIndexBegin > types.TypeRecordsByteCount / MinimumRecordSize)
        {
            return PdbError.NotPdb(path, SR.FormatPdbTypeCountTooLarge(types.TypeIndexEnd - (long)types.TypeIndexBegin, types.TypeRecordsByteCount));
        }

        var layouts = LayoutIndex.Build(Class2Records.Translate(image), new FieldListContinuations(image, types).Next);
        var symbols = SymbolIndex.Build(image.Symbols, sectionRvas);

        // Read the constant symbols from the bytes, because AsmResolver reads their values as 2 bytes.
        var constants = ConstantIndex.Build(image.GetLeafRecords(), msf.Streams[dbi.SymbolRecordStreamIndex].CreateReader());

        return new PdbFile(file, new PdbIdentity(image.UniqueId, (int)dbi.Age), layouts, symbols, constants);
    }
}

/// <summary>
///     Restores the nil streams in the stream list of an MSF file.
/// </summary>
/// <remarks>
///     AsmResolver leaves a stream with the nil size out of its list.
///     That moves each later stream to a lower index than the DBI stream and the modules use, so a read would get the wrong stream.
/// </remarks>
file static class NilStreams
{
    private const uint NilSize = uint.MaxValue;

    /// <summary>
    ///     Puts an empty stream at the directory index of each nil stream.
    /// </summary>
    /// <param name="msf">The MSF file.</param>
    /// <param name="file">The input of the MSF file.</param>
    public static void Restore(MsfFile msf, MemoryMappedInputFile file)
    {
        var reader = file.CreateReader();
        var header = MsfSuperBlock.FromReader(ref reader);

        uint blockSize = header.BlockSize;
        uint blockCount = (header.DirectoryByteCount + blockSize - 1) / blockSize;
        var map = file.CreateReader(file.BaseAddress + ((ulong)header.DirectoryMapIndex * blockSize), 0, blockCount * sizeof(uint));
        var blocks = new IDataSource[blockCount];

        for (int index = 0; index < blocks.Length; index++)
        {
            blocks[index] = new DataSourceSlice(reader.DataSource, file.BaseAddress + ((ulong)map.ReadUInt32() * blockSize), blockSize);
        }

        var directory = new MsfStream(new MsfStreamDataSource(header.DirectoryByteCount, blockSize, blocks)).CreateReader();
        int streamCount = directory.ReadInt32();

        for (int stream = 0; stream < streamCount; stream++)
        {
            if (directory.ReadUInt32() == NilSize)
            {
                msf.Streams.Insert(stream, new MsfStream([]));
            }
        }
    }
}

/// <summary>
///     Reads the section header stream, a copy of the section table of the executable.
/// </summary>
file static class SectionHeaders
{
    private const int HeaderSize = 40;
    private const ushort NoStream = 0xFFFF;
    private const int OmapFromSourceSlot = 4;
    private const int OmapToSourceSlot = 3;
    private const int StreamSlot = 5;
    private const int VirtualAddressAt = 12;

    /// <summary>
    ///     Checks for the OMAP streams a post-link tool writes when it rewrites an image.
    /// </summary>
    /// <param name="dbi">The DBI stream.</param>
    /// <returns>
    ///     <see langword="true"/> if the DBI stream lists either stream.
    /// </returns>
    public static bool HasOmap(DbiStream dbi)
    {
        var streams = dbi.ExtraStreamIndices;

        return streams.Count > OmapFromSourceSlot && (streams[OmapToSourceSlot] != NoStream || streams[OmapFromSourceSlot] != NoStream);
    }

    /// <summary>
    ///     Reads the RVA of each section.
    /// </summary>
    /// <param name="msf">The streams of the PDB.</param>
    /// <param name="dbi">The DBI stream, which lists the stream.</param>
    /// <returns>
    ///     The RVAs or <see langword="null"/> if the PDB doesn't have the stream or the stream isn't a whole number of headers.
    /// </returns>
    public static uint[]? ReadRvas(MsfFile msf, DbiStream dbi)
    {
        var streams = dbi.ExtraStreamIndices;

        if (streams.Count <= StreamSlot || streams[StreamSlot] == NoStream)
        {
            return null;
        }

        var reader = msf.Streams[streams[StreamSlot]].CreateReader();

        // Refuse a stream without whole headers, because each symbol of a missing section would read as absent.
        if (reader.Length == 0 || reader.Length % HeaderSize != 0)
        {
            return null;
        }

        uint[] rvas = new uint[reader.Length / HeaderSize];

        for (uint index = 0; index < rvas.Length; index++)
        {
            reader.RelativeOffset = (index * HeaderSize) + VirtualAddressAt;
            rvas[index] = reader.ReadUInt32();
        }

        return rvas;
    }
}

/// <summary>
///     Reads the class records with a 32-bit property word, which AsmResolver keeps as raw bytes.
/// </summary>
/// <remarks>
///     The layout comes from <c>TypeRecordMapping.cpp</c> in LLVM, because <c>cvinfo.h</c> doesn't list these records.
///     A class that derives from such a record or has a member of such a type still fails its lookup,
///     because AsmResolver resolves those type indexes in its own table.
/// </remarks>
file static class Class2Records
{
    private const int DerivedAndShapeSize = 2 * sizeof(uint);

    /// <summary>
    ///     Replaces each such record of the type stream with the record type of its older form.
    /// </summary>
    /// <param name="image">The PDB.</param>
    /// <returns>
    ///     The records of the type stream.
    /// </returns>
    public static IEnumerable<ITpiLeaf> Translate(PdbImage image)
    {
        foreach (var leaf in image.GetLeafRecords())
        {
            yield return leaf is UnknownCodeViewLeaf
            {
                LeafKind: CodeViewLeafKind.Class2
                    or CodeViewLeafKind.Interface2
                    or CodeViewLeafKind.Structure2
                    or CodeViewLeafKind.Union2
            } unknown
                ? Read(unknown, image)
                : leaf;
        }
    }

    private static CodeViewCompositeTypeRecord Read(UnknownCodeViewLeaf leaf, PdbImage image)
    {
        bool isUnion = leaf.LeafKind == CodeViewLeafKind.Union2;

        BinaryStreamReader reader = new(leaf.Data);

        // Keep the low 16 bits, because they have the same flags as the older form.
        var attributes = (StructureAttributes)unchecked((ushort)reader.ReadUInt32());
        uint fieldList = reader.ReadUInt32();

        if (!isUnion)
        {
            reader.Offset += DerivedAndShapeSize;
        }

        _ = ReadNumeric(ref reader);

        ulong size = ReadNumeric(ref reader);
        var name = reader.ReadUtf8String();

        FieldListLeaf? fields = null;

        if (fieldList != 0 && !image.TryGetLeafRecord(fieldList, out fields))
        {
            throw new BadImageFormatException(SR.FormatPdbFieldListMissing(name));
        }

        return isUnion
            ? new UnionTypeRecord(size) { Name = name, StructureAttributes = attributes, Fields = fields }
            : new ClassTypeRecord(CodeViewLeafKind.Class, name, Utf8String.Empty, size, attributes, null!) { Fields = fields };
    }

    private static ulong ReadNumeric(ref BinaryStreamReader reader)
    {
        var kind = (CodeViewLeafKind)reader.ReadUInt16();

        return kind switch
        {
            < CodeViewLeafKind.Numeric => (ushort)kind,
            CodeViewLeafKind.UShort => reader.ReadUInt16(),
            CodeViewLeafKind.ULong => reader.ReadUInt32(),
            CodeViewLeafKind.UQuadWord => reader.ReadUInt64(),
            _ => throw new BadImageFormatException(SR.FormatPdbNumericKindUnsupported($"{(ushort)kind:X4}")),
        };
    }
}

/// <summary>
///     Finds the list that continues a field list past the 64 KB limit of a record.
/// </summary>
/// <remarks>
///     AsmResolver reads the last entry of such a list, <c>LF_INDEX</c>, as an unknown leaf and drops it, so this reads that entry from the bytes.
/// </remarks>
/// <param name="image">The PDB, which has the record of the next list.</param>
/// <param name="types">The type stream, which has the bytes of each record.</param>
file sealed class FieldListContinuations(
    PdbImage image,
    TpiStream types)
{
    private const int EntrySize = 8;

    /// <summary>
    ///     Finds the list that continues <paramref name="list"/>.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <returns>
    ///     The next list or <see langword="null"/> if <paramref name="list"/> is the last.
    /// </returns>
    public FieldListLeaf? Next(FieldListLeaf list)
    {
        if (!types.TryGetLeafRecordReader(list.TypeIndex, out var reader))
        {
            return null;
        }

        ushort length = reader.ReadUInt16();

        if (length < sizeof(ushort) + EntrySize)
        {
            return null;
        }

        reader.RelativeOffset = (uint)(sizeof(ushort) + length - EntrySize);

        var kind = (CodeViewLeafKind)reader.ReadUInt16();
        ushort padding = reader.ReadUInt16();
        uint next = reader.ReadUInt32();

        return kind == CodeViewLeafKind.Index && padding == 0 && image.TryGetLeafRecord(next, out FieldListLeaf? leaf) ? leaf : null;
    }
}

/// <summary>
///     Throws what the reader reports, because it would otherwise drop the members or symbols behind a record it can't read.
/// </summary>
file sealed class StrictErrorListener : IErrorListener
{
    /// <inheritdoc/>
    public void MarkAsFatal()
    {
    }

    /// <inheritdoc/>
    public void RegisterException(Exception exception)
    {
        // Let the end of a continued field list through, because FieldListContinuations reads it.
        // Compare the text, because only the text has the kind and the last 8 bytes of a record can't show an unknown entry before them.
        if (!exception.Message.EndsWith($"leaf {nameof(CodeViewLeafKind.Index)}.", StringComparison.Ordinal))
        {
            throw exception;
        }
    }
}
