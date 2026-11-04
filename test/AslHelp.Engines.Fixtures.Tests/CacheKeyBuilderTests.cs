using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AslHelp.Engines.Fixtures.Tests;

public sealed class CacheKeyBuilderTests
{
    private int _folderCount;
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "AslHelp.Engines.Fixtures.Tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // 2 builders with the same kind, values and folder give equal keys.
    [Test]
    public void Build_SameInputs_GivesSameKey()
    {
        string folder = NewFolder();
        WriteText(folder, "a.txt", "a\n");

        var first = new CacheKeyBuilder("player").Add("version", "5.4").AddDirectory("source", folder).Build();
        var second = new CacheKeyBuilder("player").Add("version", "5.4").AddDirectory("source", folder).Build();

        Assert.That(second, Is.EqualTo(first));
    }

    // Entries are sorted by name before hashing, so the order of Add calls does not matter.
    [Test]
    public void Build_SameInputsAddedInAnotherOrder_GivesSameHash()
    {
        var first = new CacheKeyBuilder("player").Add("a", "1").Add("b", "2").Build();
        var second = new CacheKeyBuilder("player").Add("b", "2").Add("a", "1").Build();

        Assert.That(second.Hash, Is.EqualTo(first.Hash));
    }

    // A 1-character change to a value changes the hash.
    [Test]
    public void Build_ChangedValue_ChangesHash()
    {
        var first = new CacheKeyBuilder("player").Add("version", "5.4").Build();
        var second = new CacheKeyBuilder("player").Add("version", "5.5").Build();

        Assert.That(second.Hash, Is.Not.EqualTo(first.Hash));
    }

    // The name is hashed, so the same value under another name changes the hash.
    [Test]
    public void Build_ChangedName_ChangesHash()
    {
        var first = new CacheKeyBuilder("player").Add("version", "5.4").Build();
        var second = new CacheKeyBuilder("player").Add("branch", "5.4").Build();

        Assert.That(second.Hash, Is.Not.EqualTo(first.Hash));
    }

    // The kind is hashed too, so equal inputs under 2 kinds differ in kind and hash.
    [Test]
    public void Build_ChangedKind_ChangesKindAndHash()
    {
        var first = new CacheKeyBuilder("player").Add("version", "5.4").Build();
        var second = new CacheKeyBuilder("game").Add("version", "5.4").Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Kind, Is.EqualTo("game"));
            Assert.That(second.Hash, Is.Not.EqualTo(first.Hash));
        }
    }

    // Length prefixes keep the boundary between name and value, so ab|c and a|bc differ.
    [Test]
    public void Build_BoundaryBetweenNameAndValueMoved_GivesDifferentHashes()
    {
        var first = new CacheKeyBuilder("player").Add("ab", "c").Build();
        var second = new CacheKeyBuilder("player").Add("a", "bc").Build();

        Assert.That(second.Hash, Is.Not.EqualTo(first.Hash));
    }

    // The hash is a sha256 in lowercase hex, so ToString can cut it into a file-safe name.
    [Test]
    public void Build_AnyInputs_HashIs64LowercaseHexChars()
    {
        var key = new CacheKeyBuilder("player").Add("version", "5.4").Build();

        Assert.That(key.Hash, Does.Match("^[0-9a-f]{64}$"));
    }

    // Keys are shared between machines, so a change to the byte layout must show up here.
    [Test]
    public void Build_PinnedInputs_MatchesPinnedHash()
    {
        string folder = NewFolder();
        WriteText(folder, "b.txt", "b\n");
        WriteText(folder, "sub/a.txt", "a\n");

        var key = new CacheKeyBuilder("player")
            .Add("engine-version", "5.4.4")
            .AddDirectory("source", folder)
            .Build();

        Assert.That(key.Hash, Is.EqualTo("db658a691b8c56e8befb853eaeb1e21789110d7d4705250ad525e1b45a4f0943"));
    }

    // A 2nd Add with a used name throws and shows the name.
    [Test]
    public void Add_SameNameTwice_ThrowsArgumentException()
    {
        var builder = new CacheKeyBuilder("player").Add("version", "5.4");

        Assert.That(
            () => builder.Add("version", "5.5"),
            Throws.ArgumentException.With.Message.Contains("version"));
    }

    // AddDirectory rejects a name that Add used already.
    [Test]
    public void AddDirectory_NameUsedByAdd_ThrowsArgumentException()
    {
        string folder = NewFolder();
        var builder = new CacheKeyBuilder("player").Add("source", "5.4");

        Assert.That(() => builder.AddDirectory("source", folder), Throws.ArgumentException);
    }

    // A kind outside lowercase ascii letters, digits and '-' throws and lists the allowed characters.
    [TestCase("")]
    [TestCase("Player")]
    [TestCase("a 4")]
    [TestCase("a_4")]
    [TestCase("a.4")]
    [TestCase("ué")]
    public void Constructor_BadKind_ThrowsArgumentException(string kind)
    {
        Assert.That(
            () => new CacheKeyBuilder(kind),
            Throws.ArgumentException.With.Message.Contains("lowercase ascii letters, digits and '-'"));
    }

    // A name outside lowercase ascii letters, digits and '-' throws.
    [TestCase("")]
    [TestCase("Version")]
    [TestCase("a b")]
    [TestCase("a_b")]
    [TestCase("a/b")]
    public void Add_BadName_ThrowsArgumentException(string name)
    {
        CacheKeyBuilder builder = new("player");

        Assert.That(() => builder.Add(name, "1"), Throws.ArgumentException);
    }

    // Digits and '-' are allowed in a name.
    [TestCase("a")]
    [TestCase("engine-version")]
    [TestCase("a4")]
    [TestCase("4")]
    public void Add_GoodNames_Accepts(string name)
    {
        CacheKeyBuilder builder = new("player");

        Assert.That(() => builder.Add(name, "1"), Throws.Nothing);
    }

    // A 1-byte change in 1 file changes the hash.
    [Test]
    public void AddDirectory_ChangedFileContent_ChangesHash()
    {
        string first = NewFolder();
        WriteText(first, "a.txt", "a\n");
        string second = NewFolder();
        WriteText(second, "a.txt", "b\n");

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    // The relative path is hashed, so a rename or a move with the same bytes changes the hash.
    [TestCase("b.txt")]
    [TestCase("sub/a.txt")]
    public void AddDirectory_RenamedFile_ChangesHash(string newPath)
    {
        string first = NewFolder();
        WriteText(first, "a.txt", "a\n");
        string second = NewFolder();
        WriteText(second, newPath, "a\n");

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    // The file count and path are hashed, so an added empty file changes the hash.
    [Test]
    public void AddDirectory_AddedFile_ChangesHash()
    {
        string first = NewFolder();
        WriteText(first, "a.txt", "a\n");
        string second = NewFolder();
        WriteText(second, "a.txt", "a\n");
        WriteText(second, "b.txt", "");

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    // A folder with no files still adds its tag and a file count of 0, so it builds a key.
    [Test]
    public void AddDirectory_EmptyFolder_Builds()
    {
        string folder = NewFolder();

        Assert.That(HashOf(folder), Does.Match("^[0-9a-f]{64}$"));
    }

    // A 0-byte file still adds its path.
    [Test]
    public void AddDirectory_EmptyFolderAndFolderWithOneEmptyFile_GiveDifferentHashes()
    {
        string first = NewFolder();
        string second = NewFolder();
        WriteText(second, "a.txt", "");

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    // Every read chunk reaches the hash, so a change in the last byte past the 80 KB buffer changes it.
    [Test]
    public void AddDirectory_LastByteOfFileLargerThanReadBuffer_ChangesHash()
    {
        byte[] bytes = new byte[100001];

        string first = NewFolder();
        WriteBytes(first, "a.bin", bytes);

        bytes[^1] = 1;
        string second = NewFolder();
        WriteBytes(second, "a.bin", bytes);

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    // A missing folder throws at AddDirectory and shows the path.
    [Test]
    public void AddDirectory_MissingFolder_ThrowsDirectoryNotFoundException()
    {
        string folder = Path.Combine(_root, "missing");
        CacheKeyBuilder builder = new("player");

        Assert.That(
            () => builder.AddDirectory("source", folder),
            Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(folder));
    }

    // The hash matches a hash the test builds from the documented byte layout, with non-ascii paths and a file of 3
    // read chunks.
    [Test]
    public void Build_UnicodePathsAndLargeFile_MatchesDocumentedLayout()
    {
        byte[] large = new byte[200001];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = (byte)(i % 251);
        }

        string folder = NewFolder();
        WriteBytes(folder, "z.txt", [0x7A]);
        WriteBytes(folder, "ä/ü 1.txt", []);
        WriteBytes(folder, "\U0001F600.bin", large);

        var key = new CacheKeyBuilder("player")
            .Add("version", "é")
            .AddDirectory("source", folder)
            .Build();

        List<byte> layout = [];
        AppendText(layout, "player");
        layout.Add(2);
        AppendText(layout, "source");
        layout.AddRange(BitConverter.GetBytes(3u));
        AppendFile(layout, "z.txt", [0x7A]);
        AppendFile(layout, "ä/ü 1.txt", []);
        AppendFile(layout, "\U0001F600.bin", large);
        layout.Add(1);
        AppendText(layout, "version");
        AppendText(layout, "é");

        Assert.That(key.Hash, Is.EqualTo(Sha256Hex([.. layout])));
    }

    // Paths sort by UTF-16 code unit, so U+1F600 hashes before U+FF01 although its code point and UTF-8 bytes are larger.
    [Test]
    public void Build_PathsThatSortApartInUtf8_HashesInUtf16Order()
    {
        string folder = NewFolder();
        WriteBytes(folder, "\uFF01.txt", [1]);
        WriteBytes(folder, "\U0001F600.txt", [2]);

        var key = new CacheKeyBuilder("player").AddDirectory("source", folder).Build();

        List<byte> layout = [];
        AppendText(layout, "player");
        layout.Add(2);
        AppendText(layout, "source");
        layout.AddRange(BitConverter.GetBytes(2u));
        AppendFile(layout, "\U0001F600.txt", [2]);
        AppendFile(layout, "\uFF01.txt", [1]);

        Assert.That(key.Hash, Is.EqualTo(Sha256Hex([.. layout])));
    }

    // Inputs follow the ordinal name order that Build hashes, so a manifest written from them matches the key.
    [Test]
    public void Inputs_AfterBuild_ListsEntriesInHashedOrder()
    {
        var builder = new CacheKeyBuilder("player").Add("b", "2").Add("a", "1");
        _ = builder.Build();

        Assert.That(builder.Inputs, Is.EqualTo(new CacheKeyInput[] { new("a", "1"), new("b", "2") }));
    }

    // A folder input lists the sha256 of the payload Build hashes for it and the 2nd hash does not change the key layout.
    [Test]
    public void Inputs_DirectoryInput_ValueIsSha256OfFolderPayload()
    {
        byte[] large = new byte[200001];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = (byte)(i % 251);
        }

        string folder = NewFolder();
        WriteBytes(folder, "z.txt", [0x7A]);
        WriteBytes(folder, "ä/ü 1.txt", []);
        WriteBytes(folder, "\U0001F600.bin", large);

        var builder = new CacheKeyBuilder("player").AddDirectory("source", folder);
        var key = builder.Build();

        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(3u));
        AppendFile(payload, "z.txt", [0x7A]);
        AppendFile(payload, "ä/ü 1.txt", []);
        AppendFile(payload, "\U0001F600.bin", large);

        List<byte> layout = [];
        AppendText(layout, "player");
        layout.Add(2);
        AppendText(layout, "source");
        layout.AddRange(payload);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(builder.Inputs, Is.EqualTo(new CacheKeyInput[] { new("source", Sha256Hex([.. payload])) }));
            Assert.That(key.Hash, Is.EqualTo(Sha256Hex([.. layout])));
        }
    }

    // Inputs is a read-only view, so a caller cannot change what a manifest lists after Build.
    [Test]
    public void Inputs_AfterBuild_IsReadOnly()
    {
        var builder = new CacheKeyBuilder("player").Add("version", "5.4");
        _ = builder.Build();

        Assert.That(((ICollection<CacheKeyInput>)builder.Inputs).IsReadOnly, Is.True);
    }

    // Nothing is hashed before the 1st Build, so Inputs throws with a message to call Build.
    [Test]
    public void Inputs_BeforeBuild_ThrowsInvalidOperationException()
    {
        var builder = new CacheKeyBuilder("player").Add("version", "5.4");

        Assert.That(() => builder.Inputs, Throws.InvalidOperationException.With.Message.Contains("Build"));
    }

    // An Add after Build makes the last list stale, so Inputs throws until the next Build lists both entries.
    [Test]
    public void Inputs_AddAfterBuild_ThrowsUntilNextBuild()
    {
        var builder = new CacheKeyBuilder("player").Add("version", "5.4");
        _ = builder.Build();
        _ = builder.Add("branch", "main");

        Assert.That(() => builder.Inputs, Throws.InvalidOperationException);

        _ = builder.Build();

        Assert.That(builder.Inputs, Has.Count.EqualTo(2));
    }

    // The default UTF-8 encoder turns every lone surrogate into U+FFFD, so 2 such values would share 1 key.
    [TestCase(0xD800)]
    [TestCase(0xDC00)]
    public void Add_ValueWithLoneSurrogate_ThrowsArgumentException(int surrogate)
    {
        CacheKeyBuilder builder = new("player");

        Assert.That(
            () => builder.Add("version", $"a{(char)surrogate}b"),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("value").And.Message.Contains("surrogate"));
    }

    // NTFS allows a lone surrogate in a file name, so 2 such relative paths would share 1 key.
    [Test]
    [Platform("Win")]
    public void Build_FileNameWithLoneSurrogate_ThrowsIOException()
    {
        string folder = NewFolder();
        WriteBytes(folder, $"{(char)0xD800}.txt", [0x61]);
        var builder = new CacheKeyBuilder("player").AddDirectory("source", folder);

        Assert.That(() => builder.Build(), Throws.TypeOf<IOException>().With.Message.Contains("surrogate"));
    }

    // The tag separates a value from a folder, so an empty value and an empty folder under 1 name differ.
    [Test]
    public void Build_EmptyValueAndEmptyFolderUnderSameName_GiveDifferentHashes()
    {
        string folder = NewFolder();

        var value = new CacheKeyBuilder("player").Add("source", "").Build();
        var directory = new CacheKeyBuilder("player").AddDirectory("source", folder).Build();

        Assert.That(directory.Hash, Is.Not.EqualTo(value.Hash));
    }

    // A value of 1M chars builds and its last char still reaches the hash.
    [Test]
    public void Build_HugeValueWithChangedLastChar_ChangesHash()
    {
        string first = new('a', 1 << 20);
        string second = first[..^1] + "b";

        var firstKey = new CacheKeyBuilder("player").Add("version", first).Build();
        var secondKey = new CacheKeyBuilder("player").Add("version", second).Build();

        Assert.That(secondKey.Hash, Is.Not.EqualTo(firstKey.Hash));
    }

    // Add order still does not matter for 4 entries whose '-' and digits sort before letters.
    [Test]
    public void Build_FourNamesInReverseOrder_GivesSameHash()
    {
        var first = new CacheKeyBuilder("player").Add("a", "1").Add("a-b", "2").Add("a0", "3").Add("ab", "4").Build();
        var second = new CacheKeyBuilder("player").Add("ab", "4").Add("a0", "3").Add("a-b", "2").Add("a", "1").Build();

        Assert.That(second.Hash, Is.EqualTo(first.Hash));
    }

    // Build leaves the builder as it was, so a 2nd Build gives the same key and a later Add a new one.
    [Test]
    public void Build_CalledTwiceThenAfterAdd_GivesSameKeyThenNewKey()
    {
        var builder = new CacheKeyBuilder("player").Add("version", "5.4");

        var first = builder.Build();
        var second = builder.Build();
        var third = builder.Add("branch", "main").Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(third.Hash, Is.Not.EqualTo(first.Hash));
        }
    }

    // A rejected Add leaves no entry behind, so the key matches a builder built without it.
    [Test]
    public void Build_AfterRejectedAdds_GivesKeyWithoutThem()
    {
        var builder = new CacheKeyBuilder("player").Add("version", "5.4");
        Assert.That(() => builder.Add("version", "5.5"), Throws.ArgumentException);
        Assert.That(() => builder.Add("Branch", "main"), Throws.ArgumentException);
        Assert.That(() => builder.AddDirectory("source", Path.Combine(_root, "missing")), Throws.TypeOf<DirectoryNotFoundException>());

        var expected = new CacheKeyBuilder("player").Add("version", "5.4").Build();

        Assert.That(builder.Build(), Is.EqualTo(expected));
    }

    // AddDirectory checks its name with the same rule as Add.
    [TestCase("")]
    [TestCase("Source")]
    [TestCase("a.b")]
    public void AddDirectory_BadName_ThrowsArgumentException(string name)
    {
        string folder = NewFolder();
        CacheKeyBuilder builder = new("player");

        Assert.That(() => builder.AddDirectory(name, folder), Throws.ArgumentException);
    }

    // A path to a file is not a folder, so it throws like a missing folder.
    [Test]
    public void AddDirectory_PathOfFile_ThrowsDirectoryNotFoundException()
    {
        string folder = NewFolder();
        WriteBytes(folder, "a.txt", [0x61]);
        CacheKeyBuilder builder = new("player");

        Assert.That(
            () => builder.AddDirectory("source", Path.Combine(folder, "a.txt")),
            Throws.TypeOf<DirectoryNotFoundException>());
    }

    // The folder is read at Build, so a folder deleted after AddDirectory throws then.
    [Test]
    public void Build_FolderDeletedAfterAddDirectory_ThrowsDirectoryNotFoundException()
    {
        string folder = NewFolder();
        var builder = new CacheKeyBuilder("player").AddDirectory("source", folder);
        Directory.Delete(folder);

        Assert.That(() => builder.Build(), Throws.TypeOf<DirectoryNotFoundException>());
    }

    // The full path is resolved first, so a trailing separator, '/' separators or '..' give the same hash.
    [Test]
    public void AddDirectory_SameFolderSpelledAnotherWay_GivesSameHash()
    {
        string folder = NewFolder();
        WriteBytes(folder, "sub/a.txt", [0x61]);
        _ = Directory.CreateDirectory(Path.Combine(_root, "other"));

        string plain = HashOf(folder);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HashOf(folder + Path.DirectorySeparatorChar), Is.EqualTo(plain));
            Assert.That(HashOf(folder.Replace(Path.DirectorySeparatorChar, '/')), Is.EqualTo(plain));
            Assert.That(HashOf(Path.Combine(_root, "other", "..", Path.GetFileName(folder))), Is.EqualTo(plain));
        }
    }

    // A relative path is resolved at AddDirectory, so a later change of the current folder hashes the same folder.
    [Test]
    public void AddDirectory_RelativePathThenCurrentFolderChanged_HashesFolderAtAddTime()
    {
        string first = NewFolder();
        WriteBytes(first, "src/a.txt", [0x61]);
        string second = NewFolder();
        WriteBytes(second, "src/a.txt", [0x62]);
        string expected = HashOf(Path.Combine(first, "src"));

        string current = Directory.GetCurrentDirectory();
        string hash;
        try
        {
            Directory.SetCurrentDirectory(first);
            var builder = new CacheKeyBuilder("player").AddDirectory("source", "src");
            Directory.SetCurrentDirectory(second);
            hash = builder.Build().Hash;
        }
        finally
        {
            Directory.SetCurrentDirectory(current);
        }

        Assert.That(hash, Is.EqualTo(expected));
    }

    // Only files are hashed, so an empty subfolder leaves the hash as it was.
    [Test]
    public void AddDirectory_AddedEmptySubfolder_KeepsHash()
    {
        string first = NewFolder();
        WriteBytes(first, "a.txt", [0x61]);
        string second = NewFolder();
        WriteBytes(second, "a.txt", [0x61]);
        _ = Directory.CreateDirectory(Path.Combine(second, "empty", "deeper"));

        Assert.That(HashOf(second), Is.EqualTo(HashOf(first)));
    }

    // The relative path is hashed as enumerated, so a case-only rename changes the hash.
    [Test]
    public void AddDirectory_FileNameDiffersOnlyInCase_ChangesHash()
    {
        string first = NewFolder();
        WriteBytes(first, "a.txt", [0x61]);
        string second = NewFolder();
        WriteBytes(second, "A.txt", [0x61]);

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    // Only relative paths are hashed, so 2 folders at other paths with the same files give 1 hash.
    [Test]
    public void AddDirectory_SameFilesInFoldersWithOtherNames_GivesSameHash()
    {
        string first = NewFolder();
        WriteBytes(first, "ä/b.txt", [0x62]);
        string second = NewFolder();
        WriteBytes(second, "ä/b.txt", [0x62]);

        Assert.That(HashOf(second), Is.EqualTo(HashOf(first)));
    }

    // Hidden files are inputs too, so a change in 1 changes the hash.
    [Test]
    [Platform("Win")]
    public void AddDirectory_ChangedHiddenFile_ChangesHash()
    {
        string first = NewFolder();
        WriteBytes(first, ".hidden", [0x61]);
        File.SetAttributes(Path.Combine(first, ".hidden"), FileAttributes.Hidden);
        string second = NewFolder();
        WriteBytes(second, ".hidden", [0x62]);
        File.SetAttributes(Path.Combine(second, ".hidden"), FileAttributes.Hidden);

        Assert.That(HashOf(second), Is.Not.EqualTo(HashOf(first)));
    }

    private string NewFolder()
    {
        _folderCount++;

        string folder = Path.Combine(_root, $"folder-{_folderCount}");
        _ = Directory.CreateDirectory(folder);

        return folder;
    }

    private static void WriteText(string folder, string relativePath, string text)
    {
        string path = Path.Combine(folder, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private static void WriteBytes(string folder, string relativePath, byte[] bytes)
    {
        string path = Path.Combine(folder, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path, bytes);
    }

    private static string HashOf(string folder)
    {
        return new CacheKeyBuilder("player").AddDirectory("source", folder).Build().Hash;
    }

    private static void AppendText(List<byte> layout, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        layout.AddRange(BitConverter.GetBytes((uint)bytes.Length));
        layout.AddRange(bytes);
    }

    private static void AppendFile(List<byte> layout, string relativePath, byte[] bytes)
    {
        AppendText(layout, relativePath);
        layout.AddRange(BitConverter.GetBytes((ulong)bytes.Length));
        layout.AddRange(bytes);
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(bytes);

        StringBuilder hex = new();
        foreach (byte b in sha256.GetHashAndReset())
        {
            _ = hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return hex.ToString();
    }
}
