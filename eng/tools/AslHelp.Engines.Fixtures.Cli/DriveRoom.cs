using System;
using System.IO;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Makes room on the drive of 1 output folder for the steps of 1 install, through <see cref="DiskSpace.MakeRoom"/>.
/// </summary>
/// <remarks>
///     A toolchain calls <see cref="MakeFor"/> before each step that writes, like a download and an extraction, once that step
///     knows its size. So no size has to be written down ahead of time and every toolchain frees the drive the same way.
/// </remarks>
/// <param name="outputDirectory">The output folder with the downloads and engines folders.</param>
/// <param name="freeBytes">The source of the free space, given a folder on the drive.</param>
/// <param name="output">The writer of the step line for each removed path.</param>
internal sealed class DriveRoom(
    string outputDirectory,
    Func<string, long> freeBytes,
    TextWriter output)
{
    /// <summary>
    ///     Removes what <paramref name="space"/> lets go until the drive has room for it.
    /// </summary>
    /// <param name="space">The space the next step needs and the paths it keeps and lets go.</param>
    /// <exception cref="IOException">A removal fails or the drive doesn't have room even without the other installers and finished engines.</exception>
    /// <exception cref="UnauthorizedAccessException">A removal fails on a file the user can't delete.</exception>
    public void MakeFor(InstallSpace space)
    {
        DiskSpace.MakeRoom(outputDirectory, space, freeBytes, output);
    }
}
