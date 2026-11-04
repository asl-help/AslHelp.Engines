using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AslHelp.Engines.Fixtures;

/// <summary>
///     Builds a <see cref="CacheKey"/> from inputs that each have a name.
/// </summary>
/// <remarks>
///     <para>
///         The same inputs give the same key on any machine and the order of the add calls does not matter.
///         The kind and each input name use lowercase ascii letters, digits and '-' only.
///     </para>
///     <para>
///         The hash is a sha256 over this byte layout, where <c>text(s)</c> is the UTF-8 byte count of
///         <c>s</c> as a 4-byte little-endian integer followed by the bytes:
///         <list type="number">
///             <item><c>text(kind)</c>.</item>
///             <item>
///                 For each entry in ordinal order of its name, a 1-byte tag (1 for a value, 2 for a folder),
///                 <c>text(name)</c> and the payload.
///             </item>
///             <item>A value payload is <c>text(value)</c>.</item>
///             <item>
///                 A folder payload is the file count as a 4-byte little-endian integer, then for each file in
///                 ordinal order of the UTF-16 code units of its relative path <c>text(relativePath)</c>, the file
///                 length as an 8-byte little-endian integer and the file bytes.
///             </item>
///         </list>
///     </para>
///     <para>
///         Keys are shared between machines, so a change to this layout gives every cached result a new key.
///     </para>
///     <para>
///         <see cref="Inputs"/> lists what the last <see cref="Build"/> hashed, so a record of the inputs cannot drift
///         from the key. A folder input lists the sha256 over its payload, read in the same pass as the key.
///     </para>
/// </remarks>
public sealed class CacheKeyBuilder
{
    private const byte DirectoryTag = 2;
    private const int ReadBufferSize = 81920;
    private const byte ValueTag = 1;

    // Throw on a lone surrogate, because the default encoder maps every one to U+FFFD and 2 values would share a key.
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _kind;
    private readonly SortedDictionary<string, (bool IsDirectory, string Payload)> _entries = [with(StringComparer.Ordinal)];

    private ReadOnlyCollection<CacheKeyInput>? _inputs;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CacheKeyBuilder"/> class for keys of 1 kind.
    /// </summary>
    /// <param name="kind">The kind of result, in lowercase ascii letters, digits and '-'.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="kind"/> is empty or has a character other than lowercase ascii letters, digits and '-'.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public CacheKeyBuilder(string kind)
    {
        CacheKey.ThrowIfNotName(kind);

        _kind = kind;
    }

    /// <summary>
    ///     Gets the inputs the last <see cref="Build"/> hashed, in the ordinal name order it hashed them.
    /// </summary>
    /// <remarks>
    ///     A value input lists its text. A folder input lists the sha256 over the folder payload as 64 lowercase hex
    ///     chars, the bytes the key layout has after the input name.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="Build"/> has not run yet or an input was added after the last <see cref="Build"/>.
    /// </exception>
    public IReadOnlyList<CacheKeyInput> Inputs => _inputs ?? throw new InvalidOperationException(SR.NoInputs);

    /// <summary>
    ///     Adds a text input.
    /// </summary>
    /// <param name="name">The name of the input, in lowercase ascii letters, digits and '-'.</param>
    /// <param name="value">The text to hash.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="name"/> is empty, has a character other than lowercase ascii letters, digits and '-' or was
    ///     added already.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> has a lone UTF-16 surrogate.</exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public CacheKeyBuilder Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfNotNewName(name);

        try
        {
            _ = _strictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException(SR.LoneSurrogateValue, nameof(value), ex);
        }

        _entries.Add(name, (IsDirectory: false, value));
        _inputs = null;
        return this;
    }

    /// <summary>
    ///     Adds every file under a folder, recursively.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each file adds its relative path with '/' separators and its raw bytes, in ordinal order of the
    ///         relative path, so the order of the file system enumeration does not matter. Files are streamed and
    ///         never read whole into memory. An empty folder is allowed.
    ///     </para>
    ///     <para>
    ///         Every file under the folder is hashed, including <c>.git/</c>, <c>obj/</c> and editor files. Pass a
    ///         folder whose files git tracks, not the root of a checkout. Line endings are hashed as they are on
    ///         disk, so the checkout needs <c>.gitattributes</c> eol rules that fix them.
    ///     </para>
    ///     <para>
    ///         Symbolic links and junctions to folders are followed, so a link out of the folder adds the files it
    ///         points to and a link back into the folder throws once the path gets too long.
    ///     </para>
    /// </remarks>
    /// <param name="name">The name of the input, in lowercase ascii letters, digits and '-'.</param>
    /// <param name="path">The folder to hash. A relative path is resolved against the current folder now.</param>
    /// <returns>
    ///     This builder.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="name"/> is empty, has a character other than lowercase ascii letters, digits and '-' or was
    ///     added already.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/> or <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="path"/> is not an existing folder.</exception>
    public CacheKeyBuilder AddDirectory(string name, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfNotNewName(name);

        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(SR.Format(SR.FolderNotFound, path));
        }

        _entries.Add(name, (IsDirectory: true, fullPath));
        _inputs = null;
        return this;
    }

    /// <summary>
    ///     Computes the key from the kind and the inputs added so far.
    /// </summary>
    /// <remarks>
    ///     Build keeps the added inputs, so more inputs can follow for another key. Build sets <see cref="Inputs"/> to the
    ///     inputs it hashed.
    /// </remarks>
    /// <returns>
    ///     The key, with a hash of 64 lowercase hex chars.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">A folder added with <see cref="AddDirectory"/> is missing at build time.</exception>
    /// <exception cref="IOException">
    ///     A relative file path has a lone UTF-16 surrogate, a file is open for writing in another process, a file
    ///     ends before its length or a path is too long for the runtime.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">A file or subfolder cannot be read.</exception>
    public CacheKey Build()
    {
        // Clear the last list first, so a failed build leaves no list of an older key.
        _inputs = null;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var folderHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        HashSink keySink = new(hash, null);
        List<CacheKeyInput> inputs = [with(_entries.Count)];

        AppendText(keySink, _kind);
        foreach (var entry in _entries)
        {
            keySink.Append([entry.Value.IsDirectory ? DirectoryTag : ValueTag]);
            AppendText(keySink, entry.Key);

            if (entry.Value.IsDirectory)
            {
                // Feed the payload to both hashes, so the folder is read once.
                AppendDirectory(new(hash, folderHash), entry.Value.Payload);
                inputs.Add(new(entry.Key, Convert.ToHexStringLower(folderHash.GetHashAndReset())));
            }
            else
            {
                AppendText(keySink, entry.Value.Payload);
                inputs.Add(new(entry.Key, entry.Value.Payload));
            }
        }

        CacheKey key = new(_kind, Convert.ToHexStringLower(hash.GetHashAndReset()));
        _inputs = inputs.AsReadOnly();

        return key;
    }

    private void ThrowIfNotNewName(string name)
    {
        CacheKey.ThrowIfNotName(name);

        if (_entries.ContainsKey(name))
        {
            throw new ArgumentException(SR.Format(SR.DuplicateInputName, name), nameof(name));
        }
    }

    private static void AppendDirectory(HashSink sink, string root)
    {
        if (root[^1] != Path.DirectorySeparatorChar)
        {
            root += Path.DirectorySeparatorChar;
        }

        List<string> relativePaths = [];
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            relativePaths.Add(file[root.Length..].Replace(Path.DirectorySeparatorChar, '/'));
        }

        relativePaths.Sort(StringComparer.Ordinal);

        AppendUInt32(sink, (uint)relativePaths.Count);

        byte[] buffer = new byte[ReadBufferSize];
        foreach (string relativePath in relativePaths)
        {
            AppendRelativePath(sink, root, relativePath);
            AppendFile(sink, Path.Combine(root, relativePath), buffer);
        }
    }

    private static void AppendRelativePath(HashSink sink, string root, string relativePath)
    {
        try
        {
            AppendText(sink, relativePath);
        }
        catch (EncoderFallbackException ex)
        {
            throw new IOException(SR.Format(SR.LoneSurrogateFileName, root), ex);
        }
    }

    private static void AppendFile(HashSink sink, string path, byte[] buffer)
    {
        // Turn off the FileStream buffer, because every read goes through the shared buffer already.
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.SequentialScan);

        long remaining = stream.Length;
        AppendUInt64(sink, (ulong)remaining);

        // Read exactly the hashed length, so the length prefix still bounds the bytes if the file grows.
        while (remaining > 0)
        {
            int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new IOException(SR.Format(SR.FileShorterThanLength, path, remaining));
            }

            sink.Append(buffer.AsSpan(0, read));
            remaining -= read;
        }
    }

    private static void AppendText(HashSink sink, string text)
    {
        byte[] bytes = _strictUtf8.GetBytes(text);

        AppendUInt32(sink, (uint)bytes.Length);
        sink.Append(bytes);
    }

    private static void AppendUInt32(HashSink sink, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);

        sink.Append(bytes);
    }

    private static void AppendUInt64(HashSink sink, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);

        sink.Append(bytes);
    }

    /// <summary>
    ///     Appends the same bytes to the key hash and, while a folder is read, to the hash of its payload.
    /// </summary>
    /// <param name="key">The hash of the key.</param>
    /// <param name="folder">The hash of the folder payload or <see langword="null"/> outside a folder.</param>
    private readonly struct HashSink(
        IncrementalHash key,
        IncrementalHash? folder)
    {
        /// <summary>
        ///     Appends <paramref name="bytes"/> to every hash of the sink.
        /// </summary>
        /// <param name="bytes">The bytes to hash.</param>
        public void Append(ReadOnlySpan<byte> bytes)
        {
            key.AppendData(bytes);
            folder?.AppendData(bytes);
        }
    }
}
