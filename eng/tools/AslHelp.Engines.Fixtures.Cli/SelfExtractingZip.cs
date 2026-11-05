using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Reads a zip that sits after an executable stub, as in a self-extracting installer, without running the stub.
/// </summary>
/// <remarks>
///     The offsets in such a zip count from the end of the stub and not from the start of the file.
///     The stub length is the distance between the central directory's position in the file and its stored offset.
/// </remarks>
internal static class SelfExtractingZip
{
    private const int EndRecordSize = 22;
    private const int Zip64EndRecordSize = 56;
    private const int Zip64LocatorSize = 20;

    /// <summary>
    ///     Opens the zip in <paramref name="path"/> for reading.
    /// </summary>
    /// <param name="path">The self-extracting file or a plain zip.</param>
    /// <returns>
    ///     The archive. Disposing it closes the file.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist.</exception>
    /// <exception cref="InvalidDataException">
    ///     The file has no end of central directory record or its offsets point outside the file.
    /// </exception>
    public static ZipArchive Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        FileStream? file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        PayloadStream? payload = null;

        try
        {
            payload = new PayloadStream(file, FindPayloadStart(file, path));
            file = null;

            ZipArchive archive = new(payload, ZipArchiveMode.Read, leaveOpen: false);
            payload = null;

            return archive;
        }
        finally
        {
            file?.Dispose();
            payload?.Dispose();
        }
    }

    /// <summary>
    ///     Extracts every entry of the zip in <paramref name="path"/> into <paramref name="destinationDirectory"/>.
    /// </summary>
    /// <remarks>
    ///     The zip is extracted into a sibling temp folder first and then moved into place,
    ///     so a failed extraction leaves no partial destination.
    /// </remarks>
    /// <param name="path">The self-extracting file or a plain zip.</param>
    /// <param name="destinationDirectory">The folder to create. It must not exist.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="path"/> or <paramref name="destinationDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FileNotFoundException"><paramref name="path"/> does not exist.</exception>
    /// <exception cref="IOException">
    ///     <paramref name="destinationDirectory"/> exists, an entry resolves outside it,
    ///     or the temp folder cannot be moved to it.
    /// </exception>
    /// <exception cref="InvalidDataException">The zip cannot be found or an entry is damaged.</exception>
    public static void Extract(string path, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(destinationDirectory);

        // Trim a trailing separator, so the temp folder sits next to the destination and not inside it.
        string destination = Path.TrimEndingDirectorySeparator(destinationDirectory);

        if (Path.Exists(destination))
        {
            throw new IOException(SR.Format(SR.ExtractDestinationExists, destination));
        }

        string tempDirectory = destination + TempPaths.PartialSuffix;

        // Delete a temp folder from a crashed run, so its files do not mix with this one's.
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }

        try
        {
            using var archive = Open(path);
            archive.ExtractToDirectory(tempDirectory);
        }
        catch
        {
            TempPaths.DeleteFolderQuietly(tempDirectory);
            throw;
        }

        Directory.Move(tempDirectory, destination);
    }

    /// <summary>
    ///     Finds where the zip starts in <paramref name="file"/> from the records at its end.
    /// </summary>
    /// <param name="file">The open file.</param>
    /// <param name="path">The path of the file, for messages.</param>
    /// <returns>
    ///     The stub length, 0 for a plain zip.
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     The file has no end of central directory record or its offsets point outside the file.
    /// </exception>
    private static long FindPayloadStart(FileStream file, string path)
    {
        // Read enough for the longest comment and the zip64 records before the end record.
        int tailLength = (int)Math.Min(file.Length, Zip64EndRecordSize + Zip64LocatorSize + EndRecordSize + ushort.MaxValue);
        long tailStart = file.Length - tailLength;

        byte[] tail = new byte[tailLength];
        file.Position = tailStart;
        file.ReadExactly(tail);

        // Search from the back, because the comment after the end record is at most 64 KB and rarely contains a signature.
        int end = tailLength < EndRecordSize ? -1 : tail.AsSpan(0, tailLength - EndRecordSize + 4).LastIndexOf("PK\u0005\u0006"u8);

        if (end < 0)
        {
            throw new InvalidDataException(SR.Format(SR.ZipNotFound, path));
        }

        long directorySize;
        long directoryOffset;
        int directoryEnd;

        int locator = end - Zip64LocatorSize;

        if (locator >= 0 && tail.AsSpan(locator).StartsWith("PK\u0006\u0007"u8))
        {
            // Expect the zip64 end record right before its locator, because the offset in the locator counts from the unknown stub too.
            int record = locator - Zip64EndRecordSize;

            if (record < 0 || !tail.AsSpan(record).StartsWith("PK\u0006\u0006"u8))
            {
                throw new InvalidDataException(SR.Format(SR.Zip64ExtraData, path));
            }

            directorySize = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(record + 40));
            directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(record + 48));
            directoryEnd = record;
        }
        else
        {
            directorySize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end + 12));
            directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end + 16));
            directoryEnd = end;
        }

        // Check each value before the subtraction, because crafted zip64 values overflow it.
        long directoryEndPosition = tailStart + directoryEnd;

        if (directorySize < 0
            || directoryOffset < 0
            || directorySize > directoryEndPosition
            || directoryOffset > directoryEndPosition - directorySize)
        {
            throw new InvalidDataException(SR.Format(SR.ZipOffsetsOutside, path));
        }

        return directoryEndPosition - directorySize - directoryOffset;
    }

    /// <summary>
    ///     Shows the part of a file after the stub as a stream of its own, so the zip offsets line up.
    /// </summary>
    /// <param name="file">The file. The stream disposes it.</param>
    /// <param name="start">The stub length.</param>
    private sealed class PayloadStream(
        FileStream file,
        long start) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => file.Length - start;

        public override long Position
        {
            get => file.Position - start;
            set => file.Position = value + start;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return file.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return file.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return origin switch
            {
                SeekOrigin.Begin => file.Seek(offset + start, SeekOrigin.Begin) - start,
                SeekOrigin.Current or SeekOrigin.End => file.Seek(offset, origin) - start,
                _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, SR.SeekOriginInvalid),
            };
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                file.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
