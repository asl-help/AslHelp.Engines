using AslHelp.Engines.Fixtures.Cli.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Copies the DLLs that a player executable loads and the plain base image doesn't have into the folder of the executable.
/// </summary>
internal static class UnrealRuntimes
{
    /// <summary>
    ///     Copies every DLL that <paramref name="executablePath"/> loads through its static imports from
    ///     <paramref name="poolDirectory"/> into the folder of the executable.
    /// </summary>
    /// <remarks>
    ///     The import tables of the executable and of every DLL it loads from the folder or the pool are read, breadth first. An
    ///     API set, such as <c>api-ms-win-crt-runtime-l1-1-0.dll</c>, comes from Windows, also when the pool has it. A DLL in the
    ///     folder stays as it is. A DLL in the pool or its subfolders is copied under the importer's spelling. A DLL in
    ///     <paramref name="systemDirectory"/> comes from Windows. Every DLL is mapped before the 1st copy, so a missing DLL leaves
    ///     the folder as it was. Each DLL is copied to a <c>.partial</c> file first and then moved to its name. Delay imports
    ///     are not read, because the package step puts those DLLs into the player folder.
    /// </remarks>
    /// <param name="executablePath">The path of the executable.</param>
    /// <param name="poolDirectory">The folder with the runtime DLLs, searched with its subfolders. A missing folder counts as empty.</param>
    /// <param name="systemDirectory">The system folder of the architecture of the executable, such as <c>C:\Windows\System32</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">
    ///     The executable does not exist or a DLL is in neither the folder, the pool nor Windows. The message lists every missing
    ///     DLL.
    /// </exception>
    /// <exception cref="IOException">A copy or a move fails.</exception>
    /// <exception cref="InvalidDataException">
    ///     The executable or a DLL it loads is not a PE file or the pool has 2 different files of a DLL it loads.
    /// </exception>
    public static void Add(string executablePath, string poolDirectory, string systemDirectory)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(poolDirectory);
        ArgumentNullException.ThrowIfNull(systemDirectory);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(UnrealSR.FormatExecutableNotFound(executablePath), executablePath);
        }

        string binariesDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!;

        var pool = IndexPool(poolDirectory);
        Dictionary<string, string> copies = [with(StringComparer.OrdinalIgnoreCase)];
        SortedSet<string> missing = [with(StringComparer.OrdinalIgnoreCase)];
        HashSet<string> seen = [with(StringComparer.OrdinalIgnoreCase), Path.GetFileName(executablePath)];
        Queue<string> pending = new();
        pending.Enqueue(executablePath);

        while (pending.TryDequeue(out string? path))
        {
            foreach (string import in PeImports.Read(path))
            {
                // Skip an import read before. Skip an API set before the pool lookup, because engine pools ship API sets as
                // app-local DLLs that Windows ignores.
                if (!seen.Add(import) || IsApiSet(import))
                {
                    continue;
                }

                string local = Path.Combine(binariesDirectory, import);

                if (File.Exists(local))
                {
                    pending.Enqueue(local);
                }
                else if (pool[import].FirstOrDefault() is { } source)
                {
                    // Compare the bytes only for a DLL that is loaded, because engine pools have many DLLs under 1 name.
                    if (pool[import].Skip(1).FirstOrDefault(path => !HaveSameBytes(source, path)) is { } other)
                    {
                        throw new InvalidDataException(UnrealSR.FormatRuntimeConflict(import, source, other));
                    }

                    // Copy under the importer's spelling, because the pool can spell the name with other casing.
                    copies.Add(import, source);
                    pending.Enqueue(source);
                }
                else if (!File.Exists(Path.Combine(systemDirectory, import)))
                {
                    _ = missing.Add(import);
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                UnrealSR.FormatRuntimeMissing(executablePath, string.Join(", ", missing), poolDirectory));
        }

        foreach (var (name, source) in copies)
        {
            string target = Path.Combine(binariesDirectory, name);
            string partialPath = target + TempPaths.PartialSuffix;

            File.Copy(source, partialPath, overwrite: true);

            TempPaths.MovePartial(partialPath, target);
        }
    }

    /// <summary>
    ///     Maps the file name of every DLL in <paramref name="poolDirectory"/> and its subfolders to its paths.
    /// </summary>
    /// <param name="poolDirectory">The folder with the runtime DLLs.</param>
    /// <returns>
    ///     The paths by file name without case, empty for a missing folder.
    /// </returns>
    private static ILookup<string, string> IndexPool(string poolDirectory)
    {
        var paths = Directory.Exists(poolDirectory)
            ? Directory.EnumerateFiles(poolDirectory, "*.dll", SearchOption.AllDirectories)
            : [];

        return paths.ToLookup(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Checks whether 2 files have the same bytes.
    /// </summary>
    /// <param name="path">The path of the 1st file.</param>
    /// <param name="otherPath">The path of the 2nd file.</param>
    /// <returns>
    ///     <see langword="true"/> if the files have the same length and bytes.
    /// </returns>
    private static bool HaveSameBytes(string path, string otherPath)
    {
        // Read both whole, because runtime DLLs are a few MB at most and a duplicate name is rare.
        return new FileInfo(path).Length == new FileInfo(otherPath).Length
            && File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(otherPath));
    }

    /// <summary>
    ///     Checks whether <paramref name="fileName"/> is an API set, which the Windows loader maps to a system DLL itself.
    /// </summary>
    /// <param name="fileName">The DLL name of an import.</param>
    /// <returns>
    ///     <see langword="true"/> if the name starts with <c>api-ms-</c> or <c>ext-ms-</c>.
    /// </returns>
    private static bool IsApiSet(string fileName)
    {
        return fileName.StartsWith("api-ms-", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("ext-ms-", StringComparison.OrdinalIgnoreCase);
    }
}
