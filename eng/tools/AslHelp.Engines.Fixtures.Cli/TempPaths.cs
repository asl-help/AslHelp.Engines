using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Provides the suffix of a file or folder that is still being written, the move to its final name and the deletes
///     that clean up after a step.
/// </summary>
internal static class TempPaths
{
    /// <summary>
    ///     Represents the suffix of a file or folder that is still being written, which moves to its final name once complete.
    /// </summary>
    public const string PartialSuffix = ".partial";

    /// <summary>
    ///     Moves the complete file at <paramref name="partialPath"/> over <paramref name="path"/>.
    /// </summary>
    /// <param name="partialPath">The complete file under its partial name.</param>
    /// <param name="path">The file to replace.</param>
    /// <exception cref="IOException">
    ///     The move failed. The message has both paths and the file stays at <paramref name="partialPath"/>.
    /// </exception>
    public static void MovePartial(string partialPath, string path)
    {
        try
        {
            File.Move(partialPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(SR.FormatPartialNotMoved(partialPath, path), exception);
        }
    }

    /// <summary>
    ///     Deletes the file at <paramref name="path"/> and ignores a failure, so the exception that started the cleanup
    ///     reaches the caller and a file that another process has open stays for a later run.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    public static void DeleteFileQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leave the file for the next run, which replaces it.
        }
    }

    /// <summary>
    ///     Deletes the folder at <paramref name="path"/> with everything in it and ignores a failure, so the exception
    ///     that started the cleanup reaches the caller and a folder that another process has open stays for a later run.
    /// </summary>
    /// <param name="path">The folder to delete.</param>
    public static void DeleteFolderQuietly(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leave the folder for the next run, which deletes it first.
        }
    }
}
