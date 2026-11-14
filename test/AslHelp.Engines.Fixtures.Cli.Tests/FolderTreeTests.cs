using NUnit.Framework;
using System.IO;
using static AslHelp.Engines.Fixtures.Cli.Tests.TestFolder;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class FolderTreeTests
{
    private TestFolder _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // A read-only file, a read-only hidden file and a read-only folder all go, because Directory.Delete alone refuses each of them.
    [Test]
    public void Delete_ReadOnlyEntries_DeletesTheTree()
    {
        string tree = Path.Combine(_folder.NewFolder(), "tree");
        WriteBytes(tree, @"sub\file.bin", [1]);
        WriteBytes(tree, "hidden.bin", [2]);
        File.SetAttributes(Path.Combine(tree, @"sub\file.bin"), FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(tree, "hidden.bin"), FileAttributes.ReadOnly | FileAttributes.Hidden);
        new DirectoryInfo(Path.Combine(tree, "sub")).Attributes |= FileAttributes.ReadOnly;
        new DirectoryInfo(tree).Attributes |= FileAttributes.ReadOnly;

        FolderTree.Delete(tree);

        Assert.That(Directory.Exists(tree), Is.False);
    }
}
