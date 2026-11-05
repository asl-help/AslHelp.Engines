using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Represents the remote store that cached results are downloaded from and uploaded to.
/// </summary>
/// <remarks>
///     A store can also throw exceptions of its own, such as for a refused login or a passed time limit.
/// </remarks>
internal interface ICacheStore : IDisposable
{
    /// <summary>
    ///     Downloads <paramref name="path"/> to <paramref name="destinationFile"/> if it exists.
    /// </summary>
    /// <param name="path">The relative, '/' separated path in the store, such as <c>players/abc.tar.zst</c>.</param>
    /// <param name="destinationFile">The file to write. An existing file is replaced and a missing folder is created.</param>
    /// <param name="progress">
    ///     The receiver of the bytes on disk and the length of the file after each chunk or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="cancellationToken">The token that stops the download.</param>
    /// <returns>
    ///     <see langword="true"/> when the file was downloaded, <see langword="false"/> when the store has no file at <paramref name="path"/>.
    /// </returns>
    /// <exception cref="IOException">The file cannot be read from the store or written to disk.</exception>
    Task<bool> TryDownloadAsync(string path, string destinationFile, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    ///     Uploads <paramref name="sourceFile"/> to <paramref name="path"/>.
    /// </summary>
    /// <param name="sourceFile">The file to upload.</param>
    /// <param name="path">The relative, '/' separated path in the store, such as <c>players/abc.tar.zst</c>. An existing file is replaced.</param>
    /// <param name="progress">
    ///     The receiver of the bytes sent and the length of the file after each chunk or <see langword="null"/> for no reports.
    /// </param>
    /// <param name="cancellationToken">The token that stops the upload.</param>
    /// <returns>
    ///     A task that completes once the store has the file.
    /// </returns>
    /// <exception cref="FileNotFoundException"><paramref name="sourceFile"/> does not exist.</exception>
    Task UploadAsync(string sourceFile, string path, IProgress<TransferProgress>? progress, CancellationToken cancellationToken);
}
