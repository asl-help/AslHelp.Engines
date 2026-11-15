using AslHelp.Engines.Measuring.Cli.Targets;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;

namespace AslHelp.Engines.Measuring.Cli.Tests.Targets;

public sealed class TargetsFileTests
{
    private const string TargetsPath = "targets.example";

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

    // A file that another process keeps open without sharing fails as a targets file that does not read, with the path.
    [Test]
    public void ReadFile_OpenWithoutSharing_FailsAsUnreadable()
    {
        string path = Path.Combine(_folder.Root, "targets.json");
        File.WriteAllText(path, """{ "A": "a" }""");

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            string message = TargetsFile.ReadFile(path, TextTargetContext.Default.DictionaryStringString, Accept).UnwrapErr().Message;

            Assert.That(message, Does.StartWith(SR.FormatFileUnreadable(path, "")));
        }
    }

    // A path that does not exist fails as a missing file and not as a failed read, in a folder that exists and in one that does not.
    [TestCase("targets.json")]
    [TestCase("missing/targets.json")]
    public void ReadFile_MissingFile_FailsAsMissing(string relativePath)
    {
        string path = Path.Combine(_folder.Root, relativePath);

        string message = TargetsFile.ReadFile(path, TextTargetContext.Default.DictionaryStringString, Accept).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatTargetsNotFound(path)));
    }

    // A folder at the path exists and does not open, so it fails as a failed read and not as a missing file.
    [Test]
    public void ReadFile_PathIsAFolder_FailsAsUnreadable()
    {
        string message = TargetsFile.ReadFile(_folder.Root, TextTargetContext.Default.DictionaryStringString, Accept).UnwrapErr().Message;

        Assert.That(message, Does.StartWith(SR.FormatFileUnreadable(_folder.Root, "")));
    }

    // A missing resource fails with its name.
    [Test]
    public void ReadEmbedded_MissingResource_FailsWithTheName()
    {
        string message = TargetsFile.ReadEmbedded("missing.json", TextTargetContext.Default.DictionaryStringString, Accept).UnwrapErr().Message;

        Assert.That(message, Is.EqualTo(SR.FormatTargetsNotEmbedded("missing.json")));
    }

    // The common checks run before the rules of the engine, so a null target never reaches them.
    [TestCase("""{ "A": null }""", "'targets.example': the target 'A' is null.")]
    [TestCase("""{ "": "a" }""", "'targets.example': a target has a blank name.")]
    [TestCase("""{ " ": "a" }""", "'targets.example': a target has a blank name.")]
    [TestCase("{}", "'targets.example' doesn't have any targets.")]
    public void Read_CommonRuleBroken_FailsBeforeTheEngine(string json, string expected)
    {
        bool validated = false;

        string message = Read(json, (_, _) =>
        {
            validated = true;

            return Result.Ok();
        }).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Is.EqualTo(expected));
            Assert.That(validated, Is.False);
        }
    }

    // A value of the wrong type and a file cut off after its 1st byte fail the parse, so the rules of the engine never run.
    [TestCase("""{ "A": 1 }""")]
    [TestCase("[")]
    public void Read_NotAnObjectOfTargets_FailsAsInvalid(string json)
    {
        bool validated = false;

        string message = Read(json, (_, _) =>
        {
            validated = true;

            return Result.Ok();
        }).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.StartWith(SR.FormatTargetsInvalid(TargetsPath, "")));
            Assert.That(validated, Is.False);
        }
    }

    // The rules of the engine get the targets and the path and their failure is the result.
    [Test]
    public void Read_EngineRuleBroken_ReturnsItsFailure()
    {
        string? seenPath = null;

        string message = Read("""{ "A": "a" }""", (_, path) =>
        {
            seenPath = path;

            return new TextError("The engine refuses it.");
        }).UnwrapErr().Message;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Is.EqualTo("The engine refuses it."));
            Assert.That(seenPath, Is.EqualTo(TargetsPath));
        }
    }

    private static Result Accept(IReadOnlyDictionary<string, string> targets, string path)
    {
        return Result.Ok();
    }

    private static Result<IReadOnlyDictionary<string, string>> Read(string json, Func<IReadOnlyDictionary<string, string>, string, Result> validate)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        return TargetsFile.Read(stream, TargetsPath, TextTargetContext.Default.DictionaryStringString, validate);
    }
}

// Reads targets that are plain text, because the reader is the same for every type of target.
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class TextTargetContext : JsonSerializerContext;
