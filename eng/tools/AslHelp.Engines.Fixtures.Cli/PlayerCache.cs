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
    ///     The key covers the toolchain name, every file of the project folder and the inputs the toolchain adds with
    ///     <see cref="IToolchain.AddKeyInputs"/>, so a change to any of them builds the player again and a new build of
    ///     this tool does not.
    /// </remarks>
    /// <param name="toolchain">The toolchain.</param>
    /// <param name="version">The version of the toolchain, as typed.</param>
    /// <param name="projectDirectory">The project folder of the player.</param>
    /// <returns>
    ///     The player key, with a key of kind <c>player</c>, the version as the toolchain pins it and the inputs the key hashed.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="toolchain"/> adds an input called <c>toolchain</c> or <c>project</c> or pins a version that is not a
    ///     valid folder name. It is thrown before the project files are read.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="projectDirectory"/> does not exist.</exception>
    /// <exception cref="IOException">A file of the project folder cannot be read.</exception>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="toolchain"/> has no release of <paramref name="version"/>. It is thrown before the project files are read.
    ///     It is also thrown when the version the toolchain returns is not the input <c>version</c> it adds.
    /// </exception>
    public static PlayerKey KeyOf(IToolchain toolchain, string version, string projectDirectory)
    {
        CacheKeyBuilder builder = new(Kind);
        _ = builder
            .Add("toolchain", toolchain.Name)
            .AddDirectory("project", projectDirectory);

        string pinned = toolchain.AddKeyInputs(builder, version);

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
    ///     An existing player folder sends no request. A hit needs the folder or the archive alone and never reads a manifest.
    /// </remarks>
    /// <param name="key">The key of the player.</param>
    /// <param name="cancellationToken">The token that stops the download.</param>
    /// <returns>
    ///     The full path of the player folder or <see langword="null"/> when neither the output folder nor the store has the player.
    /// </returns>
    /// <exception cref="IOException">The download or the unpack failed.</exception>
    /// <exception cref="InvalidDataException">
    ///     The archive in the store cannot be unpacked, because it is damaged, cut short or has a refused entry.
    ///     The downloaded copy is deleted and the message has the store path in it.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string?> TryRestoreAsync(PlayerKey key, CancellationToken cancellationToken)
    {
        string player = PlayerPath(key);

        if (Directory.Exists(player))
        {
            return player;
        }

        if (store is null)
        {
            return null;
        }

        string remote = RemotePath(key, ArchiveExtension);
        string archive = ArchivePath(key);

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
            // Delete the download quietly, so a failed delete does not hide the store path and the disk gets its space back.
            TempPaths.DeleteFileQuietly(archive);

            // Put the store path in the message, because a fresh download that fails to unpack means the store copy is bad.
            // Keep the reason of the failed unpack as the cause and not the archive message, whose fix points at the deleted download.
            throw new InvalidDataException(SR.FormatStoreCopyDamaged(remote), exception.InnerException ?? exception);
        }

        // Delete quietly, because the player is complete and a scanner that has the archive open must not fail the run.
        TempPaths.DeleteFileQuietly(archive);

        return player;
    }

    /// <summary>
    ///     Writes the manifest into the install of a new build, moves the install to the player folder of
    ///     <paramref name="key"/> and uploads its archive and then the manifest to the store.
    /// </summary>
    /// <remarks>
    ///     The manifest is written before the move, so the player folder that later runs hit never lacks it. It is written
    ///     before the pack too, so the archive has it. The archive goes up first, so a manifest in the store always has
    ///     its archive. The archive is deleted only after both uploads, so a failed upload keeps it for an upload by hand.
    /// </remarks>
    /// <param name="key">The key of the player.</param>
    /// <param name="installDirectory">The install that the player was built and checked in.</param>
    /// <param name="cancellationToken">The token that stops the upload.</param>
    /// <returns>
    ///     The full path of the player folder.
    /// </returns>
    /// <exception cref="IOException">The move, the manifest write, the pack or an upload failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string> StoreAsync(PlayerKey key, string installDirectory, CancellationToken cancellationToken)
    {
        string player = PlayerPath(key);
        string installManifest = Path.Combine(installDirectory, ManifestFileName);

        await output.WriteLineAsync(SR.FormatCacheWritingManifest(installManifest).AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteManifestAsync(key, installManifest, cancellationToken).ConfigureAwait(false);

        await output.WriteLineAsync(SR.FormatCacheMoving(installDirectory, player).AsMemory(), cancellationToken).ConfigureAwait(false);
        Directory.Move(installDirectory, player);

        string manifest = Path.Combine(player, ManifestFileName);

        if (store is null)
        {
            return player;
        }

        // Check the token first, because the pack is synchronous and cannot stop halfway.
        cancellationToken.ThrowIfCancellationRequested();

        string archive = ArchivePath(key);
        string remoteArchive = RemotePath(key, ArchiveExtension);
        string remoteManifest = RemotePath(key, ManifestExtension);

        await output.WriteLineAsync(SR.FormatCachePacking(player, archive).AsMemory(), cancellationToken).ConfigureAwait(false);
        Archive.Pack(player, archive);

        await output.WriteLineAsync(SR.FormatCacheUploading(archive, remoteArchive).AsMemory(), cancellationToken).ConfigureAwait(false);
        await bars.ShowAsync(remoteArchive, progress => store.UploadAsync(archive, remoteArchive, progress, cancellationToken)).ConfigureAwait(false);

        // Send the manifest without a bar, because a file of about 1 KB goes up at once.
        await output.WriteLineAsync(SR.FormatCacheUploading(manifest, remoteManifest).AsMemory(), cancellationToken).ConfigureAwait(false);
        await store.UploadAsync(manifest, remoteManifest, progress: null, cancellationToken).ConfigureAwait(false);

        // Delete quietly, because the player is complete and a scanner that has the archive open must not fail the run.
        TempPaths.DeleteFileQuietly(archive);

        return player;
    }

    /// <summary>
    ///     Writes <paramref name="key"/> as indented camelCase JSON to <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    ///     A stopped write leaves a cut manifest in the install, which the next build of that install writes again.
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
