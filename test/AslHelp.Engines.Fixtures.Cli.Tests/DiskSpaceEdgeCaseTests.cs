using AslHelp.Engines.Fixtures.Cli.Hosting;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AslHelp.Engines.Fixtures.Cli.Tests;

public sealed class DiskSpaceEdgeCaseTests
{
    private const string Sha1 = "0000000000000000000000000000000000000001";

    private static readonly string _cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private string _engines = null!;
    private TestFolder _folder = null!;
    private StringWriter _output = null!;
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _root = _folder.NewFolder();
        _engines = Directory.CreateDirectory(Path.Combine(_root, DiskSpace.EnginesFolder)).FullName;
        _output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        _output.Dispose();
        _folder.Delete();
    }

    // A read-only marker doesn't stop a delete, because every file of an engine gets its read-only flag cleared first.
    [Test]
    public void Delete_ReadOnlyMarker_DeletesTheFolder()
    {
        string engine = NewFinishedEngine("engine", ageInDays: 1);
        File.SetAttributes(Path.Combine(engine, EngineMarker.FileName), FileAttributes.ReadOnly);

        DiskSpace.Delete(engine);

        Assert.That(engine, Does.Not.Exist);
    }

    // A read-only engine folder doesn't stop a delete, the same as a read-only folder inside it.
    [Test]
    public void Delete_ReadOnlyEngineFolder_DeletesTheFolder()
    {
        string engine = NewFinishedEngine("engine", ageInDays: 1);
        File.SetAttributes(engine, FileAttributes.Directory | FileAttributes.ReadOnly);

        DiskSpace.Delete(engine);

        Assert.That(engine, Does.Not.Exist);
    }

    // A delete removes a junction inside the engine and leaves its target and the flags of the target's files as they were.
    [Test]
    [Platform("Win")]
    public async Task Delete_JunctionInTheEngine_KeepsTheTargetUnchanged()
    {
        string engine = NewFinishedEngine("engine", ageInDays: 1);
        string target = _folder.NewFolder();
        string file = Path.Combine(target, "file.bin");
        await File.WriteAllTextAsync(file, "text");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var info = ProcessRunner.NewStartInfo(_cmdPath, "/c", "mklink", "/J", Path.Combine(engine, "link"), target);
        await ProcessRunner.RunAsync(info, CancellationToken.None);

        DiskSpace.Delete(engine);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine, Does.Not.Exist);
            Assert.That(file, Does.Exist);
            Assert.That(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly), Is.True);
        }

        File.SetAttributes(file, FileAttributes.Normal);
    }

    // A read-only file past 260 chars goes with its folder, because the paths of an engine can pass that limit under a long output folder.
    [Test]
    public void Delete_ReadOnlyFilePast260Chars_DeletesTheFolder()
    {
        string engine = NewFinishedEngine("engine", ageInDays: 1);
        string deep = engine;

        while (deep.Length <= 260)
        {
            deep = Path.Combine(deep, new string('a', 50));
        }

        _ = Directory.CreateDirectory(deep);
        string file = Path.Combine(deep, "file.bin");
        File.WriteAllText(file, "text");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        DiskSpace.Delete(engine);

        Assert.That(engine, Does.Not.Exist);
    }

    // Another spelling of the wanted folder still keeps it, because losing it means another download.
    [TestCase("\\")]
    [TestCase("/")]
    [TestCase("\\.")]
    [TestCase("\\..\\wanted\\")]
    public void MakeRoom_WantedEngineWithOtherPathEnd_IsNeverDeleted(string pathEnd)
    {
        string wanted = NewFinishedEngine("wanted", ageInDays: 9);

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [wanted + pathEnd], []), _ => 0, _output),
            Throws.InstanceOf<IOException>());
        Assert.That(wanted, Does.Exist);
    }

    // Deletes that free less than the files added up to, as on a compressed folder, fail after the last delete with the numbers.
    [Test]
    public void MakeRoom_DeletesFreeLessThanTheSizes_ThrowsAfterTheLastDelete()
    {
        string first = NewFinishedEngine("a", ageInDays: 1);
        string second = NewFinishedEngine("b", ageInDays: 2);
        long start = DiskSpace.MarginBytes - Sha1.Length;

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => start + DeletedOf(2), _output),
            Throws.InstanceOf<IOException>().With.Message.Contains("has 19 GiB free").And.Message.Contains("needs 20 GiB"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Does.Not.Exist);
            Assert.That(second, Does.Not.Exist);
        }
    }

    // A file in use stops the removal with that failure, so the newer engines stay and only finished deletes get a step line.
    [Test]
    public void MakeRoom_OldestEngineInUse_ThrowsAndKeepsTheNewerEngine()
    {
        string oldest = NewFinishedEngine("oldest", ageInDays: 2);
        string newest = NewFinishedEngine("newest", ageInDays: 1);

        using (File.Open(Path.Combine(oldest, "file.bin"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.That(
                () => DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => DiskSpace.MarginBytes - 1, _output),
                Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EngineMarker.Read(newest), Is.EqualTo(Sha1));
            Assert.That(_output.ToString(), Is.Empty);
        }
    }

    // A folder with spaces, braces, brackets or chars outside ASCII goes like any other and its step line has the path unchanged.
    [TestCase("a b")]
    [TestCase("a{0}b")]
    [TestCase("[x]%20&y")]
    [TestCase("ünï-日本語")]
    public void MakeRoom_OddFolderName_DeletesItAndPrintsThePath(string name)
    {
        string engine = NewFinishedEngine(name, ageInDays: 1);

        DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => DiskSpace.MarginBytes - 1 + DeletedOf(1), _output);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine, Does.Not.Exist);
            Assert.That(_output.ToString().TrimEnd(), Is.EqualTo($"Removed {engine}"));
        }
    }

    // A marker deeper in an unfinished folder doesn't count, because a finished engine has it right under the engine folder.
    [Test]
    public void MakeRoom_MarkerInASubfolder_KeepsTheFolder()
    {
        string unfinished = Path.Combine(_engines, "unfinished");
        string subfolder = Path.Combine(unfinished, "folder");
        _ = Directory.CreateDirectory(subfolder);
        EngineMarker.Write(subfolder, Sha1);

        Assert.That(
            () => DiskSpace.MakeRoom(_root, Space(0, [Path.Combine(_engines, "wanted")], []), _ => 0, _output),
            Throws.InstanceOf<IOException>());
        Assert.That(unfinished, Does.Exist);
    }

    // A root that starts with 2 backslashes fails with a message about the output folder, because DriveInfo only reads drive letters.
    [TestCase(@"\\server\share\engines")]
    [TestCase(@"\\?\C:\engines")]
    [Platform("Win")]
    public void FreeBytes_RootStartingWithTwoBackslashes_ThrowsWithThePath(string directory)
    {
        Assert.That(
            () => DiskSpace.FreeBytes(directory),
            Throws.InstanceOf<IOException>().With.Message.Contains(directory).And.Message.Contains("--output"));
    }

    private int DeletedOf(int engines)
    {
        return engines - Directory.GetDirectories(_engines).Length;
    }

    private static InstallSpace Space(long missingBytes, string[] keepPaths, string[] leftovers)
    {
        return new InstallSpace(missingBytes, 0, keepPaths, leftovers);
    }

    private string NewFinishedEngine(string name, int ageInDays)
    {
        string engine = Path.Combine(_engines, name);
        _ = Directory.CreateDirectory(engine);
        EngineMarker.Write(engine, Sha1);
        File.SetLastWriteTimeUtc(Path.Combine(engine, EngineMarker.FileName), DateTime.UtcNow.AddDays(-ageInDays));

        return engine;
    }
}
