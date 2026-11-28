using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Makes room on the drive of the output folder before every install, by removing what a later build can get back.
/// </summary>
internal static class DiskSpace
{
    /// <summary>
    ///     Represents the folder of the downloaded installers under the output folder.
    /// </summary>
    public const string DownloadsFolder = "downloads";

    /// <summary>
    ///     Represents the folder of the installed engines under the output folder.
    /// </summary>
    public const string EnginesFolder = "engines";

    /// <summary>
    ///     Represents the free space that every build keeps on top of what its toolchain measures: 20 GiB.
    /// </summary>
    public const long MarginBytes = 20L << 30;

    private const long BytesPerGib = 1L << 30;

    /// <summary>
    ///     Reads the free space on the drive of <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">A folder on the drive, which doesn't have to exist.</param>
    /// <returns>
    ///     The free bytes.
    /// </returns>
    /// <exception cref="IOException">The path doesn't start with a drive letter, such as a folder on a network share.</exception>
    public static long FreeBytes(string directory)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(directory))!;

        // Refuse a share or a \\?\ path here, because DriveInfo throws ArgumentException with a message about its own parameter.
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new IOException(SR.FormatOutputNotOnDrive(directory));
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }

    /// <summary>
    ///     Adds up the sizes of the files in <paramref name="path"/>.
    /// </summary>
    /// <param name="path">A file or a folder, which doesn't have to exist.</param>
    /// <returns>
    ///     The bytes of the file or of every file in the folder, 0 for a missing path.
    /// </returns>
    public static long SizeOf(string path)
    {
        FileInfo file = new(path);

        if (file.Exists)
        {
            return file.Length;
        }

        DirectoryInfo directory = new(path);

        if (!directory.Exists)
        {
            return 0;
        }

        long bytes = 0;

        foreach (var entry in directory.EnumerateFiles("*", FolderTree.Entries))
        {
            bytes += entry.Length;
        }

        return bytes;
    }

    /// <summary>
    ///     Gets the path of the manifest of <paramref name="engineDirectory"/>, next to the folder.
    /// </summary>
    /// <param name="engineDirectory">The engine folder, without a separator at its end.</param>
    /// <returns>
    ///     The path of the manifest.
    /// </returns>
    public static string ManifestPath(string engineDirectory)
    {
        return $"{engineDirectory}.manifest";
    }

    /// <summary>
    ///     Removes what <paramref name="space"/> lets go until the drive of <paramref name="outputDirectory"/> has room for the
    ///     install, the build and <see cref="MarginBytes"/>.
    /// </summary>
    /// <remarks>
    ///     The leftovers go first whenever the drive is short, because nothing needs them. Then the installers in <see cref="DownloadsFolder"/>
    ///     go, oldest first. The finished engines in <see cref="EnginesFolder"/> go last, oldest marker first, because an engine
    ///     is the biggest download to get back. A path of <see cref="InstallSpace.KeepPaths"/> and an engine folder
    ///     without a marker stay. Every installer and engine stays when their sizes don't add up to enough room, because
    ///     removing them wouldn't make the install fit.
    /// </remarks>
    /// <param name="outputDirectory">The output folder with the downloads and engines folders.</param>
    /// <param name="space">The space the install needs and the paths it keeps and lets go.</param>
    /// <param name="freeBytes">The source of the free space, given a folder on the drive.</param>
    /// <param name="output">The writer of the step line for each removed path.</param>
    /// <exception cref="IOException">
    ///     The drive doesn't have enough space with every other installer and finished engine gone. The message has the drive,
    ///     the free space and the needed space. Or a removal fails.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">A removal fails on a file the user can't delete.</exception>
    public static void MakeRoom(string outputDirectory, InstallSpace space, Func<string, long> freeBytes, TextWriter output)
    {
        long needed = space.MissingBytes + space.BuildBytes + MarginBytes;
        long free = freeBytes(outputDirectory);

        if (free >= needed)
        {
            return;
        }

        // Delete the leftovers before the sum check, because a failed run left them and nothing needs them.
        foreach (string leftover in space.Leftovers)
        {
            if (Path.Exists(leftover))
            {
                DeletePath(leftover);
                output.WriteLine(SR.FormatSpaceRemoved(leftover));
            }
        }

        free = freeBytes(outputDirectory);

        if (free >= needed)
        {
            return;
        }

        HashSet<string> keep = new(space.KeepPaths.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        string root = Path.GetFullPath(outputDirectory);

        List<string> removable =
        [
            .. Installers(Path.Combine(root, DownloadsFolder), keep),
            .. Engines(Path.Combine(root, EnginesFolder), keep),
        ];

        // Check the sum before the 1st delete, because a drive too small even without them shouldn't lose them.
        if (free + removable.Sum(SizeOf) < needed)
        {
            throw NewSpaceLow(outputDirectory, free, needed);
        }

        foreach (string path in removable)
        {
            DeletePath(path);
            output.WriteLine(SR.FormatSpaceRemoved(path));

            free = freeBytes(outputDirectory);

            if (free >= needed)
            {
                return;
            }
        }

        throw NewSpaceLow(outputDirectory, free, needed);
    }

    /// <summary>
    ///     Deletes an engine folder and its manifest, also when the folder has read-only files or folders.
    /// </summary>
    /// <param name="engineDirectory">The engine folder.</param>
    /// <exception cref="IOException">A file is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">A file is in use or the user can't delete it.</exception>
    public static void Delete(string engineDirectory)
    {
        // Clear read-only first, also on the marker and the engine folder, because File.Delete and Directory.Delete refuse those.
        FolderTree.ClearReadOnly(engineDirectory);

        // Delete the marker before the rest, so a delete that stops halfway leaves an unfinished folder and half an engine never passes as installed.
        File.Delete(Path.Combine(engineDirectory, EngineMarker.FileName));

        Directory.Delete(engineDirectory, recursive: true);
        File.Delete(ManifestPath(engineDirectory));
    }

    /// <summary>
    ///     Lists the files in <paramref name="downloadsDirectory"/> that may go, oldest first.
    /// </summary>
    /// <param name="downloadsDirectory">The downloads folder, which doesn't have to exist.</param>
    /// <param name="keep">The full paths that stay.</param>
    /// <returns>
    ///     The files, with partial downloads of other installers among them.
    /// </returns>
    private static IEnumerable<string> Installers(string downloadsDirectory, HashSet<string> keep)
    {
        if (!Directory.Exists(downloadsDirectory))
        {
            return [];
        }

        return new DirectoryInfo(downloadsDirectory)
            .EnumerateFiles()
            .Where(file => !keep.Contains(file.FullName))
            .OrderBy(static file => file.LastWriteTimeUtc)
            .Select(static file => file.FullName);
    }

    /// <summary>
    ///     Lists the finished engines in <paramref name="enginesDirectory"/> that may go, oldest marker first.
    /// </summary>
    /// <param name="enginesDirectory">The engines folder, which doesn't have to exist.</param>
    /// <param name="keep">The full paths that stay.</param>
    /// <returns>
    ///     The engine folders with a marker.
    /// </returns>
    private static IEnumerable<string> Engines(string enginesDirectory, HashSet<string> keep)
    {
        if (!Directory.Exists(enginesDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(enginesDirectory)
            .Where(directory => EngineMarker.Read(directory) is not null && !keep.Contains(Normalize(directory)))
            .OrderBy(EngineMarker.WrittenAt);
    }

    /// <summary>
    ///     Deletes a file or folder, also when it has read-only files or folders. An engine folder takes its manifest along.
    /// </summary>
    /// <param name="path">The file or folder.</param>
    private static void DeletePath(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        else if (EngineMarker.Read(path) is not null)
        {
            Delete(path);
        }
        else
        {
            FolderTree.Delete(path);
        }
    }

    /// <summary>
    ///     Makes the full path of <paramref name="path"/> without a separator at its end, so 2 spellings of a path compare equal.
    /// </summary>
    /// <param name="path">A file or folder.</param>
    /// <returns>
    ///     The full path.
    /// </returns>
    private static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    ///     Creates the exception of a drive that doesn't have enough space.
    /// </summary>
    /// <param name="outputDirectory">The output folder.</param>
    /// <param name="free">The free bytes.</param>
    /// <param name="needed">The needed bytes.</param>
    /// <returns>
    ///     The exception with the drive and both numbers in GiB.
    /// </returns>
    private static IOException NewSpaceLow(string outputDirectory, long free, long needed)
    {
        string drive = Path.GetPathRoot(Path.GetFullPath(outputDirectory))!;

        // Round the free space down and the needed space up, so the numbers never look like enough.
        return new IOException(SR.FormatSpaceLow(drive, free / BytesPerGib, (needed + BytesPerGib - 1) / BytesPerGib));
    }
}
