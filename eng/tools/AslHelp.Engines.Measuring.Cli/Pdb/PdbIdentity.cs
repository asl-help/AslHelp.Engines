using AslHelp.Engines.Measuring.Cli.Pdb.Errors;
using System;

namespace AslHelp.Engines.Measuring.Cli.Pdb;

/// <summary>
///     Describes the build of a PDB.
/// </summary>
/// <param name="Guid">The GUID of the info stream.</param>
/// <param name="Age">The age of the DBI stream, which is the age the executable records.</param>
internal sealed record PdbIdentity(
    Guid Guid,
    int Age)
{
    /// <summary>
    ///     Checks that the PDB and an executable come from the same build.
    /// </summary>
    /// <param name="executable">The identity in the executable.</param>
    /// <returns>
    ///     A failure with both GUIDs and both ages when they differ.
    /// </returns>
    public Result Check(ExecutableIdentity executable)
    {
        return Guid == executable.Guid && Age == executable.Age
            ? Result.Ok()
            : PdbError.IdentityMismatch(Guid, Age, executable.Guid, executable.Age);
    }
}
