using AslHelp.Engines.Measuring.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AslHelp.Engines.Measuring.Cli.Tests.Unreal4;

public sealed class UnrealTargetsEdgeTests
{
    private const string TargetsPath = "targets.example";

    // A with expression that changes a bound parses it again, so the parsed minor never keeps the old value.
    [Test]
    public void With_ChangedBound_ParsesItAgain()
    {
        Version older = new(4, 8);
        Version newer = new(4, 10);

        var global = new UnrealGlobalTarget("4.1", "4.2", "T", "4.3", "4.4") with
        {
            From = "4.8",
            Until = "4.10",
            ConstructedFrom = "4.8",
            ConstructedUntil = "4.10",
        };

        var member = new UnrealMemberTarget("4.1", "4.2", "Old", "4.3") with { From = "4.8", Until = "4.10", OldNameUntil = "4.8" };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(global.FromMinor, Is.EqualTo(older));
            Assert.That(global.UntilMinor, Is.EqualTo(newer));
            Assert.That(global.ConstructedFromMinor, Is.EqualTo(older));
            Assert.That(global.ConstructedUntilMinor, Is.EqualTo(newer));
            Assert.That(member.FromMinor, Is.EqualTo(older));
            Assert.That(member.UntilMinor, Is.EqualTo(newer));
            Assert.That(member.OldNameUntilMinor, Is.EqualTo(older));
            Assert.That(global, Is.EqualTo(new UnrealGlobalTarget("4.8", "4.10", "T", "4.8", "4.10")));
        }
    }

    // A key of another target type fails the parse, because a global with a function would search a public and not the function.
    [TestCase(""" "A": { "$type": "global", "function": "B::C" } """)]
    [TestCase(""" "A": { "$type": "global", "members": {} } """)]
    [TestCase(""" "A": { "$type": "layout", "function": "B::C", "members": {} } """)]
    [TestCase(""" "A": { "$type": "functionStatic", "function": "B::C", "members": {} } """)]
    [TestCase(""" "A": { "$type": "layout", "members": { "B": { "members": {} } } } """)]
    [TestCase(""" "A": { "$type": "layout", "members": { "B": { "$type": "global" } } } """)]
    public void Read_KeyOfAnotherType_FailsWithThePath(string target)
    {
        Assert.That(Read($$"""{ {{target}} }""").UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // A range of a type or a static fails like a range of a global, with the target in the message.
    [TestCase(""" "A": { "$type": "layout", "from": "4", "members": {} } """)]
    [TestCase(""" "A": { "$type": "layout", "until": "4.19.2", "members": {} } """)]
    [TestCase(""" "A": { "$type": "functionStatic", "function": "B::C", "from": "4.x" } """)]
    [TestCase(""" "A": { "$type": "functionStatic", "function": "B::C", "from": "4.2", "until": "4.1" } """)]
    [TestCase(""" "A": { "$type": "global", "from": "4.19.2.1" } """)]
    public void Read_BadRangeOfATarget_FailsWithTheTarget(string target)
    {
        Assert.That(Read($$"""{ {{target}} }""").UnwrapErr().Message, Does.Contain("'A' has a bad range").And.Contain(TargetsPath));
    }

    // A constructed range needs its type and both of its ends in order, because a range open at 1 end is the same as a shorter from or until.
    [TestCase(""" "constructedFrom": "4.8" """)]
    [TestCase(""" "constructedUntil": "4.10" """)]
    [TestCase(""" "constructedType": "T", "constructedFrom": "4.8" """)]
    [TestCase(""" "constructedType": "T", "constructedUntil": "4.10" """)]
    [TestCase(""" "constructedFrom": "4.8", "constructedUntil": "4.10" """)]
    [TestCase(""" "constructedType": "T", "constructedFrom": "4.10", "constructedUntil": "4.8" """)]
    [TestCase(""" "constructedType": "T", "constructedFrom": "4", "constructedUntil": "4.8" """)]
    public void Read_BadConstructedRange_FailsWithTheTarget(string constructed)
    {
        string message = Read($$"""{ "A": { "$type": "global", {{constructed}} } }""").UnwrapErr().Message;

        Assert.That(message, Does.Contain("'A' has a bad range").And.Contain(TargetsPath));
    }

    // A blank type can't build the name of a constructor, so it fails before the 1st player.
    [TestCase("")]
    [TestCase(" ")]
    public void Read_BlankConstructedType_FailsWithTheTarget(string type)
    {
        string json = $$"""{ "A": { "$type": "global", "constructedType": "{{type}}", "constructedFrom": "4.8", "constructedUntil": "4.8" } }""";

        Assert.That(Read(json).UnwrapErr().Message, Is.EqualTo(SR.FormatTargetsBlankValue(TargetsPath, "A")));
    }

    // A constructed range with both ends on 1 minor and a range across a minor past 9 parse, because the ends compare as numbers.
    [TestCase(""" "constructedFrom": "4.8", "constructedUntil": "4.8" """)]
    [TestCase(""" "constructedFrom": "4.9", "constructedUntil": "4.10" """)]
    public void Read_GoodConstructedRange_Parses(string range)
    {
        Assert.That(Read($$"""{ "A": { "$type": "global", "constructedType": "T", {{range}} } }""").IsOk, Is.True);
    }

    // A type without ends goes through its constructor on every version, because the static never has a public.
    [Test]
    public void Read_ConstructedTypeWithoutEnds_Parses()
    {
        var targets = Read("""{ "A": { "$type": "global", "from": "4.23", "constructedType": "T" } }""").Unwrap();

        Assert.That(targets["A"], Is.EqualTo(new UnrealGlobalTarget(From: "4.23", ConstructedType: "T")));
    }

    // Only a global can have a constructed range, so the keys on another target fail the parse.
    [TestCase(""" "A": { "$type": "layout", "constructedFrom": "4.8", "constructedUntil": "4.10", "members": {} } """)]
    [TestCase(""" "A": { "$type": "functionStatic", "function": "B::C", "constructedType": "T" } """)]
    [TestCase(""" "A": { "$type": "layout", "members": { "B": { "constructedFrom": "4.8", "constructedUntil": "4.10" } } } """)]
    public void Read_ConstructedRangeOnAnotherTarget_FailsWithThePath(string target)
    {
        Assert.That(Read($$"""{ {{target}} }""").UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // An old name needs its end and the end can't sit before the start of the member, because the old name would never or always apply.
    [TestCase(""" "oldName": "Old" """)]
    [TestCase(""" "oldNameUntil": "4.7" """)]
    [TestCase(""" "from": "4.8", "oldName": "Old", "oldNameUntil": "4.7" """)]
    [TestCase(""" "oldName": "Old", "oldNameUntil": "4" """)]
    [TestCase(""" "oldName": "Old", "oldNameUntil": "4.7.4" """)]
    public void Read_BadOldNameRange_FailsWithTheMember(string member)
    {
        string message = Read($$"""{ "A": { "$type": "layout", "members": { "B": { {{member}} } } } }""").UnwrapErr().Message;

        Assert.That(message, Does.Contain("'A.B' has a bad range").And.Contain(TargetsPath));
    }

    // A member range outside the range of its type fails, because the measurement never reaches a version outside it.
    [TestCase(""" "until": "4.24", "members": { "B": { "from": "4.25" } } """)]
    [TestCase(""" "from": "4.20", "members": { "B": { "until": "4.19" } } """)]
    public void Read_MemberRangeOutsideItsType_FailsWithTheMember(string layout)
    {
        string message = Read($$"""{ "A": { "$type": "layout", {{layout}} } }""").UnwrapErr().Message;

        Assert.That(message, Does.Contain("'A.B' has a bad range").And.Contain(TargetsPath));
    }

    // A blank old name can't match a member of the PDB, so it fails before the 1st player.
    [TestCase("")]
    [TestCase(" ")]
    public void Read_BlankOldName_FailsWithTheMember(string oldName)
    {
        string json = $$"""{ "A": { "$type": "layout", "members": { "B": { "oldName": "{{oldName}}", "oldNameUntil": "4.7" } } } }""";

        Assert.That(Read(json).UnwrapErr().Message, Is.EqualTo(SR.FormatTargetsBlankValue(TargetsPath, "A.B")));
    }

    // An old name that ends on the 1st minor of the member and one that ends past a minor of 9 parse, because the ends compare as numbers.
    [TestCase(""" "from": "4.7", "oldName": "Old", "oldNameUntil": "4.7" """)]
    [TestCase(""" "from": "4.9", "oldName": "Old", "oldNameUntil": "4.10" """)]
    public void Read_GoodOldNameRange_Parses(string member)
    {
        Assert.That(Read($$"""{ "A": { "$type": "layout", "members": { "B": { {{member}} } } } }""").IsOk, Is.True);
    }

    // Only a member can have an old name, so the keys on a target fail the parse.
    [TestCase(""" "A": { "$type": "layout", "oldName": "Old", "oldNameUntil": "4.7", "members": {} } """)]
    [TestCase(""" "A": { "$type": "global", "oldName": "Old", "oldNameUntil": "4.7" } """)]
    public void Read_OldNameOnATarget_FailsWithThePath(string target)
    {
        Assert.That(Read($$"""{ {{target}} }""").UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // A comment and a trailing comma aren't JSON, so a hand-edited file fails with its path.
    [TestCase("""{ /* comment */ "A": { "$type": "global" } }""")]
    [TestCase("""{ "A": { "$type": "global" }, }""")]
    [TestCase("""{ "A": { "$type": "global", } }""")]
    public void Read_CommentOrTrailingComma_FailsWithThePath(string json)
    {
        Assert.That(Read(json).UnwrapErr().Message, Does.Contain(TargetsPath));
    }

    // A constructed range has to lie inside the range of its global, because the measurement never reaches a version outside it.
    [TestCase("4.9", null)]
    [TestCase(null, "4.9")]
    public void Read_ConstructedRangeOutsideItsGlobal_FailsWithBothRanges(string? from, string? until)
    {
        string ends = from is null ? $"\"until\": \"{until}\"" : $"\"from\": \"{from}\"";
        string json = $$"""{ "A": { "$type": "global", {{ends}}, "constructedType": "T", "constructedFrom": "4.8", "constructedUntil": "4.10" } }""";

        Assert.That(
            Read(json).UnwrapErr().Message,
            Is.EqualTo(UnrealSR.FormatTargetsConstructedOutside(TargetsPath, "A", "4.8", "4.10", from, until)));
    }

    // A constructed range on the ends of its global or inside it parses.
    [TestCase(""" "from": "4.8", "until": "4.10", "constructedType": "T", "constructedFrom": "4.8", "constructedUntil": "4.10" """)]
    [TestCase(""" "from": "4.0", "constructedType": "T", "constructedFrom": "4.8", "constructedUntil": "4.10" """)]
    public void Read_ConstructedRangeInsideItsGlobal_Parses(string keys)
    {
        Assert.That(Read($$"""{ "A": { "$type": "global", {{keys}} } }""").IsOk, Is.True);
    }

    // A constant whose report key is a member or the size of a layout fails, because the report keeps 1 value per key.
    [TestCase("Scope::Value", "Scope.Value")]
    [TestCase("Scope::sizeof", "Scope.sizeof")]
    public void Read_ConstantKeyOfALayoutMember_FailsWithTheKey(string constant, string key)
    {
        string json = $$"""{ "Scope": { "$type": "layout", "members": { "Value": {} } }, "{{constant}}": { "$type": "constant" } }""";

        Assert.That(Read(json).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatTargetsReportKeyTaken(TargetsPath, constant, key)));
    }

    // A member called sizeof fails, because the report lists the size of the type under that name.
    [Test]
    public void Read_MemberCalledSizeof_FailsWithTheMember()
    {
        string json = """{ "A": { "$type": "layout", "members": { "sizeof": {} } } }""";

        Assert.That(Read(json).UnwrapErr().Message, Is.EqualTo(UnrealSR.FormatTargetsMemberReserved(TargetsPath, "A.sizeof")));
    }

    // A long list reads whole, because the checks run once over each target.
    [Test]
    public void Read_FiftyThousandTargets_ReadsAll()
    {
        var targets = Enumerable.Range(0, 50_000).Select(static index => $"\"G{index}\": {{ \"$type\": \"global\" }}");

        Assert.That(Read($"{{ {string.Join(", ", targets)} }}").Unwrap(), Has.Count.EqualTo(50_000));
    }

    private static Result<IReadOnlyDictionary<string, UnrealTarget>> Read(string json)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        return UnrealTargets.Read(stream, TargetsPath);
    }
}
