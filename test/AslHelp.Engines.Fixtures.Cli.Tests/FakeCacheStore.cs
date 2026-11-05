using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

/// <summary>
///     Stores files in memory by path and records every call in order, with no network.
/// </summary>
internal sealed class FakeCacheStore : ICacheStore
{
    private readonly List<FileStream> _heldDownloads = [];

    /// <summary>
    ///     Gets the stored files by path, such as <c>players/abc.tar.zst</c>.
    /// </summary>
    public Dictionary<string, byte[]> Files { get; } = [];

    /// <summary>
    ///     Gets the calls in order, such as <c>Download players/abc.tar.zst</c>.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    ///     Gets the call that throws an <see cref="IOException"/> or <see langword="null"/> for none. <c>Download</c> or
    ///     <c>Upload</c> fails every call of that method and a full line such as <c>Upload players/abc.json</c> fails 1 path.
    /// </summary>
    public string? FailingCall { get; init; }

    /// <summary>
    ///     Gets a value indicating whether each download stays open for reading without delete share until <see cref="Dispose"/>.
    /// </summary>
    public bool HoldDownloads { get; init; }

    /// <summary>
    ///     Gets the progress that every download of a stored file and every upload reports once or <see langword="null"/> for none.
    /// </summary>
    public TransferProgress? Progress { get; init; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="Dispose"/> ran.
    /// </summary>
    public bool Disposed { get; private set; }

    /// <inheritdoc/>
    public async Task<bool> TryDownloadAsync(
        string path,
        string destinationFile,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        Record("Download", path);

        if (!Files.TryGetValue(path, out byte[]? bytes))
        {
            return false;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationFile))!);
        await File.WriteAllBytesAsync(destinationFile, bytes, cancellationToken);
        ReportProgress(progress);

        if (HoldDownloads)
        {
            _heldDownloads.Add(new FileStream(destinationFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task UploadAsync(string sourceFile, string path, IProgress<TransferProgress>? progress, CancellationToken cancellationToken)
    {
        Record("Upload", path);
        Files[path] = await File.ReadAllBytesAsync(sourceFile, cancellationToken);
        ReportProgress(progress);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var download in _heldDownloads)
        {
            download.Dispose();
        }

        Disposed = true;
    }

    /// <summary>
    ///     Adds the call to <see cref="Calls"/> and throws if it is <see cref="FailingCall"/>.
    /// </summary>
    /// <param name="call">The call, <c>Download</c> or <c>Upload</c>.</param>
    /// <param name="path">The path the call reads or writes.</param>
    /// <exception cref="IOException">The call or its full line is <see cref="FailingCall"/>.</exception>
    private void Record(string call, string path)
    {
        string line = $"{call} {path}";
        Calls.Add(line);

        if (string.Equals(call, FailingCall, StringComparison.Ordinal) || string.Equals(line, FailingCall, StringComparison.Ordinal))
        {
            throw new IOException($"{call} failed in the fake store.");
        }
    }

    /// <summary>
    ///     Reports <see cref="Progress"/> to <paramref name="progress"/> when both are set.
    /// </summary>
    /// <param name="progress">The receiver the caller passed or <see langword="null"/>.</param>
    private void ReportProgress(IProgress<TransferProgress>? progress)
    {
        if (Progress is { } value)
        {
            progress?.Report(value);
        }
    }
}
