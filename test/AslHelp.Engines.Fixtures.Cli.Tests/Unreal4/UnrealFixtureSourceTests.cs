using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealFixtureSourceTests
{
    private TestFolder _folder = null!;
    private string _source = null!;
    private string _target = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = new TestFolder();
        _source = _folder.NewFolder();
        _target = Path.Combine(_folder.Root, "target");
    }

    [TearDown]
    public void TearDown()
    {
        _folder.Delete();
    }

    // A taken block keeps its lines and the directive lines go away.
    [Test]
    public void Render_IfGreaterOrEqualTaken_KeepsLinesAndDropsDirectives()
    {
        string rendered = RenderText("file.h", "//@if >=4.14\nA\n//@endif\n", "4.14");

        Assert.That(rendered, Is.EqualTo("A\n"));
    }

    // A block that isn't taken goes away and the file is still written because of its ungated lines.
    [Test]
    public void Render_IfNotTaken_DropsLines()
    {
        string rendered = RenderText("file.h", "//@if >=4.14\nA\n//@endif\nB\n", "4.13");

        Assert.That(rendered, Is.EqualTo("B\n"));
    }

    // Every operator compares major.minor.
    [TestCase(">=4.14", "4.14", true)]
    [TestCase(">=4.14", "4.13", false)]
    [TestCase(">4.14", "4.14", false)]
    [TestCase(">4.14", "4.15", true)]
    [TestCase("<4.14", "4.13", true)]
    [TestCase("<4.14", "4.14", false)]
    [TestCase("<=4.14", "4.14", true)]
    [TestCase("<=4.14", "4.15", false)]
    [TestCase("==4.14", "4.14", true)]
    [TestCase("==4.14", "4.15", false)]
    public void Render_EveryOp_ComparesMajorMinor(string condition, string version, bool taken)
    {
        string rendered = RenderText("file.h", $"//@if {condition}\nA\n//@endif\nB\n", version);

        Assert.That(rendered, Is.EqualTo(taken ? "A\nB\n" : "B\n"));
    }

    // A text compare would put 4.10 before 4.9 and treat 4.1 like the start of 4.10.
    [TestCase(">=4.10", "4.10", true)]
    [TestCase(">=4.10", "4.9", false)]
    [TestCase("<4.10", "4.9", true)]
    [TestCase("<4.9", "4.10", false)]
    [TestCase(">4.9", "4.10", true)]
    [TestCase("==4.1", "4.10", false)]
    [TestCase(">4.27", "5.0", true)]
    [TestCase("<5.0", "4.27", true)]
    [TestCase(">=5.0", "4.27", false)]
    public void Render_NumericCompare_UsesMajorThenMinor(string condition, string version, bool taken)
    {
        string rendered = RenderText("file.h", $"//@if {condition}\nA\n//@endif\nB\n", version);

        Assert.That(rendered, Is.EqualTo(taken ? "A\nB\n" : "B\n"));
    }

    // A pinned hotfix version renders like its minor.
    [Test]
    public void Render_PatchVersion_IgnoresThePatch()
    {
        string rendered = RenderText("file.h", "//@if ==4.19\nA\n//@endif\nB\n", "4.19.2");

        Assert.That(rendered, Is.EqualTo("A\nB\n"));
    }

    // Exactly 1 branch stays.
    [TestCase("5.0", "A\n")]
    [TestCase("4.27", "B\n")]
    public void Render_Else_KeepsOnlyTheOtherBranch(string version, string expected)
    {
        string rendered = RenderText("file.h", "//@if >=5.0\nA\n//@else\nB\n//@endif\n", version);

        Assert.That(rendered, Is.EqualTo(expected));
    }

    // An inner condition that's true doesn't bring back lines of an outer block that isn't taken.
    [TestCase("4.21", "C\n")]
    [TestCase("4.16", "A\nC\n")]
    [TestCase("4.14", "C\n")]
    public void Render_NestedIf_InnerIgnoredWhenOuterNotTaken(string version, string expected)
    {
        string rendered = RenderText("file.h", "//@if <4.20\n//@if >=4.15\nA\n//@endif\n//@endif\nC\n", version);

        Assert.That(rendered, Is.EqualTo(expected));
    }

    // An else inside a block that isn't taken stays dropped.
    [Test]
    public void Render_ElseInsideUntakenBlock_StaysDropped()
    {
        string rendered = RenderText("file.h", "//@if <4.20\n//@if >=5.0\nA\n//@else\nB\n//@endif\n//@endif\nC\n", "4.21");

        Assert.That(rendered, Is.EqualTo("C\n"));
    }

    // An if inside an else only counts when the else is the kept branch.
    [TestCase("5.0", "A\n")]
    [TestCase("4.22", "B\n")]
    [TestCase("4.21", "C\n")]
    public void Render_IfInsideElse_KeepsOneBranch(string version, string expected)
    {
        string rendered = RenderText("file.h", "//@if >=5.0\nA\n//@else\n//@if >=4.22\nB\n//@else\nC\n//@endif\n//@endif\n", version);

        Assert.That(rendered, Is.EqualTo(expected));
    }

    // An else after a closed inner block goes back to the outer condition.
    [TestCase("4.19", "X\nY\n")]
    [TestCase("4.18", "X\n")]
    [TestCase("4.20", "Z\n")]
    public void Render_ElseAfterInnerBlock_UsesTheOuterCondition(string version, string expected)
    {
        string rendered = RenderText("file.h", "//@if <4.20\nX\n//@if ==4.19\nY\n//@endif\n//@else\nZ\n//@endif\n", version);

        Assert.That(rendered, Is.EqualTo(expected));
    }

    // An ini file uses its own marker, so //@if is plain text there.
    [Test]
    public void Render_IniDirective_UsesSemicolonMarker()
    {
        string rendered = RenderText("file.ini", ";@if <4.6\nA=1\n;@endif\n//@if <4.6\nB=2\n", "4.6");

        Assert.That(rendered, Is.EqualTo("//@if <4.6\nB=2\n"));
    }

    // A directive can sit at the indent of the code it gates.
    [Test]
    public void Render_IndentedDirective_IsStillADirective()
    {
        string rendered = RenderText("file.h", "{\n    //@if >=4.14\n    A\n    //@endif\n\t//@if <4.14\n    B\n\t//@endif\n}\n", "4.14");

        Assert.That(rendered, Is.EqualTo("{\n    A\n}\n"));
    }

    // Only the exact marker at the start of a line opens a directive.
    [TestCase("file.h", "int A; //@if nothing\n")]
    [TestCase("file.h", "///@if nothing\n")]
    [TestCase("file.h", "// @if nothing\n")]
    [TestCase("file.h", ";@if nothing\n")]
    [TestCase("file.ini", "; @if nothing\n")]
    [TestCase("file.ini", "A=1 ;@endif\n")]
    public void Render_MarkerLookalike_StaysPlainText(string relativePath, string text)
    {
        string rendered = RenderText(relativePath, text, "4.14");

        Assert.That(rendered, Is.EqualTo(text));
    }

    // A directive can be the last bytes of a file, with or without the CR of a cut-off CRLF.
    [TestCase("//@if >=4.14\nA\n//@endif", "A\n")]
    [TestCase("//@if >=4.14\nA\n//@else\nB\n//@endif\r", "A\n")]
    [TestCase("A\n//@if <4.14\nB\n//@endif", "A\n")]
    public void Render_DirectiveAtEndOfFile_StillCounts(string text, string expected)
    {
        string rendered = RenderText("file.h", text, "4.14");

        Assert.That(rendered, Is.EqualTo(expected));
    }

    // Windows treats File.H like file.h, so its directives can't reach the engine.
    [TestCase("FILE.H", "//@if <4.14\nA\n//@endif\nB\n")]
    [TestCase("FILE.INI", ";@if <4.14\nA\n;@endif\nB\n")]
    public void Render_UppercaseExtension_StillReadsDirectives(string relativePath, string text)
    {
        string rendered = RenderText(relativePath, text, "4.14");

        Assert.That(rendered, Is.EqualTo("B\n"));
    }

    // Kept lines keep their CRLF, trailing spaces and the missing final line ending, because the player key hashes the bytes.
    [Test]
    public void Render_Crlf_KeepsEveryByteOfKeptLines()
    {
        byte[] input = Encoding.UTF8.GetBytes("A  \r\n//@if >=4.14\r\nB\r\n//@endif\r\n\r\n\tC");
        TestFolder.WriteBytes(_source, "file.h", input);

        Render("4.14");

        Assert.That(File.ReadAllBytes(Path.Combine(_target, "file.h")), Is.EqualTo(Encoding.UTF8.GetBytes("A  \r\nB\r\n\r\n\tC")));
    }

    // Bytes of a file kind without gates stay the same, even when they look like a directive or aren't text.
    [Test]
    public void Render_BinaryFile_CopiesBytes()
    {
        byte[] input = [0x00, 0xFF, 0x0A, 0x2F, 0x2F, 0x40, 0x69, 0x66, 0x0A, 0x40];
        TestFolder.WriteBytes(_source, "file.bin", input);

        Render("4.14");

        Assert.That(File.ReadAllBytes(Path.Combine(_target, "file.bin")), Is.EqualTo(input));
    }

    // A file with only blank lines left isn't written, because the engine would read an empty source file.
    [TestCase("file.h", "\r\n//@if >=4.14\r\nA\r\n//@endif\r\n \t\r\n")]
    [TestCase("file.h", "")]
    [TestCase("file.cs", " \t")]
    [TestCase("file.ini", "\n\n\r\n")]
    public void Render_FileRendersToWhitespace_IsNotWritten(string relativePath, string text)
    {
        WriteText(_source, relativePath, text);

        Render("4.13");

        Assert.That(File.Exists(Path.Combine(_target, relativePath)), Is.False);
    }

    // 4.18 only stages a project with a Content folder and git can't store an empty folder.
    [Test]
    public void Render_AnyProject_CreatesTheContentFolder()
    {
        WriteText(_source, "file.h", "A\n");

        Render("4.18");

        Assert.That(Directory.Exists(Path.Combine(_target, "Content")), Is.True);
    }

    // A project file gets major.minor in its blank engine association and every other byte stays.
    [TestCase("{\r\n  \"EngineAssociation\": \"\"\r\n}\r\n", "{\r\n  \"EngineAssociation\": \"4.19\"\r\n}\r\n")]
    [TestCase("{ \"EngineAssociation\": \"5.0\" }", "{ \"EngineAssociation\": \"5.0\" }")]
    [TestCase("{ \"EngineAssociation\": \"", "{ \"EngineAssociation\": \"")]
    [TestCase("", "")]
    public void Render_BlankEngineAssociation_FilledWithMajorMinor(string text, string expected)
    {
        string rendered = RenderText("project.uproject", text, "4.19.2");

        Assert.That(rendered, Is.EqualTo(expected));
    }

    // A project file is JSON, so a directive line is plain text there.
    [Test]
    public void Render_ProjectFile_ReadsNoDirective()
    {
        string rendered = RenderText("project.uproject", "//@if nothing\n", "4.19");

        Assert.That(rendered, Is.EqualTo("//@if nothing\n"));
    }

    // A swapped operator fails with the place of the line.
    [Test]
    public void Render_UnknownOp_ThrowsWithFileAndLine()
    {
        Assert.That(
            () => RenderText("file.h", "A\nB\n//@if =>4.14\nC\n//@endif\n", "4.14"),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains($"{Path.Combine(_source, "file.h")}(3)")
                .And.Message.Contains("=>4.14"));
    }

    // Any other word after the marker fails, so a misspelled directive can't end up in a rendered file.
    [TestCase("//@ifdef X")]
    [TestCase("//@elif >=4.14")]
    [TestCase("//@")]
    [TestCase("//@else junk")]
    [TestCase("//@endif junk")]
    [TestCase("//@IF >=4.14")]
    [TestCase("//@ if >=4.14")]
    [TestCase("//@if2 >=4.14")]
    public void Render_UnknownWord_ThrowsWithFileAndLine(string directive)
    {
        Assert.That(
            () => RenderText("file.h", $"A\n{directive}\n", "4.14"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"{Path.Combine(_source, "file.h")}(2)"));
    }

    // A condition compares major.minor only.
    [TestCase(">=4.14.1")]
    [TestCase(">=4")]
    [TestCase(">=")]
    [TestCase(">=a.b")]
    [TestCase(">= 4.14")]
    [TestCase(">=99999999999.1")]
    [TestCase(">=4.14 <5.0")]
    [TestCase("=4.14")]
    [TestCase("!=4.14")]
    [TestCase("")]
    public void Render_ConditionNotMajorMinor_Throws(string condition)
    {
        Assert.That(
            () => RenderText("file.h", $"//@if {condition}\nA\n//@endif\n", "4.14"),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains($"{Path.Combine(_source, "file.h")}(1)")
                .And.Message.Contains(condition));
    }

    // An else needs an open if.
    [Test]
    public void Render_ElseWithoutIf_Throws()
    {
        Assert.That(
            () => RenderText("file.h", "A\n//@else\n", "4.14"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"{Path.Combine(_source, "file.h")}(2)"));
    }

    // A 2nd else fails at its own line and points at the if it belongs to.
    [Test]
    public void Render_SecondElse_ThrowsWithBothLines()
    {
        Assert.That(
            () => RenderText("file.h", "A\n//@if >=4.14\n//@else\n//@else\n//@endif\n", "4.14"),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains($"{Path.Combine(_source, "file.h")}(4)")
                .And.Message.Contains("line 2"));
    }

    // An endif needs an open if.
    [Test]
    public void Render_EndifWithoutIf_Throws()
    {
        Assert.That(
            () => RenderText("file.h", "A\n//@endif\n", "4.14"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"{Path.Combine(_source, "file.h")}(2)"));
    }

    // A file that ends inside a block fails at the line of the open if.
    [Test]
    public void Render_UnclosedIf_ThrowsWithTheIfLine()
    {
        Assert.That(
            () => RenderText("file.h", "A\n//@if >=4.14\nB\n", "4.14"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"{Path.Combine(_source, "file.h")}(2)"));
    }

    // A dropped branch gets checked too, so a typo doesn't hide until another version renders.
    [Test]
    public void Render_BadDirectiveInsideUntakenBlock_StillThrows()
    {
        Assert.That(
            () => RenderText("file.h", "//@if >=5.0\n//@if =>4.14\n//@endif\n//@endif\n", "4.14"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"{Path.Combine(_source, "file.h")}(2)"));
    }

    // Every gated kind fails the same way, with its own marker.
    [TestCase("file.cpp", "A\n//@if =>4.14\n")]
    [TestCase("file.cs", "A\n//@else\n")]
    [TestCase("file.ini", "A\n;@bad\n")]
    public void Render_BadDirectiveInEachKind_ThrowsWithFileAndLine(string relativePath, string text)
    {
        Assert.That(
            () => RenderText(relativePath, text, "4.14"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains($"{Path.Combine(_source, relativePath)}(2)"));
    }

    // A file from an earlier render for another version can't stay in the target.
    [Test]
    public void Render_ExistingTarget_ReplacesIt()
    {
        WriteText(_source, "file.h", "A\n");
        WriteText(_target, "folder/stale.h", "B\n");

        Render("4.14");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_target, "file.h")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(_target, "folder")), Is.False);
        }
    }

    // A bad directive in a later file leaves the last good render alone, so a build can't use a half-written project.
    [Test]
    public void Render_BadDirectiveInLaterFile_KeepsTheOldTarget()
    {
        WriteText(_source, "a.h", "A\n");
        WriteText(_source, "z.h", "//@if =>4.14\n");
        WriteText(_target, "old.h", "old\n");

        Assert.That(() => Render("4.14"), Throws.TypeOf<InvalidDataException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(Path.Combine(_target, "old.h")), Is.EqualTo("old\n"));
            Assert.That(File.Exists(Path.Combine(_target, "a.h")), Is.False);
            Assert.That(Directory.Exists(PartialFolder()), Is.False);
        }
    }

    // A crashed run leaves its temp folder behind and its files can't end up in the next render.
    [Test]
    public void Render_TempFolderFromACrashedRun_LeavesItsFilesOut()
    {
        WriteText(_source, "file.h", "A\n");
        WriteText(PartialFolder(), "stale.h", "stale\n");

        Render("4.14");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_target, "file.h")), Is.True);
            Assert.That(File.Exists(Path.Combine(_target, "stale.h")), Is.False);
            Assert.That(Directory.Exists(PartialFolder()), Is.False);
        }
    }

    // A trailing separator on the target can't put the temp folder inside the target.
    [TestCase("\\")]
    [TestCase("/")]
    public void Render_TargetWithTrailingSeparator_RendersIntoTheTarget(string separator)
    {
        WriteText(_source, "file.h", "A\n");
        WriteText(_target, "old.h", "old\n");

        UnrealFixtureSource.Render(_source, _target + separator, new Version(4, 14));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_target, "file.h")), Is.True);
            Assert.That(File.Exists(Path.Combine(_target, "old.h")), Is.False);
            Assert.That(Directory.Exists(PartialFolder()), Is.False);
        }
    }

    // An old target that's still open fails the render and the temp folder doesn't stay around.
    [Test]
    public void Render_OldTargetFileOpenWithoutSharing_ThrowsIOExceptionAndDeletesTheTempFolder()
    {
        WriteText(_source, "file.h", "A\n");
        WriteText(_target, "open.h", "old\n");

        using (new FileStream(Path.Combine(_target, "open.h"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.That(() => Render("4.14"), Throws.InstanceOf<IOException>());
        }

        Assert.That(Directory.Exists(PartialFolder()), Is.False);
    }

    // A missing source fails before the target goes away.
    [Test]
    public void Render_MissingSource_ThrowsDirectoryNotFound()
    {
        string missing = Path.Combine(_folder.Root, "missing");
        WriteText(_target, "file.h", "A\n");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => UnrealFixtureSource.Render(missing, _target, new Version(4, 14)),
                Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(missing));
            Assert.That(File.Exists(Path.Combine(_target, "file.h")), Is.True);
        }
    }

    private static void WriteText(string folder, string relativePath, string text)
    {
        TestFolder.WriteBytes(folder, relativePath, Encoding.UTF8.GetBytes(text));
    }

    private string PartialFolder()
    {
        return _target + TempPaths.PartialSuffix;
    }

    private void Render(string version)
    {
        UnrealFixtureSource.Render(_source, _target, Version.Parse(version));
    }

    private string RenderText(string relativePath, string text, string version)
    {
        WriteText(_source, relativePath, text);

        Render(version);

        return File.ReadAllText(Path.Combine(_target, relativePath));
    }
}
