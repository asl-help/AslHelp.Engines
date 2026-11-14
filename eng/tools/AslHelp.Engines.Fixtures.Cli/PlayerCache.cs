using AslHelp.Engines.Fixtures.Cli.Formats;
using AslHelp.Engines.Fixtures.Cli.Transfer;
using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli;

/// <summary>
///     Restores built players from the output folder or a cache store and stores new builds in both.
/// </summary>
/// <remarks>
///     A player sits in <c>&lt;output&gt;/&lt;toolchain&gt;-&lt;version&gt;-&lt;hash&gt;</c> on disk and in
///     <c>players/&lt;toolchain&gt;/&lt;version&gt;/&lt;hash&gt;.tar.zst</c> in the store, with its manifest as
///     <c>manifest.json</c> in the folder and as <c>&lt;hash&gt;.json</c> beside the archive. The version is the one the
///     toolchain pins and the hash is <see cref="CacheKey.ShortHash"/>.
/// </remarks>
/// <param name="store">The store to download and upload archives or <see langword="null"/> to keep every player local.</param>
/// <param name="outputDirectory">The folder for the player folders and their archives.</param>
/// <param name="output">The writer for the steps.</param>
/// <param name="bars">The bars that show the archive download and upload. They draw on the console of <paramref name="output"/>.</param>
internal sealed class PlayerCache(
    ICacheStore? store,
    string outputDirectory,
    TextWriter output,
    TransferBars bars)
{
    private const string ArchiveExtension = ".tar.zst";
    private const string Kind = "player";
    private const string ManifestExtension = ".json";
    private const string ManifestFileName = "manifest.json";
    private const string RemoteFolder = "players";
    private const string VersionInput = "version";

    // Escape only what JSON needs, because a person reads the manifest and '\u003E' hides the '>' of a value.
    private static readonly JsonTypeInfo<PlayerKey> _manifestTypeInfo = new JsonSerializerOptions(PlayerKeyContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }.GetTypeInfo<PlayerKey>();

    /// <summary>
    ///     Builds the key of the player that <paramref name="toolchain"/> builds at <paramref name="version"/>.
    /// </summary>
    /// <remarks>
    ///     The key covers the toolchain name and the inputs the toolchain adds with <see cref="IToolchain.AddKeyInputs"/>,
    ///     the project folder among them, so a change to any of them builds the player again and a new build of this tool
    ///     does not.
    /// </remarks>
    /// <param name="toolchain">The toolchain.</param>
    /// <param name="version">The player id from <see cref="IToolchain.ListPlayers"/>.</param>
    /// <param name="projectDirectory">The project folder of the player.</param>
    /// <returns>
    ///     The player key, with a key of kind <c>player</c>, the version as the toolchain pins it and the inputs the key hashed.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="toolchain"/> adds an input called <c>toolchain</c> or pins a version that is not a valid folder name.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="projectDirectory"/> does not exist.</exception>
    /// <exception cref="IOException">A file of the project folder cannot be read.</exception>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="toolchain"/> doesn't have a release of <paramref name="version"/>. It is also thrown when the version
    ///     the toolchain returns is not the input <c>version</c> it adds.
    /// </exception>
    public static PlayerKey KeyOf(IToolchain toolchain, string version, string projectDirectory)
    {
        CacheKeyBuilder builder = new(Kind);
        _ = builder.Add("toolchain", toolchain.Name);

        string pinned = toolchain.AddKeyInputs(builder, version, projectDirectory);

        // Check the pinned version first, because it becomes a folder name and a store path segment.
        if (!IsFolderName(pinned))
        {
            throw new ArgumentException(SR.FormatVersionNotFolderName(pinned, toolchain.Name), nameof(toolchain));
        }

        var key = builder.Build();

        // Compare the returned version with the hashed one, because the paths use the 1st and the manifest lists the 2nd.
        if (!builder.Inputs.Contains(new CacheKeyInput(VersionInput, pinned)))
        {
            throw new InvalidOperationException(SR.FormatVersionNotHashed(toolchain.Name, pinned));
        }

        return new(key, toolchain.Name, pinned, builder.Inputs);
    }

    /// <summary>
    ///     Gets the player of <paramref name="key"/> from the output folder or downloads and unpacks it from the store.
    /// </summary>
    /// <remarks>
    ///     An existing player folder sends no request, unless the archive of a failed upload sits beside it. That archive and the
    ///     manifest go up then. A hit needs the folder or the archive alone and never reads a manifest.
    ///     A store copy that is damaged, cut short or has a refused entry counts as a miss with a step line.
    ///     The downloaded copy is deleted.
    /// </remarks>
    /// <param name="key">The key of the player.</param>
    /// <param name="cancellationToken">The token that stops the download.</param>
    /// <returns>
    ///     The full path of the player folder or <see langword="null"/> when neither the output folder nor the store has a
    ///     good copy of the player.
    /// </returns>
    /// <exception cref="IOException">
    ///     The download, the unpack or the upload of a leftover archive failed. A failed unpack deletes the downloaded copy.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string?> TryRestoreAsync(PlayerKey key, CancellationToken cancellationToken)
    {
        string player = PlayerPath(key);
        string archive = ArchivePath(key);

        if (Directory.Exists(player))
        {
            // Upload an archive beside the player, because it stays there only after a failed upload of an earlier run.
            if (store is not null && File.Exists(archive))
            {
                await UploadAsync(store, key, player, archive, cancellationToken).ConfigureAwait(false);
            }

            return player;
        }

        if (store is null)
        {
            return null;
        }

        string remote = RemotePath(key, ArchiveExtension);

        await output.WriteLineAsync(SR.FormatCacheDownloading(remote).AsMemory(), cancellationToken).ConfigureAwait(false);

        bool downloaded = await bars.ShowAsync(remote, progress => store.TryDownloadAsync(remote, archive, progress, cancellationToken))
            .ConfigureAwait(false);

        if (!downloaded)
        {
            await output.WriteLineAsync(SR.FormatCacheMiss(remote).AsMemory(), cancellationToken).ConfigureAwait(false);
            return null;
        }

        // Check the token first, because the unpack is synchronous and cannot stop halfway.
        cancellationToken.ThrowIfCancellationRequested();

        await output.WriteLineAsync(SR.FormatCacheUnpacking(archive, player).AsMemory(), cancellationToken).ConfigureAwait(false);

        try
        {
            Archive.Unpack(archive, player);
        }
        catch (InvalidDataException exception)
        {
            // Delete the download quietly, so a failed delete does not stop the rebuild and the disk gets its space back.
            TempPaths.DeleteFileQuietly(archive);

            // Treat the damaged store copy as a miss, because a fresh download that fails to unpack means the store copy is bad
            // and the upload after the rebuild replaces it. Print the reason of the failed unpack and not the archive message,
            // whose fix points at the deleted download.
            string reason = (exception.InnerException ?? exception).Message;
            await output.WriteLineAsync(SR.FormatCacheDamaged(remote, reason).AsMemory(), cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch
        {
            // Delete the download on any other failure too, because it takes tens of GB and the next run downloads it again.
            TempPaths.DeleteFileQuietly(archive);

            throw;
        }

        // Delete quietly, because the player is complete and a scanner that has the archive open must not fail the run.
        TempPaths.DeleteFileQuietly(archive);

        return player;
    }

    /// <summary>
    ///     Writes the manifest into the folder of a new build, moves that folder to the player folder of
    ///     <paramref name="key"/> and uploads its archive and then the manifest to the store.
    /// </summary>
    /// <remarks>
    ///     The manifest is written before the move, so the player folder that later runs hit never lacks it. It is written
    ///     before the pack too, so the archive has it. The archive goes up first, so a manifest in the store always has
    ///     its archive. The archive is deleted only after both uploads, so a failed upload keeps it for the next run, which uploads it
    ///     on its hit of the player folder.
    /// </remarks>
    /// <param name="key">The key of the player.</param>
    /// <param name="playerDirectory">The checked folder of the player from <see cref="IToolchain.BuildAsync"/>.</param>
    /// <param name="cancellationToken">The token that stops the upload.</param>
    /// <returns>
    ///     The full path of the player folder.
    /// </returns>
    /// <exception cref="IOException">The move, the manifest write, the pack or an upload failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string> StoreAsync(PlayerKey key, string playerDirectory, CancellationToken cancellationToken)
    {
        string player = PlayerPath(key);
        string builtManifest = Path.Combine(playerDirectory, ManifestFileName);

        await output.WriteLineAsync(SR.FormatCacheWritingManifest(builtManifest).AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteManifestAsync(key, builtManifest, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(SR.FormatCacheMoving(playerDirectory, player).AsMemory(), cancellationToken).ConfigureAwait(false);
        Directory.Move(playerDirectory, player);

        if (store is null)
        {
            return player;
        }

        // Check the token first, because the pack is synchronous and cannot stop halfway.
        cancellationToken.ThrowIfCancellationRequested();

        string archive = ArchivePath(key);

        await output.WriteLineAsync(SR.FormatCachePacking(player, archive).AsMemory(), cancellationToken).ConfigureAwait(false);
        Archive.Pack(player, archive);

        await UploadAsync(store, key, player, archive, cancellationToken).ConfigureAwait(false);

        return player;
    }

    /// <summary>
    ///     Uploads the archive of <paramref name="key"/> and then its manifest to the store and deletes the archive.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="key">The key of the player.</param>
    /// <param name="player">The player folder with the manifest.</param>
    /// <param name="archive">The packed player.</param>
    /// <param name="cancellationToken">The token that stops the upload.</param>
    /// <returns>
    ///     A task that completes once the store has both files.
    /// </returns>
    private async Task UploadAsync(ICacheStore store, PlayerKey key, string player, string archive, CancellationToken cancellationToken)
    {
        string manifest = Path.Combine(player, ManifestFileName);
        string remoteArchive = RemotePath(key, ArchiveExtension);
        string remoteManifest = RemotePath(key, ManifestExtension);

        await output.WriteLineAsync(SR.FormatCacheUploading(archive, remoteArchive).AsMemory(), cancellationToken).ConfigureAwait(false);
        await bars.ShowAsync(remoteArchive, progress => store.UploadAsync(archive, remoteArchive, progress, cancellationToken)).ConfigureAwait(false);

        // Send the manifest without a bar, because a file of about 1 KB goes up at once.
        await output.WriteLineAsync(SR.FormatCacheUploading(manifest, remoteManifest).AsMemory(), cancellationToken).ConfigureAwait(false);
        await store.UploadAsync(manifest, remoteManifest, progress: null, cancellationToken).ConfigureAwait(false);

        // Delete quietly, because the player is complete and a scanner that has the archive open must not fail the run.
        TempPaths.DeleteFileQuietly(archive);
    }

    /// <summary>
    ///     Writes <paramref name="key"/> as indented camelCase JSON to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    ///     A stopped write leaves a cut manifest in the built folder, which the next build writes again.
    /// </remarks>
    /// <param name="key">The key of the player.</param>
    /// <param name="path">The manifest file to create or replace.</param>
    /// <param name="cancellationToken">The token that stops the write.</param>
    /// <returns>
    ///     A task that completes once the manifest is written.
    /// </returns>
    private static async Task WriteManifestAsync(PlayerKey key, string path, CancellationToken cancellationToken)
    {
        var stream = File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, key, _manifestTypeInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Checks whether <paramref name="value"/> can be 1 folder name.
    /// </summary>
    /// <param name="value">The name to check.</param>
    /// <returns>
    ///     <see langword="true"/> when a folder can be called <paramref name="value"/>, so it can be a path segment on disk
    ///     and in the store.
    /// </returns>
    private static bool IsFolderName(string value)
    {
        return value is not ("" or "." or "..") && value.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>
    ///     Gets the full path of the player folder of <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key of the player.</param>
    /// <returns>
    ///     <c>&lt;output&gt;/&lt;toolchain&gt;-&lt;version&gt;-&lt;hash&gt;</c> as a full path.
    /// </returns>
    private string PlayerPath(PlayerKey key)
    {
        return Path.GetFullPath(Path.Combine(outputDirectory, $"{key.Toolchain}-{key.Version}-{key.Key.ShortHash}"));
    }

    /// <summary>
    ///     Gets the full path of the local archive of <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key of the player.</param>
    /// <returns>
    ///     <c>&lt;output&gt;/&lt;toolchain&gt;-&lt;version&gt;-&lt;hash&gt;.tar.zst</c> as a full path.
    /// </returns>
    private string ArchivePath(PlayerKey key)
    {
        return PlayerPath(key) + ArchiveExtension;
    }

    /// <summary>
    ///     Gets the path of a file of <paramref name="key"/> in the store.
    /// </summary>
    /// <param name="key">The key of the player.</param>
    /// <param name="extension">The extension of the file, <c>.tar.zst</c> for the archive or <c>.json</c> for the manifest.</param>
    /// <returns>
    ///     <c>players/&lt;toolchain&gt;/&lt;version&gt;/&lt;hash&gt;</c> followed by <paramref name="extension"/>.
    /// </returns>
    private static string RemotePath(PlayerKey key, string extension)
    {
        return $"{RemoteFolder}/{key.Toolchain}/{key.Version}/{key.Key.ShortHash}{extension}";
    }
}
