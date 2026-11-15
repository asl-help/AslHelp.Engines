using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Makes room on the drive of the engine folders by removing finished engines when the drive is short.
/// </summary>
internal static class EngineSpace
{
    /// <summary>
    ///     Represents the free space a build needs on top of the install: 20 GiB of cook scratch and 20 GiB of margin.
    /// </summary>
    public const long ScratchBytes = 40L << 30;

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
            throw new IOException(SR.FormatEngineNotOnDrive(directory));
        }

        return new DriveInfo(root).AvailableFreeSpace;
    }

    /// <summary>
    ///     Adds up the sizes of the files in <paramref name="engineDirectory"/>.
    /// </summary>
    /// <param name="engineDirectory">The engine folder, which doesn't have to exist.</param>
    /// <returns>
    ///     The bytes of its files, 0 for a missing folder.
    /// </returns>
    public static long SizeOf(string engineDirectory)
    {
        DirectoryInfo engine = new(engineDirectory);

        if (!engine.Exists)
        {
            return 0;
        }

        long bytes = 0;

        foreach (var file in engine.EnumerateFiles("*", FolderTree.Entries))
        {
            bytes += file.Length;
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
    ///     Removes stale scratch folders and then finished engines, oldest marker first, until the drive has room for
    ///     <paramref name="installBytes"/> and <see cref="ScratchBytes"/>.
    /// </summary>
    /// <remarks>
    ///     A folder without a marker is an unfinished install and stays. The scratch folders go first and always, because a
    ///     failed build left them. Every engine stays when the engine sizes don't add up to enough room, because removing
    ///     them wouldn't make the install fit and an engine costs a download to get back.
    /// </remarks>
    /// <param name="enginesDirectory">The folder with the engine folders.</param>
    /// <param name="keepDirectory">The engine folder that stays.</param>
    /// <param name="installBytes">The bytes the install still needs, 0 for an engine that is installed.</param>
    /// <param name="scratchDirectories">The scratch folders of earlier builds that may go. A missing one counts as 0 bytes.</param>
    /// <param name="freeBytes">The source of the free space, given a folder on the drive.</param>
    /// <param name="output">The writer of the step line for each removed folder.</param>
    /// <exception cref="IOException">
    ///     The drive doesn't have enough space with every other finished engine gone. The message has the drive, the free
    ///     space and the needed space. Or a removal fails.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">A removal fails on a file the user can't delete.</exception>
    public static void MakeRoom(
        string enginesDirectory,
        string keepDirectory,
        long installBytes,
        IReadOnlyList<string> scratchDirectories,
        Func<string, long> freeBytes,
        TextWriter output)
    {
        long needed = installBytes + ScratchBytes;
        long free = freeBytes(enginesDirectory);

        if (free >= needed)
        {
            return;
        }

        // Trim the path end, because GetFullPath keeps it and the enumerated folders don't have one.
        string keep = Path.TrimEndingDirectorySeparator(Path.GetFullPath(keepDirectory));

        // Delete the scratch folders before the sum check, because a failed build left them and nothing needs them.
        foreach (string scratch in scratchDirectories)
        {
            if (Directory.Exists(scratch))
            {
                FolderTree.Delete(scratch);
                output.WriteLine(SR.FormatEngineRemoved(scratch));
            }
        }

        free = freeBytes(enginesDirectory);

        if (free >= needed)
        {
            return;
        }

        List<string> engines = [];
        long removable = 0;

        foreach (string directory in Directory.EnumerateDirectories(enginesDirectory))
        {
            if (EngineMarker.Read(directory) is not null && !Path.GetFullPath(directory).Equals(keep, StringComparison.OrdinalIgnoreCase))
            {
                engines.Add(directory);
                removable += SizeOf(directory);
            }
        }

        // Check the sum before the 1st delete, because a drive too small even without those engines shouldn't lose them.
        if (free + removable < needed)
        {
            throw NewSpaceLow(enginesDirectory, free, needed);
        }

        foreach (string folder in engines.OrderBy(EngineMarker.WrittenAt))
        {
            Delete(folder);
            output.WriteLine(SR.FormatEngineRemoved(folder));

            free = freeBytes(enginesDirectory);

            if (free >= needed)
            {
                return;
            }
        }

        throw NewSpaceLow(enginesDirectory, free, needed);
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
    ///     Creates the exception of a drive that doesn't have enough space.
    /// </summary>
    /// <param name="enginesDirectory">The folder with the engine folders.</param>
    /// <param name="free">The free bytes.</param>
    /// <param name="needed">The needed bytes.</param>
    /// <returns>
    ///     The exception with the drive and both numbers in GiB.
    /// </returns>
    private static IOException NewSpaceLow(string enginesDirectory, long free, long needed)
    {
        string drive = Path.GetPathRoot(Path.GetFullPath(enginesDirectory))!;

        // Round the free space down and the needed space up, so the numbers never look like enough.
        return new IOException(SR.FormatEngineSpaceLow(drive, free / BytesPerGib, (needed + BytesPerGib - 1) / BytesPerGib));
    }
}
