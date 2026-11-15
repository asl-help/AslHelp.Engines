using AslHelp.Engines.Measuring.Cli.Files;
using AslHelp.Engines.Measuring.Cli.Pdb.Errors;
using System;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Describes the PDB that an executable was linked with.
/// </summary>
/// <param name="Guid">The GUID of the PDB.</param>
/// <param name="Age">The age of the PDB.</param>
/// <param name="PdbName">The path of the PDB as the linker wrote it.</param>
internal sealed record ExecutableIdentity(
    Guid Guid,
    int Age,
    string PdbName)
{
    /// <summary>
    ///     Reads the CodeView debug entry of the file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>
    ///     The identity or a failure with <paramref name="path"/> when the file doesn't open, is not a PE file,
    ///     doesn't have the entry or the entry doesn't read.
    /// </returns>
    public static Result<ExecutableIdentity> Read(string path)
    {
        if (!PeFile.Open(path).TryUnwrap(out var reader, out var error))
        {
            return error;
        }

        using (reader)
        {
            return ReadEntry(reader, path);
        }
    }

    private static Result<ExecutableIdentity> ReadEntry(PEReader reader, string path)
    {
        try
        {
            foreach (var entry in reader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    var data = reader.ReadCodeViewDebugDirectoryData(entry);

                    return new ExecutableIdentity(data.Guid, data.Age, data.Path);
                }
            }

            return PdbError.ExecutableNoCodeView(path);
        }
        catch (BadImageFormatException exception)
        {
            return PdbError.ExecutableCodeViewInvalid(path, exception.Message);
        }
    }
}
