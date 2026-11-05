using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Packs folders into <c>.tar.zst</c> archives and unpacks them.
/// </summary>
internal static class Archive
{
    /// <summary>
    ///     Sets the zstd level to 3, because engine builds are tens of GB and a higher level is too slow.
    /// </summary>
    public const int CompressionLevel = 3;

    /// <summary>
    ///     Packs every file and folder under <paramref name="sourceDirectory"/> into <paramref name="archivePath"/>.
    /// </summary>
    /// <remarks>
    ///     Entries are sorted by their relative path and keep the last write time from disk,
    ///     so the same folder gives the same bytes.
    ///     Unix mode bits are not stored, so files unpacked on Linux or macOS lose their exec bit.
    ///     A file link is packed as a copy of its target.
    ///     Every zstd frame ends with a checksum of its content, so a damaged archive fails to unpack instead of giving wrong files.
    ///     The archive is written next to <paramref name="archivePath"/> first and then moved over it,
    ///     so a crash never leaves a half archive under the real name.
    /// </remarks>
    /// <param name="sourceDirectory">The folder to pack.</param>
    /// <param name="archivePath">The archive to write. An existing file is replaced and a missing folder is created.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="sourceDirectory"/> or <paramref name="archivePath"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="sourceDirectory"/> does not exist.</exception>
    /// <exception cref="IOException">
    ///     A folder sits at <paramref name="archivePath"/>, <paramref name="sourceDirectory"/> has a folder link,
    ///     a file under <paramref name="sourceDirectory"/> cannot be read,
    ///     or the complete temp file cannot be moved over <paramref name="archivePath"/>.
    /// </exception>
    public static void Pack(string sourceDirectory, string archivePath)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(archivePath);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(SR.Format(SR.PackFolderNotFound, sourceDirectory));
        }

        if (Directory.Exists(archivePath))
        {
            throw new IOException(SR.Format(SR.ArchivePathIsFolder, archivePath));
        }

        var entries = CollectEntries(sourceDirectory);
        string tempPath = Path.GetFullPath(archivePath + TempPaths.PartialSuffix);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        try
        {
            WriteArchive(entries, tempPath);
        }
        catch
        {
            TempPaths.DeleteFileQuietly(tempPath);
            throw;
        }

        // Keep the complete temp file when the move fails, because packing tens of GB again takes long.
        try
        {
            File.Move(tempPath, archivePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(SR.Format(SR.ArchiveNotReplaced, tempPath, archivePath), exception);
        }
    }

    /// <summary>
    ///     Unpacks <paramref name="archivePath"/> into <paramref name="destinationDirectory"/> and restores last write times.
    /// </summary>
    /// <remarks>
    ///     The archive is unpacked into a sibling temp folder first and then moved into place,
    ///     so a failed unpack leaves no partial destination.
    /// </remarks>
    /// <param name="archivePath">The archive to read.</param>
    /// <param name="destinationDirectory">The folder to create. It may exist if it is empty.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="archivePath"/> or <paramref name="destinationDirectory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FileNotFoundException"><paramref name="archivePath"/> does not exist.</exception>
    /// <exception cref="IOException">
    ///     <paramref name="destinationDirectory"/> is not empty or is a file,
    ///     or the complete temp folder cannot be moved to <paramref name="destinationDirectory"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    ///     An entry resolves outside <paramref name="destinationDirectory"/> or is neither a file nor a folder,
    ///     or the archive is cut short, its tar header is damaged, its zstd stream is damaged or its checksum does not match.
    ///     The message of a damaged archive has <paramref name="archivePath"/> in it.
    /// </exception>
    public static void Unpack(string archivePath, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(destinationDirectory);

        // Trim a trailing separator, so the temp folder sits next to the destination and not inside it.
        string destination = Path.TrimEndingDirectorySeparator(destinationDirectory);

        if (File.Exists(destination))
        {
            throw new IOException(SR.Format(SR.UnpackDestinationIsFile, destination));
        }

        bool destinationExists = Directory.Exists(destination);

        if (destinationExists && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            throw new IOException(SR.Format(SR.UnpackFolderNotEmpty, destination));
        }

        string tempDirectory = destination + TempPaths.PartialSuffix;

        // Delete a temp folder from a crashed run, so its files do not mix with this one's.
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }

        _ = Directory.CreateDirectory(tempDirectory);

        try
        {
            ExtractArchive(archivePath, tempDirectory);
        }
        catch
        {
            TempPaths.DeleteFolderQuietly(tempDirectory);
            throw;
        }

        // Keep the complete temp folder when the move fails, because unpacking tens of GB again takes long.
        try
        {
            // Delete the empty destination first, because Directory.Move fails on an existing folder.
            if (destinationExists)
            {
                Directory.Delete(destination);
            }

            Directory.Move(tempDirectory, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(SR.Format(SR.UnpackNotMoved, tempDirectory, destination), exception);
        }
    }

    /// <summary>
    ///     Lists every folder and file under <paramref name="sourceDirectory"/> in 1 pass, as its relative path with '/' separators.
    /// </summary>
    /// <param name="sourceDirectory">The folder to list.</param>
    /// <returns>
    ///     The entries, sorted ordinally by relative path. A folder path ends in '/'.
    /// </returns>
    /// <exception cref="IOException">A folder under <paramref name="sourceDirectory"/> is a link.</exception>
    private static List<(string Name, FileSystemInfo Info)> CollectEntries(string sourceDirectory)
    {
        // Skip no attributes and fail on unreadable folders, so hidden and system files are packed too.
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        List<(string Name, FileSystemInfo Info)> entries = [];

        foreach (var info in new DirectoryInfo(sourceDirectory).EnumerateFileSystemInfos("*", options))
        {
            string name = Path.GetRelativePath(sourceDirectory, info.FullName).Replace(Path.DirectorySeparatorChar, '/');

            if (info is DirectoryInfo)
            {
                // Refuse folder links before the enumeration recurses into them, because a link to a parent loops forever.
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException(SR.Format(SR.PackFolderIsLink, info.FullName));
                }

                name += "/";
            }

            entries.Add((name, info));
        }

        // Sort ordinally, so the order does not depend on the file system or the culture.
        entries.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return entries;
    }

    /// <summary>
    ///     Writes <paramref name="entries"/> as a zstd-compressed PAX tar to <paramref name="archivePath"/>.
    /// </summary>
    /// <param name="entries">The sorted entries. A folder name ends in '/'.</param>
    /// <param name="archivePath">The file to write.</param>
    private static void WriteArchive(List<(string Name, FileSystemInfo Info)> entries, string archivePath)
    {
        using FileStream file = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using CompressionStream compression = new(file, CompressionLevel, leaveOpen: false);
        compression.SetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers, Environment.ProcessorCount);

        // Add a content checksum to every frame, because a flipped byte in a raw block would otherwise unpack into a wrong file with no error.
        compression.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);

        using (TarWriter writer = new(compression, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach ((string name, var info) in entries)
            {
                WriteEntry(writer, name, info);
            }
        }

        // Write the 2 zero blocks that end a tar for an empty folder, because TarWriter writes them only after an entry.
        if (entries.Count == 0)
        {
            compression.Write(new byte[2 * 512]);
        }
    }

    /// <summary>
    ///     Writes the file or folder <paramref name="info"/> to <paramref name="writer"/> as <paramref name="name"/> with its last write time.
    /// </summary>
    /// <param name="writer">The tar writer.</param>
    /// <param name="name">The entry name. A folder name ends in '/'.</param>
    /// <param name="info">The file or folder from the enumeration, with its last write time already read.</param>
    private static void WriteEntry(TarWriter writer, string name, FileSystemInfo info)
    {
        if (info is DirectoryInfo)
        {
            PaxTarEntry folderEntry = new(TarEntryType.Directory, name)
            {
                ModificationTime = info.LastWriteTimeUtc,
            };

            writer.WriteEntry(folderEntry);
            return;
        }

        // Open the file as a stream, so the writer copies it in chunks instead of reading it whole.
        using FileStream file = new(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

        PaxTarEntry fileEntry = new(TarEntryType.RegularFile, name)
        {
            ModificationTime = info.LastWriteTimeUtc,
            DataStream = file,
        };

        writer.WriteEntry(fileEntry);
    }

    /// <summary>
    ///     Extracts every entry of <paramref name="archivePath"/> into <paramref name="rootDirectory"/>.
    /// </summary>
    /// <param name="archivePath">The archive to read.</param>
    /// <param name="rootDirectory">The existing, empty folder to extract into.</param>
    /// <exception cref="InvalidDataException">
    ///     An entry is refused, the archive is cut short, its tar header or zstd stream is damaged or its checksum does not match.
    /// </exception>
    private static void ExtractArchive(string archivePath, string rootDirectory)
    {
        string root = Path.GetFullPath(rootDirectory) + Path.DirectorySeparatorChar;
        List<(string Path, DateTime Time)> folders = [];
        string? lastFolder = null;

        try
        {
            using FileStream file = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using DecompressionStream decompression = new(file, leaveOpen: false);
            using TarReader reader = new(decompression, leaveOpen: false);

            while (ReadEntry(reader, archivePath) is { } entry)
            {
                ExtractEntry(entry, root, folders, ref lastFolder);
            }

            // Read the stream to its end, because the decoder checks the checksum only at the end of the frame and the tar
            // reader stops at the end mark.
            decompression.CopyTo(Stream.Null);
        }
        catch (Exception exception) when (exception is ZstdException or EndOfStreamException)
        {
            throw NewDamagedException(archivePath, exception);
        }

        // Set folder times after all files, because writing a file into a folder changes the folder's time.
        foreach ((string path, var time) in folders)
        {
            Directory.SetLastWriteTimeUtc(path, time);
        }
    }

    /// <summary>
    ///     Reads the next entry header from <paramref name="reader"/>.
    /// </summary>
    /// <param name="reader">The tar reader.</param>
    /// <param name="archivePath">The archive that <paramref name="reader"/> reads, for the message.</param>
    /// <returns>
    ///     The entry with its data still in the stream or <see langword="null"/> at the end of the tar.
    /// </returns>
    /// <exception cref="InvalidDataException">The tar header is damaged.</exception>
    private static TarEntry? ReadEntry(TarReader reader, string archivePath)
    {
        // Catch only the header read here, because ExtractEntry throws InvalidDataException with the entry name on purpose.
        try
        {
            return reader.GetNextEntry(copyData: false);
        }
        catch (InvalidDataException exception)
        {
            throw NewDamagedException(archivePath, exception);
        }
    }

    /// <summary>
    ///     Creates the exception for an archive that is damaged or cut short.
    /// </summary>
    /// <param name="archivePath">The archive, for the message.</param>
    /// <param name="inner">The exception from the decoder or the tar reader.</param>
    /// <returns>
    ///     The exception, with <paramref name="archivePath"/> in its message and <paramref name="inner"/> as its inner exception.
    /// </returns>
    private static InvalidDataException NewDamagedException(string archivePath, Exception inner)
    {
        return new InvalidDataException(SR.Format(SR.ArchiveDamaged, archivePath), inner);
    }

    /// <summary>
    ///     Extracts <paramref name="entry"/> under <paramref name="root"/>.
    /// </summary>
    /// <param name="entry">The entry to extract.</param>
    /// <param name="root">The full path of the folder to extract into, ending in a separator.</param>
    /// <param name="folders">The folders whose last write times are set after all entries.</param>
    /// <param name="lastFolder">
    ///     The folder that the last entry created or wrote into, so the files of 1 folder create it only once.
    /// </param>
    /// <exception cref="InvalidDataException">
    ///     The entry resolves outside <paramref name="root"/>, is neither a file nor a folder or uses a path that an earlier
    ///     entry took.
    /// </exception>
    private static void ExtractEntry(TarEntry entry, string root, List<(string Path, DateTime Time)> folders, ref string? lastFolder)
    {
        string path = ResolveInside(root, entry.Name);

        switch (entry.EntryType)
        {
            case TarEntryType.Directory:
            {
                CreateFolder(root, path, entry.Name);
                folders.Add((path, entry.ModificationTime.UtcDateTime));
                lastFolder = Path.TrimEndingDirectorySeparator(path);

                break;
            }
            case TarEntryType.RegularFile:
            case TarEntryType.V7RegularFile:
            {
                string folder = Path.GetDirectoryName(path)!;

                // Create the folder only when it changes, because the pack puts each folder before the files in it.
                if (!string.Equals(folder, lastFolder, StringComparison.Ordinal))
                {
                    CreateFolder(root, folder, entry.Name);
                    lastFolder = folder;
                }

                using var file = CreateFile(root, path, entry.Name);
                entry.DataStream?.CopyTo(file);

                // Set the time on the open handle after the last write, because a 2nd open by path costs 3 more calls per file.
                file.Flush();
                File.SetLastWriteTimeUtc(file.SafeFileHandle, entry.ModificationTime.UtcDateTime);

                break;
            }
            default:
            {
                // Refuse links and every other kind, because a link can point outside the destination.
                throw new InvalidDataException(SR.Format(SR.ArchiveEntryKind, entry.Name, entry.EntryType));
            }
        }
    }

    /// <summary>
    ///     Creates the folder <paramref name="path"/> of the entry <paramref name="name"/>.
    /// </summary>
    /// <param name="root">The full path of the folder to extract into, ending in a separator.</param>
    /// <param name="path">The full path of the folder.</param>
    /// <param name="name">The entry name, for the message.</param>
    /// <exception cref="InvalidDataException">An earlier entry put a file on the path.</exception>
    private static void CreateFolder(string root, string path, string name)
    {
        try
        {
            _ = Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && IsTaken(root, path))
        {
            throw new InvalidDataException(SR.Format(SR.ArchiveEntryTaken, name), exception);
        }
    }

    /// <summary>
    ///     Creates the file <paramref name="path"/> of the entry <paramref name="name"/> for writing.
    /// </summary>
    /// <param name="root">The full path of the folder to extract into, ending in a separator.</param>
    /// <param name="path">The full path of the file.</param>
    /// <param name="name">The entry name, for the message.</param>
    /// <returns>
    ///     The new, empty file.
    /// </returns>
    /// <exception cref="InvalidDataException">An earlier entry took the path.</exception>
    private static FileStream CreateFile(string root, string path, string name)
    {
        try
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && IsTaken(root, path))
        {
            throw new InvalidDataException(SR.Format(SR.ArchiveEntryTaken, name), exception);
        }
    }

    /// <summary>
    ///     Checks whether an earlier entry took <paramref name="path"/> or put a file on a folder above it.
    /// </summary>
    /// <remarks>
    ///     The unpack writes into a fresh folder, so only the entries of the archive can be there.
    /// </remarks>
    /// <param name="root">The full path of the folder to extract into, ending in a separator.</param>
    /// <param name="path">The full path of the entry under <paramref name="root"/>.</param>
    /// <returns>
    ///     <see langword="true"/> if something exists at <paramref name="path"/> or a folder above it under
    ///     <paramref name="root"/> is a file.
    /// </returns>
    private static bool IsTaken(string root, string path)
    {
        if (Path.Exists(path))
        {
            return true;
        }

        for (string? folder = Path.GetDirectoryName(path); folder is not null && folder.Length >= root.Length; folder = Path.GetDirectoryName(folder))
        {
            if (File.Exists(folder))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Resolves <paramref name="name"/> against <paramref name="root"/> and checks that the result stays under it.
    /// </summary>
    /// <param name="root">The full path of the folder to extract into, ending in a separator.</param>
    /// <param name="name">The entry name.</param>
    /// <returns>
    ///     The full path of the entry.
    /// </returns>
    /// <exception cref="InvalidDataException">The entry resolves outside <paramref name="root"/>.</exception>
    private static string ResolveInside(string root, string name)
    {
        string path = Path.GetFullPath(Path.Combine(root, name));

        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException(SR.Format(SR.ArchiveEntryOutside, name));
        }

        return path;
    }
}
