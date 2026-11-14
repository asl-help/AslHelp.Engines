using AslHelp.Engines.Measuring.Cli.Files.Errors;
using System;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Files;

/// <summary>
///     Opens PE files and reads their headers.
/// </summary>
internal static class PeFile
{
    /// <summary>
    ///     Opens the file at <paramref name="path"/> and reads its headers.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The reader with the headers read or a failure with <paramref name="path"/> when the file does not open or is not a
    ///     PE file. The caller disposes the reader.
    /// </returns>
    public static Result<PEReader> Open(string path)
    {
        PEReader? reader = null;

        try
        {
            reader = new PEReader(File.OpenRead(path));

            // Check the optional header, because PEReader reads a file without the PE signature as an object file with no error.
            if (reader.PEHeaders.PEHeader is null)
            {
                return FileError.NotPe(path);
            }

            var opened = reader;
            reader = null;

            return opened;
        }
        catch (BadImageFormatException)
        {
            return FileError.NotPe(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FileError.Unreadable(path, exception.Message);
        }
        finally
        {
            reader?.Dispose();
        }
    }
}
