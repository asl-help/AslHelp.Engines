using AslHelp.Engines.Measuring.Cli.Unreal3;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal3;

public sealed class UdkTargetsTests
{
    private const string NameArray = """ "Names": { "$type": "nameArray", "firstNames": ["A", "B"] } """;
    private const string ObjectArray = """ "Objects": { "$type": "objectArray" } """;
    private const string TargetsPath = "targets.example";

    private static readonly string[] _globals = ["GNames", "GObjObjects", "GEngine", "GWorld"];

    // The embedded file has the 4 globals, each with its type.
    [Test]
    public void Read_EmbeddedFile_HasTheFourGlobals()
    {
        var targets = UdkTargets.ReadEmbedded().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets.Keys, Is.EquivalentTo(_globals));
            Assert.That(targets["GNames"], Is.TypeOf<UdkNameArrayTarget>().With.Property("FirstNames").Count.EqualTo(16));
            Assert.That(targets["GObjObjects"], Is.TypeOf<UdkObjectArrayTarget>());
            Assert.That(targets["GEngine"], Is.EqualTo(new UdkObjectSlotTarget("Engine")));
            Assert.That(targets["GWorld"], Is.EqualTo(new UdkObjectSlotTarget("World", "TheWorld")));
        }
    }

    // A hand-written target with its type after its other keys parses.
    [Test]
    public void Read_TypeAfterOtherKeys_Parses()
    {
        var targets = Read($$"""{ "Names": { "firstNames": ["A", "B"], "$type": "nameArray" }, {{ObjectArray}} }""").Unwrap();

        Assert.That(targets["Names"], Is.TypeOf<UdkNameArrayTarget>());
    }

    // A type that no code handles fails with the path, because a member has no place in the file.
    [Test]
    public void Read_UnknownType_FailsWithThePath()
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, "Type.Member": { "$type": "member" } }""");
    }

    // A target without a type fails with the path and not with the error of the abstract record.
    [Test]
    public void Read_MissingType_FailsWithThePath()
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, "Other": { } }""");
    }

    // A misspelled key fails the parse, because the optional object name would otherwise stay null with no error.
    [Test]
    public void Read_UnknownKey_FailsWithThePath()
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, "Slot": { "$type": "objectSlot", "class": "A", "objekt": "B" } }""");
    }

    // A file with no targets fails before any player.
    [Test]
    public void Read_EmptyObject_FailsWithThePath()
    {
        AssertRefused("{}");
    }

    // A null target fails with a message and not a null reference later.
    [Test]
    public void Read_NullTarget_FailsWithTheGlobal()
    {
        Assert.That(
            Read($$"""{ {{NameArray}}, {{ObjectArray}}, "Other": null }""").UnwrapErr().Message,
            Does.Contain("Other").And.Contain(TargetsPath));
    }

    // A global written twice would lose the 1st target with no error.
    [Test]
    public void Read_GlobalWrittenTwice_FailsWithThePath()
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, "Objects": { "$type": "objectSlot", "class": "A" } }""");
    }

    // The name array search starts from 2 names, so 1 name is not enough.
    [Test]
    public void Read_NameArrayWithOneFirstName_Fails()
    {
        Assert.That(
            Read($$"""{ "Names": { "$type": "nameArray", "firstNames": ["A"] }, {{ObjectArray}} }""").UnwrapErr().Message,
            Does.Contain("Names").And.Contain(TargetsPath));
    }

    // 2 name arrays would search for 1 array twice, with 2 lists of names.
    [Test]
    public void Read_TwoNameArrays_Fails()
    {
        AssertRefused($$"""{ {{NameArray}}, "Other": { "$type": "nameArray", "firstNames": ["A", "B"] }, {{ObjectArray}} }""");
    }

    // Measuring the members needs the object array, so it is required even in a file that lists no slot.
    [Test]
    public void Read_NoObjectArray_Fails()
    {
        AssertRefused($$"""{ {{NameArray}} }""");
    }

    // A path to --targets that does not exist fails with that path.
    [Test]
    public void ReadFile_MissingFile_FailsWithThePath()
    {
        string missing = Path.Combine(Path.GetTempPath(), "missing-targets.example");

        Assert.That(UdkTargets.ReadFile(missing).UnwrapErr().Message, Is.EqualTo(UdkSR.FormatTargetsNotFound(missing)));
    }

    // A file saved with a byte order mark parses, because Windows editors write one.
    [Test]
    public void Read_ByteOrderMark_Parses()
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        using MemoryStream stream = new([.. bom, .. Encoding.UTF8.GetBytes($$"""{ {{NameArray}}, {{ObjectArray}} }""")]);

        Assert.That(UdkTargets.Read(stream, TargetsPath).Unwrap(), Has.Count.EqualTo(2));
    }

    // Keys and types compare with their case, because a misspelled key has to fail.
    [TestCase(""" "Names": { "$type": "NAMEARRAY", "firstNames": ["A", "B"] } """)]
    [TestCase(""" "Names": { "$Type": "nameArray", "firstNames": ["A", "B"] } """)]
    [TestCase(""" "Names": { "$type": "nameArray", "FirstNames": ["A", "B"] } """)]
    public void Read_KeyOrTypeInAnotherCase_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{target}}, {{ObjectArray}} }""");
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

    // A value of the wrong type or a missing value fails the parse.
    [TestCase(""" "Names": { "$type": "nameArray" } """)]
    [TestCase(""" "Names": { "$type": "nameArray", "firstNames": null } """)]
    [TestCase(""" "Names": { "$type": "nameArray", "firstNames": "A" } """)]
    public void Read_BadNameArray_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{target}}, {{ObjectArray}} }""");
    }

    // A slot needs its class, because no search can start without one.
    [TestCase(""" "Slot": { "$type": "objectSlot" } """)]
    [TestCase(""" "Slot": { "$type": "objectSlot", "class": null } """)]
    [TestCase(""" "Slot": { "$type": "objectSlot", "class": 1 } """)]
    public void Read_SlotWithoutClass_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, {{target}} }""");
    }

    // A slot with a null object is the same as a slot with no object.
    [Test]
    public void Read_SlotWithNullObject_Parses()
    {
        var targets = Read($$"""{ {{NameArray}}, {{ObjectArray}}, "Slot": { "$type": "objectSlot", "class": "A", "object": null } }""").Unwrap();

        Assert.That(targets["Slot"], Is.EqualTo(new UdkObjectSlotTarget("A")));
    }

    // A null or blank first name passes the parse and would then search for an empty text, so it has to fail before the 1st player.
    [TestCase("""["A", null]""")]
    [TestCase("""[null, "B"]""")]
    [TestCase("""["", "B"]""")]
    [TestCase("""["A", "  "]""")]
    [TestCase("""["A", "B", ""]""")]
    public void Read_NullOrBlankFirstName_FailsWithThePath(string firstNames)
    {
        AssertRefused($$"""{ "Names": { "$type": "nameArray", "firstNames": {{firstNames}} }, {{ObjectArray}} }""");
    }

    // A blank value finds nothing in any player, so it has to fail before the 1st player.
    [TestCase(""" "": { "$type": "objectSlot", "class": "A" } """)]
    [TestCase(""" "Slot": { "$type": "objectSlot", "class": "" } """)]
    [TestCase(""" "Slot": { "$type": "objectSlot", "class": " " } """)]
    [TestCase(""" "Slot": { "$type": "objectSlot", "class": "A", "object": "" } """)]
    public void Read_BlankSlotValue_FailsWithThePath(string target)
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, {{target}} }""");
    }

    // A name array with an empty list has the same problem as one with 1 name.
    [Test]
    public void Read_NameArrayWithNoFirstNames_Fails()
    {
        AssertRefused($$"""{ "Names": { "$type": "nameArray", "firstNames": [] }, {{ObjectArray}} }""");
    }

    // 2 object arrays would report 1 array under 2 globals.
    [Test]
    public void Read_TwoObjectArrays_Fails()
    {
        AssertRefused($$"""{ {{NameArray}}, {{ObjectArray}}, "Other": { "$type": "objectArray" } }""");
    }

    // Every member search reads names, so the name array is required.
    [Test]
    public void Read_NoNameArray_Fails()
    {
        AssertRefused($$"""{ {{ObjectArray}} }""");
    }

    // Globals compare ordinally, so 2 globals that differ in case stay 2 keys in the report.
    [Test]
    public void Read_GlobalsDifferingInCase_Parses()
    {
        var targets = Read($$"""{ {{NameArray}}, {{ObjectArray}}, "objects": { "$type": "objectSlot", "class": "A" } }""").Unwrap();

        Assert.That(targets, Has.Count.EqualTo(3));
    }

    // Names outside ASCII come through the parse as they were written.
    [Test]
    public void Read_UnicodeGlobal_KeepsIt()
    {
        var targets = Read($$"""{ {{NameArray}}, {{ObjectArray}}, "\u00DC": { "$type": "objectSlot", "class": "\u30AF" } }""").Unwrap();

        Assert.That(targets["\u00DC"], Is.EqualTo(new UdkObjectSlotTarget("\u30AF")));
    }

    private static Result<IReadOnlyDictionary<string, UdkTarget>> Read(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        return UdkTargets.Read(stream, TargetsPath);
    }

    private static void AssertRefused(string json)
    {
        Assert.That(Read(json).UnwrapErr().Message, Does.Contain(TargetsPath));
    }
}
