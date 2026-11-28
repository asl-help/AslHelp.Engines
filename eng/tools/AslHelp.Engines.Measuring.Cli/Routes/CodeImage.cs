using AslHelp.Engines.Measuring.Cli.Files;
using AslHelp.Engines.Measuring.Cli.Files.Errors;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Routes;

/// <summary>
///     Represents the code of an executable file.
/// </summary>
/// <remarks>
///     Keeps the file open until <see cref="Dispose"/>, because the bytes of the parts point into the memory of the reader,
///     which maps the file or copies it into memory.
/// </remarks>
internal sealed class CodeImage : IDisposable
{
    private IMemoryOwner<byte>? _block;
    private PEReader? _reader;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CodeImage"/> class over bytes that the caller owns.
    /// </summary>
    /// <param name="is64Bit">Whether the code is x64 and not x86.</param>
    /// <param name="imageBase">The preferred image base, which the absolute addresses in the code assume.</param>
    /// <param name="imageSize">The size of the image in memory.</param>
    /// <param name="sections">The executable sections in file order.</param>
    /// <param name="data">The headers and the sections without code in file order or <see langword="null"/> for none.</param>
    public CodeImage(
        bool is64Bit,
        ulong imageBase,
        uint imageSize,
        IReadOnlyList<CodeSection> sections,
        IReadOnlyList<CodeSection>? data = null)
    {
        Is64Bit = is64Bit;
        ImageBase = imageBase;
        ImageSize = imageSize;
        Sections = sections;
        Data = data ?? [];
    }

    /// <summary>
    ///     Gets the headers and the sections without code in file order, which the loader maps next to the code.
    /// </summary>
    /// <remarks>
    ///     The rest of the image up to <see cref="ImageSize"/> reads as zeros at run time.
    /// </remarks>
    public IReadOnlyList<CodeSection> Data { get; }

    /// <summary>
    ///     Gets the preferred image base, which the absolute addresses in the code assume.
    /// </summary>
    public ulong ImageBase { get; }

    /// <summary>
    ///     Gets the size of the image in memory.
    /// </summary>
    public uint ImageSize { get; }

    /// <summary>
    ///     Gets whether the code is x64 and not x86.
    /// </summary>
    public bool Is64Bit { get; }

    /// <summary>
    ///     Gets the executable sections in file order.
    /// </summary>
    public IReadOnlyList<CodeSection> Sections { get; }

    /// <summary>
    ///     Reads the sections and the headers of the file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>
    ///     The code of the file or a failure with <paramref name="path"/> when the file does not open, is not a PE file, has a
    ///     section outside the file or is not an x86 or x64 executable. The caller disposes the code.
    /// </returns>
    public static Result<CodeImage> Read(string path)
    {
        // Let PEReader map the file, because it maps a large file instead of reading it and frees the mapping on dispose.
        if (!PeFile.Open(path).TryUnwrap(out var opened, out var error))
        {
            return error;
        }

        var reader = opened;

        try
        {
            var read = Read(opened, path);

            if (read.IsOk)
            {
                // Keep the reader open, because the image disposes it.
                reader = null;
            }

            return read;
        }
        finally
        {
            reader?.Dispose();
        }
    }

    /// <summary>
    ///     Closes the file and frees the memory of its bytes.
    /// </summary>
    public void Dispose()
    {
        // Dispose the block first, because a block that outlives the reader would read freed memory.
        _block?.Dispose();
        _reader?.Dispose();
    }

    private static Result<CodeImage> Read(PEReader reader, string path)
    {
        var headers = reader.PEHeaders;
        var peHeader = headers.PEHeader!;

        bool? is64Bit = headers.CoffHeader.Machine switch
        {
            Machine.I386 => false,
            Machine.Amd64 => true,
            _ => null,
        };

        if (is64Bit is null)
        {
            return FileError.MachineUnsupported(path);
        }

        // Refuse a negative header size, because PEReader does not check it and the slice below would throw.
        if (peHeader.SizeOfHeaders < 0)
        {
            return FileError.NotPe(path);
        }

        PEMemoryBlock file;

        try
        {
            file = reader.GetEntireImage();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileError.Unreadable(path, exception.Message);
        }

        var block = MappedBlock.Of(file);
        var bytes = block.Memory;

        // Cut at the end of the file, because the loader fills the rest of the headers with zeros.
        List<CodeSection> sections = [];
        List<CodeSection> data = [new CodeSection(0, bytes[..Math.Min(peHeader.SizeOfHeaders, file.Length)])];

        foreach (var section in headers.SectionHeaders)
        {
            // Take the bytes that the loader maps, because a section with a smaller virtual size ends there and a section
            // without one is on disk whole.
            int length = section.VirtualSize == 0 ? section.SizeOfRawData : Math.Min(section.VirtualSize, section.SizeOfRawData);

            // Refuse a section outside the file or above 2 GB, because PEReader does not check the section headers against the file.
            if (section.VirtualAddress < 0 || section.PointerToRawData < 0 || length < 0 || section.PointerToRawData > file.Length - length)
            {
                block.Dispose();

                return FileError.NotPe(path);
            }

            CodeSection part = new((uint)section.VirtualAddress, bytes.Slice(section.PointerToRawData, length));
            bool isCode = (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0;

            (isCode ? sections : data).Add(part);
        }

        return new CodeImage(is64Bit.Value, peHeader.ImageBase, (uint)peHeader.SizeOfImage, sections, data)
        {
            _block = block,
            _reader = reader
        };
    }
}

// Exposes the image that a PEReader owns as memory without a copy. The reader frees the image, so the image disposes the block first.
file sealed unsafe class MappedBlock(
    byte* start,
    int length) : MemoryManager<byte>
{
    private bool _disposed;

    public static IMemoryOwner<byte> Of(PEMemoryBlock block)
    {
        return new MappedBlock(block.Pointer, block.Length);
    }

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new Span<byte>(start, length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new MemoryHandle(start + elementIndex);
    }

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
    }
}
