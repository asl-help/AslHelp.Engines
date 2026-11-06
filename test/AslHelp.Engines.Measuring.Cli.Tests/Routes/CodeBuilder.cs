using AslHelp.Engines.Measuring.Cli.Routes;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Routes;

/// <summary>
///     Builds x86 or x64 code byte by byte, with the operands computed from the wanted RVA, so a test needs no real executable.
/// </summary>
/// <param name="is64Bit">Whether the code is x64.</param>
/// <param name="imageBase">The preferred image base.</param>
/// <param name="rva">The RVA of the section.</param>
internal sealed class CodeBuilder(
    bool is64Bit,
    ulong imageBase = CodeBuilder.DefaultBase,
    uint rva = CodeBuilder.CodeRva)
{
    /// <summary>
    ///     Represents the default RVA of the section.
    /// </summary>
    public const uint CodeRva = 0x1000;

    /// <summary>
    ///     Represents the default preferred image base.
    /// </summary>
    public const ulong DefaultBase = 0x0040_0000;

    /// <summary>
    ///     Represents the size of each image.
    /// </summary>
    public const uint ImageSize = 0x0010_0000;

    private readonly List<byte> _bytes = [];

    /// <summary>
    ///     Adds bytes as they are.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Raw(params ReadOnlySpan<byte> bytes)
    {
        _bytes.AddRange(bytes);

        return this;
    }

    /// <summary>
    ///     Adds the absolute address of an RVA.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Absolute(uint target)
    {
        return Int32(unchecked((int)(uint)(imageBase + target)));
    }

    /// <summary>
    ///     Adds a displacement to an RVA, relative to the end of an instruction.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <param name="trailing">The number of bytes of the instruction after the displacement.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Relative(uint target, int trailing = 0)
    {
        long next = rva + _bytes.Count + sizeof(int) + trailing;

        return Int32((int)(target - next));
    }

    /// <summary>
    ///     Adds 1-byte instructions that do nothing.
    /// </summary>
    /// <param name="count">The number of instructions.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Nops(int count)
    {
        for (int index = 0; index < count; index++)
        {
            _ = Raw(0x90);
        }

        return this;
    }

    /// <summary>
    ///     Adds a return.
    /// </summary>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Ret()
    {
        return Raw(0xC3);
    }

    /// <summary>
    ///     Adds <c>mov eax, [target]</c> in the x86 form without a ModRM byte.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder MovEax(uint target)
    {
        return Raw(0xA1).Absolute(target);
    }

    /// <summary>
    ///     Adds <c>mov ecx, [target]</c> for x86.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder MovEcx(uint target)
    {
        return Raw(0x8B, 0x0D).Absolute(target);
    }

    /// <summary>
    ///     Adds <c>mov edx, [target]</c> for x86.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder MovEdx(uint target)
    {
        return Raw(0x8B, 0x15).Absolute(target);
    }

    /// <summary>
    ///     Adds <c>push target</c> for x86.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Push(uint target)
    {
        return Raw(0x68).Absolute(target);
    }

    /// <summary>
    ///     Adds <c>mov rax, [rip + displacement]</c> to an RVA for x64.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder MovRax(uint target)
    {
        return Raw(0x48, 0x8B, 0x05).Relative(target);
    }

    /// <summary>
    ///     Adds <c>lea rcx, [rip + displacement]</c> to an RVA for x64.
    /// </summary>
    /// <param name="target">The RVA.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder LeaRcx(uint target)
    {
        return Raw(0x48, 0x8D, 0x0D).Relative(target);
    }

    /// <summary>
    ///     Adds a near call with a 4-byte displacement.
    /// </summary>
    /// <param name="displacement">The displacement.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder Call(int displacement)
    {
        return Raw(0xE8).Int32(displacement);
    }

    /// <summary>
    ///     Adds <c>mov eax, [ecx + 0x1A8]</c> for x86.
    /// </summary>
    /// <returns>
    ///     This builder.
    /// </returns>
    public CodeBuilder MovFromField()
    {
        return Raw(0x8B, 0x81).Int32(0x1A8);
    }

    /// <summary>
    ///     Creates the image with the bytes as its only section.
    /// </summary>
    /// <returns>
    ///     The image.
    /// </returns>
    public CodeImage Build()
    {
        return new CodeImage(is64Bit, imageBase, ImageSize, [Section()]);
    }

    /// <summary>
    ///     Creates the section with the bytes.
    /// </summary>
    /// <returns>
    ///     The section.
    /// </returns>
    public CodeSection Section()
    {
        return new CodeSection(rva, [.. _bytes]);
    }

    /// <summary>
    ///     Writes <paramref name="image"/> as a PE file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="image">The image.</param>
    /// <param name="machine">The machine in the COFF header or <see langword="null"/> for the machine of the image.</param>
    public static void WritePe(string path, CodeImage image, Machine? machine = null)
    {
        const int CoffAt = 0x44;
        const int FileAlignment = 0x200;

        int optionalSize = image.Is64Bit ? 0xF0 : 0xE0;
        int optionalAt = CoffAt + 20;
        int sectionsAt = optionalAt + optionalSize;
        int headersSize = (sectionsAt + (image.Sections.Count * 40) + FileAlignment - 1) & -FileAlignment;

        int fileSize = headersSize;

        foreach (var section in image.Sections)
        {
            fileSize += section.Bytes.Length;
        }

        byte[] file = new byte[fileSize];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), CoffAt - 4);
        file[CoffAt - 4] = (byte)'P';
        file[CoffAt - 3] = (byte)'E';

        var written = machine ?? (image.Is64Bit ? Machine.Amd64 : Machine.I386);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(CoffAt), (ushort)written);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(CoffAt + 2), (ushort)image.Sections.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(CoffAt + 16), (ushort)optionalSize);

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(optionalAt), (ushort)(image.Is64Bit ? 0x20B : 0x10B));

        if (image.Is64Bit)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(optionalAt + 24), image.ImageBase);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalAt + 28), (uint)image.ImageBase);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalAt + 32), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalAt + 36), FileAlignment);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalAt + 56), image.ImageSize);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(optionalAt + 60), (uint)headersSize);

        int rawAt = headersSize;

        for (int index = 0; index < image.Sections.Count; index++)
        {
            var section = image.Sections[index];
            int at = sectionsAt + (index * 40);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 8), section.Bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 12), section.Rva);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 16), section.Bytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 20), rawAt);
            const SectionCharacteristics Executable = SectionCharacteristics.MemRead | SectionCharacteristics.MemExecute;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 36), (uint)Executable);
            section.Bytes.CopyTo(file, rawAt);
            rawAt += section.Bytes.Length;
        }

        File.WriteAllBytes(path, file);
    }

    private CodeBuilder Int32(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);

        return Raw(bytes);
    }
}
