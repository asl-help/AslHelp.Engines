using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Reads the file names of a Microsoft cabinet and unpacks its files with the Windows <c>expand.exe</c>.
/// </summary>
/// <remarks>
///     The file entries come before the data blocks, so reading the names reads only the header and the file entries. The read
///     goes forward only, so a zip entry stream works too.
/// </remarks>
internal static class Cabinet
{
    private const int FileCountOffset = 28;
    private const int FileEntrySize = 16;
    private const int FilesOffsetOffset = 16;
    private const int HeaderSize = 36;
    private const int MaxNameLength = 260;
    private const int NameIsUtf8 = 0x80;

    // Run expand.exe by its full path, because a GNU expand on PATH can shadow it.
    private static readonly string _expandPath = Path.Combine(Environment.SystemDirectory, "expand.exe");

    /// <summary>
    ///     Reads the names of the file entries of the cabinet in <paramref name="cabinet"/>.
    /// </summary>
    /// <param name="cabinet">The stream at the start of the cabinet. It needs no seek.</param>
    /// <returns>
    ///     The file names in cabinet order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="cabinet"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">
    ///     The stream does not start with <c>MSCF</c>, the file entries start inside the header, the stream ends before the
    ///     last name or a name is longer than 260 bytes.
    /// </exception>
    public static IReadOnlyList<string> ReadNames(Stream cabinet)
    {
        ArgumentNullException.ThrowIfNull(cabinet);

        Span<byte> header = stackalloc byte[HeaderSize];
        Span<byte> name = stackalloc byte[MaxNameLength];

        try
        {
            cabinet.ReadExactly(header);

            if (!header.StartsWith("MSCF"u8))
            {
                throw new InvalidDataException(SR.CabinetNoSignature);
            }

            uint filesOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[FilesOffsetOffset..]);
            int fileCount = BinaryPrimitives.ReadUInt16LittleEndian(header[FileCountOffset..]);

            if (filesOffset < HeaderSize)
            {
                throw new InvalidDataException(SR.Format(SR.CabinetFilesInHeader, filesOffset));
            }

            // Skip the reserved fields and the folder entries by reading them, because the stream may not seek.
            for (long skip = filesOffset - HeaderSize; skip > 0; skip -= name.Length)
            {
                cabinet.ReadExactly(name[..(int)Math.Min(skip, name.Length)]);
            }

            List<string> names = [with(fileCount)];
            var entry = header[..FileEntrySize];

            for (int i = 0; i < fileCount; i++)
            {
                cabinet.ReadExactly(entry);
                int length = ReadName(cabinet, name);
                var encoding = (entry[14] & NameIsUtf8) != 0 ? Encoding.UTF8 : Encoding.Latin1;

                names.Add(encoding.GetString(name[..length]));
            }

            return names;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException(SR.CabinetEndsEarly, exception);
        }
    }

    /// <summary>
    ///     Unpacks the files of <paramref name="cabinetPath"/> that match <paramref name="filter"/> into <paramref name="targetDirectory"/>.
    /// </summary>
    /// <param name="cabinetPath">The cabinet file.</param>
    /// <param name="filter">The file name pattern, such as <c>*.dll</c>.</param>
    /// <param name="targetDirectory">The folder to unpack into.</param>
    /// <param name="cancellationToken">The token that ends <c>expand.exe</c>.</param>
    /// <returns>
    ///     A task that completes once <c>expand.exe</c> exits with code 0.
    /// </returns>
    /// <exception cref="InvalidOperationException"><c>expand.exe</c> exits with a code other than 0.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task ExpandAsync(string cabinetPath, string filter, string targetDirectory, CancellationToken cancellationToken)
    {
        return ProcessRunner.RunAsync(ProcessRunner.NewStartInfo(_expandPath, cabinetPath, $"-F:{filter}", targetDirectory), cancellationToken);
    }

    /// <summary>
    ///     Reads a name up to its 0 byte into <paramref name="name"/>.
    /// </summary>
    /// <param name="cabinet">The stream at the name.</param>
    /// <param name="name">The buffer of 260 bytes.</param>
    /// <returns>
    ///     The length of the name without the 0 byte.
    /// </returns>
    /// <exception cref="EndOfStreamException">The stream ends before the 0 byte.</exception>
    /// <exception cref="InvalidDataException">The name is longer than 260 bytes.</exception>
    private static int ReadName(Stream cabinet, Span<byte> name)
    {
        for (int length = 0; length < name.Length; length++)
        {
            int value = cabinet.ReadByte();

            switch (value)
            {
                case -1:
                {
                    throw new EndOfStreamException();
                }
                case 0:
                {
                    return length;
                }
                default:
                {
                    name[length] = (byte)value;
                    break;
                }
            }
        }

        throw new InvalidDataException(SR.Format(SR.CabinetNameTooLong, MaxNameLength));
    }
}
