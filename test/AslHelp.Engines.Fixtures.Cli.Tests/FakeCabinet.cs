using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Writes cabinet files for tests, either names only by hand or a real one through the system <c>makecab.exe</c>.
/// </summary>
internal static class FakeCabinet
{
    private const int FileEntrySize = 16;
    private const int FilesOffset = HeaderSize + FolderSize;
    private const int FolderSize = 8;
    private const int HeaderSize = 36;

    // Run makecab.exe by its full path, so a tool of the same name on PATH never runs.
    private static readonly string _makecabPath = Path.Combine(Environment.SystemDirectory, "makecab.exe");

    /// <summary>
    ///     Gets the bytes of a cabinet that lists <paramref name="names"/> and has no file data.
    /// </summary>
    /// <remarks>
    ///     The header, 1 folder and 1 file entry per name are written, so a name reader finds every name. <c>expand.exe</c>
    ///     refuses the result, because it has no data blocks.
    /// </remarks>
    /// <param name="names">The file names in cabinet order.</param>
    /// <returns>
    ///     The cabinet bytes.
    /// </returns>
    public static byte[] Names(params string[] names)
    {
        int length = FilesOffset;

        foreach (string name in names)
        {
            length += FileEntrySize + Encoding.ASCII.GetByteCount(name) + 1;
        }

        byte[] cabinet = new byte[length];
        var bytes = cabinet.AsSpan();

        "MSCF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], FilesOffset);
        bytes[24] = 3;
        bytes[25] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[28..], (ushort)names.Length);

        int offset = FilesOffset;

        foreach (string name in names)
        {
            offset += FileEntrySize;
            offset += Encoding.ASCII.GetBytes(name, bytes[offset..]) + 1;
        }

        return cabinet;
    }

    /// <summary>
    ///     Builds a real cabinet at <paramref name="cabinetPath"/> that stores each of <paramref name="sourceFiles"/> under its file name.
    /// </summary>
    /// <remarks>
    ///     Pass 2 files or more, as the DirectX cabinets have, because <c>expand.exe</c> writes the file of a 1-file
    ///     cabinet under the cabinet's own name.
    /// </remarks>
    /// <param name="cabinetPath">The cabinet to create. Its folder must exist.</param>
    /// <param name="sourceFiles">The files to store, in cabinet order.</param>
    /// <exception cref="InvalidOperationException"><c>makecab.exe</c> exits with a code other than 0.</exception>
    public static void Make(string cabinetPath, params string[] sourceFiles)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(cabinetPath))!;
        string directivePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(cabinetPath) + ".ddf");

        // Write the report and the inf to NUL, so makecab leaves only the cabinet next to the directive file.
        StringBuilder directives = new();
        _ = directives.Append(".Set CabinetNameTemplate=").AppendLine(Path.GetFileName(cabinetPath));
        _ = directives.Append(".Set DiskDirectoryTemplate=").AppendLine(directory);
        _ = directives.AppendLine(".Set InfFileName=NUL");
        _ = directives.AppendLine(".Set RptFileName=NUL");

        foreach (string sourceFile in sourceFiles)
        {
            _ = directives.Append('"').Append(sourceFile).AppendLine("\"");
        }

        File.WriteAllText(directivePath, directives.ToString());

        ProcessStartInfo info = new(_makecabPath, ["/F", directivePath])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            WorkingDirectory = directory,
        };

        using var process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        File.Delete(directivePath);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"makecab.exe exited with code {process.ExitCode}. Its output was:{Environment.NewLine}{output}");
        }
    }

    /// <summary>
    ///     Builds a real cabinet that stores each of <paramref name="files"/> under its name.
    /// </summary>
    /// <remarks>
    ///     The source files go to a new folder of <paramref name="folder"/>, because <c>makecab.exe</c> reads a
    ///     semicolon in a directive file as the start of a comment. Pass 2 files or more for a cabinet that
    ///     <c>expand.exe</c> unpacks under the name of each file.
    /// </remarks>
    /// <param name="folder">The temp folder of the test.</param>
    /// <param name="files">The name and the content of each file, in cabinet order.</param>
    /// <returns>
    ///     The cabinet bytes.
    /// </returns>
    /// <exception cref="InvalidOperationException"><c>makecab.exe</c> exits with a code other than 0.</exception>
    public static byte[] Make(TestFolder folder, params (string Name, byte[] Bytes)[] files)
    {
        string sources = folder.NewFolder();
        string[] sourceFiles = new string[files.Length];

        for (int i = 0; i < files.Length; i++)
        {
            sourceFiles[i] = Path.Combine(sources, files[i].Name);
            File.WriteAllBytes(sourceFiles[i], files[i].Bytes);
        }

        string cabinetPath = Path.Combine(folder.NewFolder(), "files.cab");
        Make(cabinetPath, sourceFiles);

        return File.ReadAllBytes(cabinetPath);
    }
}
