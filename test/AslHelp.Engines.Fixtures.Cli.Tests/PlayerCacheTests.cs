using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class PlayerCacheTests
{
    private TestFolder _folder = null!;
    private string _project = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _project = _folder.NewFolder();

        TestFolder.WriteBytes(_project, "file.bin", [1, 2, 3]);
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // The key kind is player, so player folders are called player-<16 hex chars>.
    [Test]
    public void KeyOf_AnyInputs_KindIsPlayer()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);

        Assert.That(key.Key.Kind, Is.EqualTo("player"));
    }

    // The same inputs give the same key, so a 2nd run finds the folder of the 1st run.
    [Test]
    public void KeyOf_SameInputs_GivesSameKey()
    {
        FakeToolchain toolchain = new("player");
        var first = PlayerCache.KeyOf(toolchain, "1.0", _project);

        var second = PlayerCache.KeyOf(toolchain, "1.0", _project);

        Assert.That(second.Key, Is.EqualTo(first.Key));
    }

    // The key equals 1 built by hand from the toolchain name, the project folder and the toolchain's inputs, so nothing about
    // the build of the tool is in it.
    [Test]
    public void KeyOf_FakeToolchain_HasOnlyTheNameTheProjectAndTheToolchainInputs()
    {
        var expected = new CacheKeyBuilder("player")
            .Add("toolchain", "player")
            .AddDirectory("project", _project)
            .Add("version", "1.0")
            .Add("extra", "")
            .Build();

        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);

        Assert.That(key.Key, Is.EqualTo(expected));
    }

    // 2 versions of 1 toolchain get different keys.
    [Test]
    public void KeyOf_ChangedVersion_ChangesKey()
    {
        FakeToolchain toolchain = new("player");

        Assert.That(PlayerCache.KeyOf(toolchain, "1.1", _project).Key, Is.Not.EqualTo(PlayerCache.KeyOf(toolchain, "1.0", _project).Key));
    }

    // A 1-byte change in a project file changes the key, so an edited player project is built again.
    [Test]
    public void KeyOf_ChangedProjectFile_ChangesKey()
    {
        FakeToolchain toolchain = new("player");
        var before = PlayerCache.KeyOf(toolchain, "1.0", _project);

        File.WriteAllBytes(Path.Combine(_project, "file.bin"), [1, 2, 4]);

        Assert.That(PlayerCache.KeyOf(toolchain, "1.0", _project).Key, Is.Not.EqualTo(before.Key));
    }

    // 2 toolchains with the same version and project folder get different keys.
    [Test]
    public void KeyOf_ChangedToolchainName_ChangesKey()
    {
        Assert.That(
            PlayerCache.KeyOf(new FakeToolchain("other"), "1.0", _project).Key,
            Is.Not.EqualTo(PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project).Key));
    }

    // An input the toolchain adds is in the key, so a toolchain change that shows in its inputs builds the player again.
    [Test]
    public void KeyOf_ChangedToolchainInput_ChangesKey()
    {
        FakeToolchain toolchain = new("player");
        var before = PlayerCache.KeyOf(toolchain, "1.0", _project);

        toolchain.ExtraKeyInput = "b";

        Assert.That(PlayerCache.KeyOf(toolchain, "1.0", _project).Key, Is.Not.EqualTo(before.Key));
    }

    // Inputs list what the key hashed in ordinal name order, with the project folder as a 64-hex content hash, so the manifest matches the key.
    [Test]
    public void KeyOf_ToolchainWithExtraInput_ListsInputsInHashedOrder()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player") { ExtraKeyInput = "x" }, "1.0", _project);

        string[] names = [.. key.Inputs.Select(input => input.Name)];
        string[] expected = ["extra", "project", "toolchain", "version"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Is.EqualTo(expected));
            Assert.That(key.Inputs[0].Value, Is.EqualTo("x"));
            Assert.That(key.Inputs[1].Value, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(key.Inputs[2].Value, Is.EqualTo("player"));
            Assert.That(key.Inputs[3].Value, Is.EqualTo("1.0"));
        }
    }

    // The version comes from the toolchain and not from the command line, so a typed spelling is replaced by the pinned one.
    [Test]
    public void KeyOf_ToolchainPinsVersion_ReturnsPinnedVersion()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player") { PinnedVersion = "1.0-PINNED" }, "1.0-pinned", _project);

        Assert.That(key.Version, Is.EqualTo("1.0-PINNED"));
    }

    // The paths use the returned version and the manifest lists the hashed one, so a toolchain that returns another throws.
    [Test]
    public void KeyOf_ReturnedVersionNotHashed_ThrowsInvalidOperationException()
    {
        FakeToolchain toolchain = new("player") { PinnedVersion = "1.0-PINNED", HashedVersion = "1.0-pinned" };

        Assert.That(
            () => PlayerCache.KeyOf(toolchain, "1.0-pinned", _project),
            Throws.InvalidOperationException.With.Message.Contains("'1.0-PINNED'").And.Message.Contains("'version'"));
    }

    // The folder and the store path use the version the toolchain pins, so the typed spelling appears in neither.
    [Test]
    public async Task StoreAsync_ToolchainPinsVersion_UsesPinnedVersionInFolderAndStorePath()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player") { PinnedVersion = "1.0-PINNED" }, "1.0-pinned", _project);
        FakeCacheStore store = new();
        string output = _folder.NewFolder();
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        string player = await cache.StoreAsync(key, NewInstall(), CancellationToken.None);

        string remote = $"players/player/1.0-PINNED/{key.Key.ShortHash}";
        string[] calls = [$"Upload {remote}.tar.zst", $"Upload {remote}.json"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(player, Is.EqualTo(Path.Combine(output, $"player-1.0-PINNED-{key.Key.ShortHash}")));
            Assert.That(store.Calls, Is.EqualTo(calls));
        }
    }

    // The pinned version becomes a folder name and a store path segment, so a separator or a dot name throws before any path exists.
    [TestCase("1/0")]
    [TestCase("1\\0")]
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("")]
    public void KeyOf_PinnedVersionNotAFolderName_ThrowsArgumentException(string pinned)
    {
        FakeToolchain toolchain = new("player") { PinnedVersion = pinned };

        Assert.That(
            () => PlayerCache.KeyOf(toolchain, "1.0", _project),
            Throws.ArgumentException.With.Message.Contains($"'{pinned}'"));
    }

    // The archive goes up before the manifest, so a store manifest always has its archive. The stored JSON is the player's manifest.json.
    [Test]
    public async Task StoreAsync_NewBuild_UploadsManifestBesideArchive()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        FakeCacheStore store = new();
        string output = _folder.NewFolder();
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        string player = await cache.StoreAsync(key, NewInstall(), CancellationToken.None);

        string[] calls = [$"Upload {RemotePath(key, ".tar.zst")}", $"Upload {RemotePath(key, ".json")}"];
        byte[] manifest = await File.ReadAllBytesAsync(Path.Combine(player, "manifest.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Calls, Is.EqualTo(calls));
            Assert.That(store.Files[RemotePath(key, ".json")], Is.EqualTo(manifest));
            AssertManifest(manifest, key);
            Assert.That(File.Exists(player + ".tar.zst"), Is.False);
        }
    }

    // The manifest is written before the pack, so a restore on another machine gets it with no 2nd download.
    [Test]
    public async Task StoreAsync_NewBuild_PacksTheManifestIntoTheArchive()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        FakeCacheStore store = new();
        PlayerCache cache = new(store, _folder.NewFolder(), TextWriter.Null, QuietBars());

        _ = await cache.StoreAsync(key, NewInstall(), CancellationToken.None);

        string archive = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        await File.WriteAllBytesAsync(archive, store.Files[RemotePath(key, ".tar.zst")]);
        string unpacked = Path.Combine(_folder.Root, "unpacked");
        Archive.Unpack(archive, unpacked);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(unpacked, "file.bin")), Is.True);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(unpacked, "manifest.json")), Is.EqualTo(store.Files[RemotePath(key, ".json")]));
        }
    }

    // With no store the player folder still gets its manifest.json and nothing is packed next to it.
    [Test]
    public async Task StoreAsync_NoStore_WritesManifestIntoThePlayerFolder()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string output = _folder.NewFolder();
        PlayerCache cache = new(null, output, TextWriter.Null, QuietBars());

        string player = await cache.StoreAsync(key, NewInstall(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(player, Is.EqualTo(PlayerPath(output, key)));
            AssertManifest(await File.ReadAllBytesAsync(Path.Combine(player, "manifest.json")), key);
            Assert.That(Directory.GetFiles(output), Is.Empty);
        }
    }

    // A person reads the manifest, so a '>' in an input value stays a '>' and is not escaped as '\u003E'.
    [Test]
    public async Task StoreAsync_InputWithAngleBracket_WritesItUnescaped()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player") { ExtraKeyInput = "a -> b" }, "1.0", _project);
        PlayerCache cache = new(null, _folder.NewFolder(), TextWriter.Null, QuietBars());

        string player = await cache.StoreAsync(key, NewInstall(), CancellationToken.None);

        Assert.That(await File.ReadAllTextAsync(Path.Combine(player, "manifest.json")), Does.Contain("\"a -> b\""));
    }

    // The manifest goes into the install before the move, so a stopped write leaves no player folder for a later run to hit.
    [Test]
    public async Task StoreAsync_CanceledDuringManifestWrite_LeavesNoPlayerFolder()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string output = _folder.NewFolder();
        string install = NewInstall();
        using CancellationTokenSource source = new();
        PlayerCache cache = new(null, output, new CancelingWriter("Writing ", source), QuietBars());

        _ = await Assert.CatchAsync<OperationCanceledException>(() => cache.StoreAsync(key, install, source.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(PlayerPath(output, key)), Is.False);
            Assert.That(Directory.Exists(install), Is.True);
        }
    }

    // The manifest upload comes after the archive upload, so on its failure the store has the archive and the local archive
    // stays for an upload by hand.
    [Test]
    public async Task StoreAsync_ManifestUploadFails_KeepsArchiveAndThrows()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        FakeCacheStore store = new() { FailingCall = $"Upload {RemotePath(key, ".json")}" };
        string output = _folder.NewFolder();
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        var exception = await Assert.ThrowsAsync<IOException>(() => cache.StoreAsync(key, NewInstall(), CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("Upload failed in the fake store."));
            Assert.That(store.Files.ContainsKey(RemotePath(key, ".tar.zst")), Is.True);
            Assert.That(File.Exists(PlayerPath(output, key) + ".tar.zst"), Is.True);
        }
    }

    // A hit needs the archive alone, so a store with no manifest still restores the player and nothing requests the JSON.
    [Test]
    public async Task TryRestoreAsync_RemoteArchiveWithoutManifest_Restores()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string remote = RemotePath(key, ".tar.zst");

        string packed = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(_project, packed);

        FakeCacheStore store = new();
        store.Files[remote] = await File.ReadAllBytesAsync(packed);
        string output = _folder.NewFolder();
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        string? player = await cache.TryRestoreAsync(key, CancellationToken.None);

        string[] calls = [$"Download {remote}"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(player, Is.EqualTo(PlayerPath(output, key)));
            Assert.That(File.Exists(Path.Combine(player!, "file.bin")), Is.True);
            Assert.That(store.Calls, Is.EqualTo(calls));
        }
    }

    // A restore draws the bar of the archive download between the download line and the unpack line.
    [Test]
    public async Task TryRestoreAsync_StoreReportsProgress_PrintsTheBarBeforeTheUnpack()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string remote = RemotePath(key, ".tar.zst");
        string packed = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(_project, packed);
        FakeCacheStore store = new() { Progress = new TransferProgress(1, 2) };
        store.Files[remote] = await File.ReadAllBytesAsync(packed);
        using StringWriter output = new();
        PlayerCache cache = new(store, _folder.NewFolder(), output, TransferBars.Create(output));

        _ = await cache.TryRestoreAsync(key, CancellationToken.None);

        string[] lines = output.ToString().Split(Environment.NewLine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines[0], Does.StartWith("Downloading "));
            Assert.That(lines[1], Is.EqualTo($"{remote}: 50%"));
            Assert.That(lines[2], Does.StartWith("Unpacking "));
        }
    }

    // A new build draws a bar for the archive upload only, because the manifest is about 1 KB.
    [Test]
    public async Task StoreAsync_StoreReportsProgress_PrintsTheBarOfTheArchiveOnly()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        FakeCacheStore store = new() { Progress = new TransferProgress(1, 2) };
        using StringWriter output = new();
        PlayerCache cache = new(store, _folder.NewFolder(), output, TransferBars.Create(output));

        _ = await cache.StoreAsync(key, NewInstall(), CancellationToken.None);

        string text = output.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain($"{RemotePath(key, ".tar.zst")}: 50%"));
            Assert.That(text, Does.Not.Contain($"{RemotePath(key, ".json")}: 50%"));
        }
    }

    // An existing player folder is a hit with or without manifest.json, so it sends no store call.
    [Test]
    public async Task TryRestoreAsync_LocalFolderWithoutManifest_Hits()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        FakeCacheStore store = new();
        string output = _folder.NewFolder();
        string expected = PlayerPath(output, key);
        _ = Directory.CreateDirectory(expected);
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        string? player = await cache.TryRestoreAsync(key, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(player, Is.EqualTo(expected));
            Assert.That(store.Calls, Is.Empty);
        }
    }

    // A store copy that is cut short fails with players/<toolchain>/<version>/<hash>.tar.zst and the word store in the message,
    // keeps the reason of the failed unpack as the cause, deletes the download and sends no other request to the store.
    [Test]
    public async Task TryRestoreAsync_StoreArchiveCutShort_ThrowsWithStorePathAndDeletesDownload()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string remote = RemotePath(key, ".tar.zst");

        string packed = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(_project, packed);
        byte[] bytes = await File.ReadAllBytesAsync(packed);

        FakeCacheStore store = new();
        store.Files[remote] = bytes[..(bytes.Length / 2)];

        string output = _folder.NewFolder();
        string player = PlayerPath(output, key);
        string archive = player + ".tar.zst";
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => cache.TryRestoreAsync(key, CancellationToken.None));

        string[] calls = [$"Download {remote}"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain(remote).And.Contain("store"));
            Assert.That(exception.InnerException, Is.TypeOf<EndOfStreamException>());
            Assert.That(File.Exists(archive), Is.False);
            Assert.That(Directory.Exists(player), Is.False);
            Assert.That(Directory.Exists(player + ".partial"), Is.False);
            Assert.That(store.Calls, Is.EqualTo(calls));
            Assert.That(store.Files.ContainsKey(remote), Is.True);
        }
    }

    // A download that another process keeps open without delete share stays on disk and the message still has the store path in it.
    [Test]
    public async Task TryRestoreAsync_StoreArchiveCutShortAndDownloadLocked_ThrowsWithStorePath()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string remote = RemotePath(key, ".tar.zst");

        string packed = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(_project, packed);
        byte[] bytes = await File.ReadAllBytesAsync(packed);

        using FakeCacheStore store = new() { HoldDownloads = true };
        store.Files[remote] = bytes[..(bytes.Length / 2)];

        string output = _folder.NewFolder();
        string archive = PlayerPath(output, key) + ".tar.zst";
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => cache.TryRestoreAsync(key, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain(remote));
            Assert.That(File.Exists(archive), Is.True);
        }
    }

    // A download that another process has open stays after a good restore, because the player is complete and only the
    // delete of the archive failed.
    [Test]
    public async Task TryRestoreAsync_DownloadOpenElsewhere_ReturnsPlayerAndKeepsArchive()
    {
        var key = PlayerCache.KeyOf(new FakeToolchain("player"), "1.0", _project);
        string remote = RemotePath(key, ".tar.zst");

        string packed = Path.Combine(_folder.NewFolder(), "file.tar.zst");
        Archive.Pack(_project, packed);

        using FakeCacheStore store = new() { HoldDownloads = true };
        store.Files[remote] = await File.ReadAllBytesAsync(packed);

        string output = _folder.NewFolder();
        string player = PlayerPath(output, key);
        PlayerCache cache = new(store, output, TextWriter.Null, QuietBars());

        string? restored = await cache.TryRestoreAsync(key, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored, Is.EqualTo(player));
            Assert.That(Directory.Exists(player), Is.True);
            Assert.That(File.Exists(player + ".tar.zst"), Is.True);
        }
    }

    private string NewInstall()
    {
        string install = _folder.NewFolder();
        TestFolder.WriteBytes(install, "file.bin", [1, 2, 3]);

        return install;
    }

    // Draw the bars on no output, because the tests check the step lines and the files.
    private static TransferBars QuietBars()
    {
        return TransferBars.Create(TextWriter.Null);
    }

    private static string PlayerPath(string output, PlayerKey key)
    {
        return Path.Combine(output, $"player-1.0-{key.Key.ShortHash}");
    }

    private static string RemotePath(PlayerKey key, string extension)
    {
        return $"players/player/1.0/{key.Key.ShortHash}{extension}";
    }

    private static void AssertManifest(byte[] manifest, PlayerKey key)
    {
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;

        List<string> properties = [];
        foreach (var property in root.EnumerateObject())
        {
            properties.Add(property.Name);
        }

        List<CacheKeyInput> inputs = [];
        foreach (var input in root.GetProperty("inputs").EnumerateArray())
        {
            inputs.Add(new(input.GetProperty("name").GetString()!, input.GetProperty("value").GetString()!));
        }

        string[] expected = ["key", "toolchain", "version", "inputs"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(properties, Is.EqualTo(expected));
            Assert.That(root.GetProperty("key").GetString(), Is.EqualTo($"player-{key.Key.Hash}"));
            Assert.That(root.GetProperty("toolchain").GetString(), Is.EqualTo("player"));
            Assert.That(root.GetProperty("version").GetString(), Is.EqualTo("1.0"));
            Assert.That(inputs, Is.EqualTo(key.Inputs));
        }
    }
}

/// <summary>
///     Cancels <paramref name="source"/> once a line that starts with <paramref name="prefix"/> is written, so a test
///     stops the step that follows that line.
/// </summary>
/// <param name="prefix">The start of the line that cancels.</param>
/// <param name="source">The source to cancel.</param>
file sealed class CancelingWriter(
    string prefix,
    CancellationTokenSource source) : StringWriter
{
    /// <inheritdoc/>
    public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Span.StartsWith(prefix, StringComparison.Ordinal))
        {
            await source.CancelAsync().ConfigureAwait(false);
        }
    }
}
