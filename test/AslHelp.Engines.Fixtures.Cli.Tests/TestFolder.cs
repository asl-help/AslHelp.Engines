using System;
using System.Formats.Tar;
using System.IO;
using ZstdSharp;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Creates a fresh temp folder for 1 test and the files and archives inside it.
/// </summary>
internal sealed class TestFolder
{
    private int _folderCount;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TestFolder"/> class and creates <see cref="Root"/>.
    /// </summary>
    public TestFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), $"AslHelp.Engines.Fixtures.Cli.Tests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Root);
    }

    /// <summary>
    ///     Gets the full path of the temp folder.
    /// </summary>
    public string Root { get; }

    /// <summary>
    ///     Creates a new numbered folder under <see cref="Root"/>.
    /// </summary>
    /// <returns>
    ///     The full path of the new folder.
    /// </returns>
    public string NewFolder()
    {
        string folder = Path.Combine(Root, $"folder{_folderCount++}");
        _ = Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>
    ///     Deletes <see cref="Root"/> and everything under it.
    /// </summary>
    public void Delete()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>
    ///     Writes <paramref name="bytes"/> to <paramref name="relativePath"/> under <paramref name="folder"/> and creates missing folders.
    /// </summary>
    /// <param name="folder">The folder the path is relative to.</param>
    /// <param name="relativePath">The file path, with '/' or the platform separator.</param>
    /// <param name="bytes">The file content.</param>
    public static void WriteBytes(string folder, string relativePath, byte[] bytes)
    {
        string path = Path.Combine(folder, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    ///     Creates a file entry called <paramref name="name"/> with <paramref name="bytes"/> as its data.
    /// </summary>
    /// <param name="name">The entry name, written as is.</param>
    /// <param name="bytes">The file content.</param>
    /// <returns>
    ///     The entry.
    /// </returns>
    public static PaxTarEntry NewFile(string name, byte[] bytes)
    {
        return new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(bytes) };
    }

    /// <summary>
    ///     Fills <paramref name="length"/> bytes with xorshift output, so zstd cannot shrink them and every run gets the same bytes.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>
    ///     The bytes.
    /// </returns>
    public static byte[] Noise(int length)
    {
        byte[] bytes = new byte[length];
        uint state = 2463534242;

        for (int i = 0; i < length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = (byte)(state & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    ///     Writes a <c>.tar.zst</c> archive at <paramref name="path"/> with the entries <paramref name="write"/> adds and no
    ///     content checksum, unchecked.
    /// </summary>
    /// <param name="path">The archive to create.</param>
    /// <param name="write">The callback that writes the entries.</param>
    public static void CraftArchive(string path, Action<TarWriter> write)
    {
        using FileStream file = new(path, FileMode.CreateNew);
        using CompressionStream compression = new(file, Archive.CompressionLevel);
        using TarWriter writer = new(compression, TarEntryFormat.Pax);

        write(writer);
    }
}
