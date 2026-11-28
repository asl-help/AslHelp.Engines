using System.Collections.Generic;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Describes the space that the next step of an install still needs and the paths that the toolchain keeps or lets go.
/// </summary>
/// <param name="MissingBytes">The bytes the step still writes, like the rest of a download or the files of an extraction.</param>
/// <param name="BuildBytes">The bytes the build writes next to the install, such as cook scratch, 0 for none.</param>
/// <param name="KeepPaths">
///     The files in the downloads folder and the folders in the engines folder that this install needs, such as its installer or
///     its engine. Only those 2 folders lose files for space.
/// </param>
/// <param name="Leftovers">The files and folders that failed runs left, which go first whenever the drive is short.</param>
internal sealed record InstallSpace(
    long MissingBytes,
    long BuildBytes,
    IReadOnlyList<string> KeepPaths,
    IReadOnlyList<string> Leftovers);
