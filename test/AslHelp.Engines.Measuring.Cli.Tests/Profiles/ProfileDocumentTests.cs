using AslHelp.Engines.Measuring.Cli.Profiles;
using AslHelp.Engines.Measuring.Cli.Routes;
using AslHelp.Engines.Profiles;
using AslHelp.Engines.Unreal;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static AslHelp.Engines.Measuring.Cli.Tests.Profiles.TestGroups;

namespace AslHelp.Engines.Measuring.Cli.Tests.Profiles;

public sealed class ProfileDocumentTests
{
    private static readonly string[] _bareVersions = ["1.0", "2.0"];
    private static readonly string[] _first = ["1.0"];
    private static readonly string[] _global = ["Global"];
    private static readonly string[] _parts = ["engine", "key", "versions", "shapes", "offsets", "globals"];
    private static readonly string[] _reversed = ["2.0", "1.0"];
    private static readonly string[] _unkeyedVersions = ["2010-09", "2013-07"];

    // A group with a key and shapes writes every part in order, with the key cut off its versions.
    [Test]
    public void Create_GroupWithKeyAndShapes_WritesEveryPart()
    {
        var group = Group("win-x64", ["1.0-win-x64", "2.0-win-x64"], ("Global", RoutesOf(Route("1.0-win-x64")))) with
        {
            Shapes = new Dictionary<string, string>(StringComparer.Ordinal) { ["Type"] = "flat" },
        };

        var json = Serialize(ProfileDocument.Create("engine", group).Unwrap());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json.EnumerateObject().Select(static part => part.Name), Is.EqualTo(_parts));
            Assert.That(json.GetProperty("key").GetString(), Is.EqualTo("win-x64"));
            Assert.That(json.GetProperty("versions").EnumerateArray().Select(static version => version.GetString()), Is.EqualTo(_bareVersions));
            Assert.That(json.GetProperty("shapes").GetProperty("Type").GetString(), Is.EqualTo("flat"));
            Assert.That(json.GetProperty("offsets").GetProperty("Type").GetProperty("Member").GetString(), Is.EqualTo("0x28"));
        }
    }

    // A group without a key and shapes leaves both parts out and keeps its versions.
    [Test]
    public void Create_GroupWithoutKeyAndShapes_LeavesBothOut()
    {
        var json = Serialize(ProfileDocument.Create("engine", Group(null, [.. _unkeyedVersions], ("Global", RoutesOf(Route("2010-09"))))).Unwrap());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json.TryGetProperty("key", out _), Is.False);
            Assert.That(json.TryGetProperty("shapes", out _), Is.False);
            Assert.That(json.GetProperty("versions").EnumerateArray().Select(static version => version.GetString()), Is.EqualTo(_unkeyedVersions));
        }
    }

    // Only a key at the end of a version is cut, so a version with the key in the middle stays.
    [Test]
    public void Create_VersionWithoutTheKeySuffix_KeepsTheVersion()
    {
        string[] versions = ["1.0", "2.0-x64-beta"];

        var document = ProfileDocument.Create("engine", Group("x64", [.. versions])).Unwrap();

        Assert.That(document.Versions, Is.EqualTo(versions));
    }

    // Each route becomes 1 locator route in report order, under the bare 1st version it covers.
    [Test]
    public void Create_TwoRoutes_WritesEachUnderItsFirstBareVersion()
    {
        var group = Group(
            "x64",
            ["1.0-x64", "2.0-x64", "3.0-x64"],
            ("Global", RoutesOf(Route("2.0-x64", "3.0-x64") with { Forward = "A1 ????????", Offset = 1 }, Route("1.0-x64"))));

        var global = ProfileDocument.Create("engine", group).Unwrap().Globals["Global"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(global.Keys, Is.EqualTo(_reversed));
            Assert.That(global["2.0"], Is.EqualTo(new ProfileStep[] { new("A1 ????????", 1, null), new(null, null, "global") }));
            Assert.That(global["1.0"], Is.EqualTo(new ProfileStep[] { new(Forward, 2, null), new(null, null, "global") }));
        }
    }

    // A global that some versions don't reach still gets the routes it has.
    [Test]
    public void Create_PartlyUncoveredGlobal_KeepsItsRoutes()
    {
        GlobalRoutes routes = new([Route("1.0")], new Dictionary<string, string>(StringComparer.Ordinal) { ["2.0"] = "reason" });

        var document = ProfileDocument.Create("engine", Group(null, ["1.0", "2.0"], ("Global", routes))).Unwrap();

        Assert.That(document.Globals["Global"].Keys, Is.EqualTo(_first));
    }

    // A global without a route doesn't get a locator.
    [Test]
    public void Create_GlobalWithoutRoutes_LeavesItOut()
    {
        GlobalRoutes missing = new([], new Dictionary<string, string>(StringComparer.Ordinal) { ["1.0"] = "reason" });

        var document = ProfileDocument.Create("engine", Group(null, ["1.0"], ("Missing", missing), ("Global", RoutesOf(Route("1.0"))))).Unwrap();

        Assert.That(document.Globals.Keys, Is.EqualTo(_global));
    }

    // 2 routes of 1 global that get the same name once the key is cut off fail with the global, the name and the group.
    [Test]
    public void Create_TwoRoutesSameName_ReturnsTheError()
    {
        var group = Group("a", ["1.0", "1.0-a"], ("Global", RoutesOf(Route("1.0-a"), Route("1.0"))));

        var document = ProfileDocument.Create("engine", group);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.TryUnwrapErr(out var error), Is.True);
            Assert.That(error!.Message, Does.Contain("Global").And.Contain("'1.0'").And.Contain("[1.0, 1.0-a]"));
        }
    }

    // The file name of a keyed group has the key after the 1st version without the key.
    [Test]
    public void FileName_GroupWithKey_EndsWithTheKey()
    {
        var document = ProfileDocument.Create("engine", Group("win-x64-shipping", ["4.18.3-win-x64-shipping", "4.19.2-win-x64-shipping"])).Unwrap();

        Assert.That(document.FileName, Is.EqualTo("engine-4.18.3-win-x64-shipping.json"));
    }

    // The file name of a group without a key stops at its 1st version.
    [Test]
    public void FileName_GroupWithoutKey_EndsWithTheFirstVersion()
    {
        var document = ProfileDocument.Create("engine", Group(null, [.. _unkeyedVersions])).Unwrap();

        Assert.That(document.FileName, Is.EqualTo("engine-2010-09.json"));
    }

    // The library reader fails a document with a key it doesn't read, so every part the writer writes must be a key the reader or a mapper reads.
    [Test]
    public void Create_WrittenDocument_ReadsWithoutUnknownKeys()
    {
        var group = Group("win-x64", ["1.0-win-x64"], ("Global", RoutesOf(Route("1.0-win-x64")))) with
        {
            Shapes = new Dictionary<string, string>(StringComparer.Ordinal) { ["Type"] = "flat" },
        };
        string json = JsonSerializer.Serialize(ProfileDocument.Create("engine", group).Unwrap(), ProfileDocumentContext.Default.ProfileDocument);

        var read = ProfileReader.Read(
            json,
            static reader =>
            {
                _ = reader.ReadObject("shapes");
                return Result.Ok(reader.Engine);
            },
            UnrealTarget.ParseVersion);

        Assert.That(read.Unwrap(), Is.EqualTo("engine"));
    }

    private static JsonElement Serialize(ProfileDocument document)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ProfileDocumentContext.Default.ProfileDocument));

        return json.RootElement.Clone();
    }
}
