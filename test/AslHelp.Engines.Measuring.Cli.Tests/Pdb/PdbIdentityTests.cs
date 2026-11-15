using AslHelp.Engines.Measuring.Cli.Pdb;
using AslHelp.Engines.Measuring.Cli.Tests.Routes;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection.PortableExecutable;

namespace AslHelp.Engines.Measuring.Cli.Tests.Pdb;

public sealed class PdbIdentityTests
{
    private static readonly Guid _first = new("d43d3384-a767-42b9-8883-d83e5ab17338");
    private static readonly Guid _second = new("11111111-2222-3333-4444-555555555555");

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

    // A PDB of another build fails with both GUIDs and both ages.
    [Test]
    public void Check_DifferentGuid_FailsWithBothValues()
    {
        var result = new PdbIdentity(_first, 3).Check(new ExecutableIdentity(_second, 7, "file.pdb"));

        string message = result.UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain(_first.ToString()));
            Assert.That(message, Does.Contain(_second.ToString()));
            Assert.That(message, Does.Contain("age 3"));
            Assert.That(message, Does.Contain("age 7"));
        }
    }

    // A later link of the same PDB keeps the GUID and only the age changes.
    [Test]
    public void Check_SameGuidDifferentAge_Fails()
    {
        var result = new PdbIdentity(_first, 1).Check(new ExecutableIdentity(_first, 2, "file.pdb"));

        Assert.That(result.IsErr, Is.True);
    }

    // The same GUID and age pass.
    [Test]
    public void Check_SamePair_Passes()
    {
        var result = new PdbIdentity(_first, 1).Check(new ExecutableIdentity(_first, 1, "file.pdb"));

        Assert.That(result.IsOk, Is.True);
    }

    // The test assembly has a CodeView entry, so its GUID, age and PDB name come from real headers.
    [Test]
    public void Read_TestAssembly_ReturnsTheCodeViewEntry()
    {
        string path = typeof(PdbIdentityTests).Assembly.Location;
        using PEReader reader = new(File.OpenRead(path));
        CodeViewDebugDirectoryData expected = default;

        foreach (var entry in reader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                expected = reader.ReadCodeViewDebugDirectoryData(entry);
            }
        }

        var identity = ExecutableIdentity.Read(path).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.Guid, Is.EqualTo(expected.Guid));
            Assert.That(identity.Guid, Is.Not.EqualTo(Guid.Empty));
            Assert.That(identity.Age, Is.EqualTo(expected.Age));
            Assert.That(identity.PdbName, Is.EqualTo(expected.Path));
        }
    }

    // A PE file without a debug directory fails with its path.
    [Test]
    public void Read_PeWithoutDebugDirectory_FailsWithPath()
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        CodeBuilder.WritePe(path, new CodeBuilder(true).Ret().Build());

        Assert.That(ExecutableIdentity.Read(path).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNoCodeView(path)));
    }

    // A file that is not a PE file fails with its path, in the wording CodeImage uses.
    [TestCase(0)]
    [TestCase(16)]
    [TestCase(0x1000)]
    public void Read_FileThatIsNoPeFile_FailsWithPath(int length)
    {
        string path = Path.Combine(_folder.Root, "file.bin");
        File.WriteAllBytes(path, new byte[length]);

        Assert.That(ExecutableIdentity.Read(path).UnwrapErr().Message, Is.EqualTo(SR.FormatExecutableNotPe(path)));
    }

    // A missing file fails with its path and the reason of the open.
    [Test]
    public void Read_MissingFile_FailsWithPath()
    {
        string path = Path.Combine(_folder.Root, "missing.bin");

        Assert.That(ExecutableIdentity.Read(path).UnwrapErr().Message, Does.StartWith($"Can't read '{path}': "));
    }
}
