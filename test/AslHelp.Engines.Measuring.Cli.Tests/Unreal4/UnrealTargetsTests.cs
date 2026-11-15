using AslHelp.Engines.Measuring.Cli.Unreal4;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealTargetsTests
{
    private const string Global = """ "Global": { "$type": "global" } """;
    private const string TargetsPath = "targets.example";

    // The embedded file has the 3 globals, the static of the name table, the property types of both forms and the game types.
    [Test]
    public void Read_EmbeddedFile_HasTheGlobalsAndTypes()
    {
        var targets = UnrealTargets.ReadEmbedded().Unwrap();

        var nameTable = (UnrealFunctionStaticTarget)targets["NameTable"];
        var property = (UnrealLayoutTarget)targets["UProperty"];
        var ffieldProperty = (UnrealLayoutTarget)targets["FProperty"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                targets["GUObjectArray"],
                Is.EqualTo(new UnrealGlobalTarget(ConstructedType: "FUObjectArray", ConstructedFrom: "4.8", ConstructedUntil: "4.10")));
            Assert.That(targets["GWorld"], Is.EqualTo(new UnrealGlobalTarget()));
            Assert.That(targets["GEngine"], Is.EqualTo(new UnrealGlobalTarget()));
            Assert.That(nameTable.Function, Is.EqualTo("FName::GetNames"));
            Assert.That(nameTable.Until, Is.EqualTo("4.22"));
            Assert.That(property.Until, Is.EqualTo("4.24"));
            Assert.That(property.Members, Does.ContainKey("Offset_Internal"));
            Assert.That(ffieldProperty.From, Is.EqualTo("4.25"));
            Assert.That(ffieldProperty.Members, Does.ContainKey("Offset_Internal"));
            Assert.That(targets["UEngine"], Is.TypeOf<UnrealLayoutTarget>().With.Property("Members").Empty);
        }
    }

    // A member of the embedded file can have its own range inside the range of its type.
    [Test]
    public void Read_EmbeddedFile_HasTheMemberRanges()
    {
        var targets = UnrealTargets.ReadEmbedded().Unwrap();

        var nameEntry = (UnrealLayoutTarget)targets["FNameEntry"];
        var setLayout = (UnrealLayoutTarget)targets["FScriptSetLayout"];
        var structType = (UnrealLayoutTarget)targets["UStruct"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nameEntry.Members["Index"], Is.EqualTo(new UnrealMemberTarget(Until: "4.22")));
            Assert.That(nameEntry.Members["Header"], Is.EqualTo(new UnrealMemberTarget(From: "4.23")));
            Assert.That(setLayout.Members["ElementOffset"], Is.EqualTo(new UnrealMemberTarget(Until: "4.21")));
            Assert.That(structType.Members["ChildProperties"], Is.EqualTo(new UnrealMemberTarget(From: "4.25")));
        }
    }

    // A hand-written target with its type after its other keys parses.
    [Test]
    public void Read_TypeAfterOtherKeys_Parses()
    {
        var targets = Read("""{ "Static": { "function": "A::B", "$type": "functionStatic" } }""").Unwrap();

        Assert.That(targets["Static"], Is.EqualTo(new UnrealFunctionStaticTarget("A::B")));
    }

    // A type that no code handles fails with the path, because nothing can find it.
    [Test]
    public void Read_UnknownType_FailsWithThePath()
    {
        AssertRefused($$"""{ {{Global}}, "Type.Member": { "$type": "member" } }""");
    }

    // A target without a type fails with the path and not with the error of the abstract record.
    [Test]
    public void Read_MissingType_FailsWithThePath()
    {
        AssertRefused($$"""{ {{Global}}, "Other": { } }""");
    }

    // A misspelled key fails the parse, because the optional range would otherwise stay null without an error.
    [Test]
    public void Read_UnknownKey_FailsWithThePath()
    {
        AssertRefused("""{ "Global": { "$type": "global", "untill": "4.22" } }""");
    }

    // A file without targets fails before any player.
    [Test]
    public void Read_EmptyObject_FailsWithThePath()
    {
        AssertRefused("{}");
    }

    // A null target fails with a message and not a null reference later.
    [Test]
    public void Read_NullTarget_FailsWithTheName()
    {
        Assert.That(
            Read($$"""{ {{Global}}, "Other": null }""").UnwrapErr().Message,
            Does.Contain("Other").And.Contain(TargetsPath));
    }

    // A target or a member written twice would lose the 1st one without an error.
    [TestCase("""{ "Global": { "$type": "global" }, "Global": { "$type": "global" } }""")]
    [TestCase("""{ "Type": { "$type": "layout", "members": { "Member": {}, "Member": {} } } }""")]
    public void Read_TargetWrittenTwice_FailsWithThePath(string json)
    {
        AssertRefused(json);
    }

    // A range outside major.minor or with its ends the wrong way around fails with the name and the path before any player.
    [TestCase(""" "from": "4" """)]
    [TestCase(""" "from": "4.a" """)]
    [TestCase(""" "from": "" """)]
    [TestCase(""" "from": "4.19.2" """)]
    [TestCase(""" "until": "4" """)]
    [TestCase(""" "from": "4.25", "until": "4.24" """)]
    [TestCase(""" "from": "4.10", "until": "4.9" """)]
    public void Read_BadRange_FailsWithTheName(string range)
    {
        string global = Read($$"""{ "Global": { "$type": "global", {{range}} } }""").UnwrapErr().Message;
        string member = Read($$"""{ "Type": { "$type": "layout", "members": { "Member": { {{range}} } } } }""").UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(global, Does.Contain("Global").And.Contain(TargetsPath));
            Assert.That(member, Does.Contain("Type.Member").And.Contain(TargetsPath));
        }
    }

    // A range with both ends on 1 minor and a 4.9 to 4.10 range parse, because the ends compare as numbers.
    [TestCase(""" "from": "4.25", "until": "4.25" """)]
    [TestCase(""" "from": "4.9", "until": "4.10" """)]
    public void Read_GoodRange_Parses(string range)
    {
        Assert.That(Read($$"""{ "Global": { "$type": "global", {{range}} } }""").IsOk, Is.True);
    }

    // A layout with an empty list reports the size alone, so only a missing list fails.
    [Test]
    public void Read_LayoutWithoutMembers_Parses()
    {
        var targets = Read("""{ "Type": { "$type": "layout", "members": {} } }""").Unwrap();

        Assert.That(targets["Type"], Is.TypeOf<UnrealLayoutTarget>().With.Property("Members").Empty);
    }

    // A layout without a member list fails, because only an empty list reports the size alone.
    [TestCase(""" "Type": { "$type": "layout" } """)]
    [TestCase(""" "Type": { "$type": "layout", "members": null } """)]
    [TestCase(""" "Type": { "$type": "layout", "members": [] } """)]
    public void Read_BadMemberList_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{target}} }""");
    }

    // A blank function or member finds nothing in any player, so it has to fail before the 1st player.
    [TestCase(""" "Static": { "$type": "functionStatic", "function": "" } """)]
    [TestCase(""" "Static": { "$type": "functionStatic", "function": " " } """)]
    [TestCase(""" "Type": { "$type": "layout", "members": { " ": {} } } """)]
    [TestCase(""" "Type": { "$type": "layout", "members": { "": {} } } """)]
    [TestCase(""" "": { "$type": "global" } """)]
    public void Read_BlankFunctionOrMember_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{target}} }""");
    }

    // A function target needs its function, because no search can start without one.
    [TestCase(""" "Static": { "$type": "functionStatic" } """)]
    [TestCase(""" "Static": { "$type": "functionStatic", "function": null } """)]
    [TestCase(""" "Static": { "$type": "functionStatic", "function": 1 } """)]
    public void Read_FunctionStaticWithoutFunction_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{target}} }""");
    }

    // The report lists a constant under its scope, so a constant without one fails before the 1st player.
    [TestCase("Value")]
    [TestCase("::Value")]
    [TestCase("Scope::")]
    public void Read_ConstantWithoutScope_FailsWithTheName(string name)
    {
        string message = Read($$"""{ "{{name}}": { "$type": "constant" } }""").UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(UnrealSR.FormatTargetsConstantUnscoped(TargetsPath, name)));
    }

    // A null member fails with a message and not a null reference later.
    [Test]
    public void Read_NullMember_FailsWithTheMember()
    {
        Assert.That(
            Read("""{ "Type": { "$type": "layout", "members": { "Member": null } } }""").UnwrapErr().Message,
            Does.Contain("Type.Member").And.Contain(TargetsPath));
    }

    // Keys and types compare with their case, because a misspelled key has to fail.
    [TestCase(""" "Global": { "$type": "GLOBAL" } """)]
    [TestCase(""" "Global": { "$Type": "global" } """)]
    [TestCase(""" "Global": { "$type": "global", "From": "4.1" } """)]
    public void Read_KeyOrTypeInAnotherCase_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{target}} }""");
    }

    // Anything but an object of targets fails with the path and not with the error of the parser alone.
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("1")]
    [TestCase("{")]
    [TestCase("""{ "A": 1 }""")]
    [TestCase("""{ "A": [] }""")]
    public void Read_NotAnObjectOfTargets_FailsWithThePath(string json)
    {
        AssertRefused(json);
    }

    // A file saved with a byte order mark parses, because Windows editors write one.
    [Test]
    public void Read_ByteOrderMark_Parses()
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        using MemoryStream stream = new([.. bom, .. Encoding.UTF8.GetBytes($$"""{ {{Global}} }""")]);

        Assert.That(UnrealTargets.Read(stream, TargetsPath).Unwrap(), Has.Count.EqualTo(1));
    }

    // A path to --targets that does not exist fails with that path.
    [Test]
    public void ReadFile_MissingFile_FailsWithThePath()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-targets.example");

        Assert.That(UnrealTargets.ReadFile(missing).UnwrapErr().Message, Is.EqualTo(SR.FormatTargetsNotFound(missing)));
    }

    // Targets compare ordinally, so 2 targets that differ in case stay 2 keys in the report.
    [Test]
    public void Read_TargetsDifferingInCase_Parses()
    {
        var targets = Read($$"""{ {{Global}}, "global": { "$type": "global" } }""").Unwrap();

        Assert.That(targets, Has.Count.EqualTo(2));
    }

    // Text outside ASCII comes through the parse as it was written.
    [Test]
    public void Read_UnicodeTarget_KeepsIt()
    {
        var targets = Read("""{ "Ü": { "$type": "functionStatic", "function": "ク" } }""").Unwrap();

        Assert.That(targets["Ü"], Is.EqualTo(new UnrealFunctionStaticTarget("ク")));
    }

    private static Result<IReadOnlyDictionary<string, UnrealTarget>> Read(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        return UnrealTargets.Read(stream, TargetsPath);
    }

    private static void AssertRefused(string json)
    {
        Assert.That(Read(json).UnwrapErr().Message, Does.Contain(TargetsPath));
    }
}
