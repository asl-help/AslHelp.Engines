using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Enumerates and deletes folder trees, also ones with read-only files or folders in them.
/// </summary>
internal static class FolderTree
{
    /// <summary>
    ///     Represents the options that enumerate every file and folder of a tree, with hidden and system ones, but don't follow links.
    /// </summary>
    /// <remarks>
    ///     Links are skipped, so an attribute change, a size or a delete stays inside the tree. Hidden and system files are
    ///     enumerated, because a read-only one stops a delete as well.
    /// </remarks>
    public static readonly EnumerationOptions Entries = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
    };

    /// <summary>
    ///     Clears the read-only attribute of <paramref name="directory"/> and of every file and folder in it.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <exception cref="IOException"><paramref name="directory"/> does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The user can't change the attributes of an entry.</exception>
    public static void ClearReadOnly(string directory)
    {
        DirectoryInfo root = new(directory);
        root.Attributes &= ~FileAttributes.ReadOnly;

        foreach (var entry in root.EnumerateFileSystemInfos("*", Entries))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }

    /// <summary>
    ///     Deletes <paramref name="directory"/> with everything in it, also when it has read-only files or folders.
    /// </summary>
    /// <remarks>
    ///     Clears read-only first, because <see cref="Directory.Delete(string, bool)"/> refuses a read-only entry.
    /// </remarks>
    /// <param name="directory">The folder.</param>
    /// <exception cref="IOException"><paramref name="directory"/> does not exist or a file is in use.</exception>
    /// <exception cref="UnauthorizedAccessException">A file is in use or the user can't delete it.</exception>
    public static void Delete(string directory)
    {
        ClearReadOnly(directory);
        Directory.Delete(directory, recursive: true);
    }
}
