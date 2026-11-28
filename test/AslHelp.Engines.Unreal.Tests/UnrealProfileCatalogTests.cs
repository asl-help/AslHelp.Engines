using AslHelp.Engines.Profiles;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealProfileCatalogTests
{
    // Every shipped document maps, 56 for Unreal Engine 4 and 5 and 4 for UDK, each from the Profiles folder of the assembly.
    [Test]
    public void CreateCatalog_ShippedDocuments_LoadsAll60ThroughTheirMapper()
    {
        var clock = Stopwatch.StartNew();
        var entries = UnrealProfile.CreateCatalog().Entries;
        TestContext.Out.WriteLine($"catalog: {clock.ElapsedMilliseconds} ms");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries.Count(static entry => entry.Engine == UnrealTarget.Unreal4), Is.EqualTo(56));
            Assert.That(entries.Count(static entry => entry.Engine == UnrealTarget.Unreal3), Is.EqualTo(4));
            Assert.That(entries.Select(static entry => entry.Source), Has.All.StartWith("Profiles/").And.All.EndWith(".json"));
            Assert.That(entries.Select(static entry => entry.Profile.Engine), Is.EqualTo(entries.Select(static entry => entry.Engine)));
        }
    }

    // A version listed by 2 documents of 1 key would make the pick depend on the order of the files.
    [Test]
    public void CreateCatalog_ShippedDocuments_ListEachEngineKeyAndVersionOnce()
    {
        var listings = UnrealProfile.CreateCatalog().Entries
            .SelectMany(static entry => entry.Versions.Select(version => (entry.Engine, entry.Key, version)));

        Assert.That(listings, Is.Unique);
    }

    // The documents check out with CRLF on every machine, so every build ships the bytes the measure tool wrote.
    [Test]
    public void CreateCatalog_ShippedDocuments_EmbedTheMeasuredCrlfBytes()
    {
        var assembly = typeof(UnrealProfile).Assembly;

        foreach (string name in assembly.GetManifestResourceNames().Where(static name => name.StartsWith("Profiles/", StringComparison.Ordinal)))
        {
            using StreamReader reader = new(assembly.GetManifestResourceStream(name)!);
            string text = reader.ReadToEnd();

            int pairs = text.Split(["\r\n"], StringSplitOptions.None).Length - 1;

            Assert.That((text.Count(static c => c == '\r'), text.Count(static c => c == '\n')), Is.EqualTo((pairs, pairs)), name);
        }
    }

    // The 4.19.2 x64 shipping player uses the group of 4.18.3, which lists 4.19.2.
    [Test]
    public void Select_Shipped4192X64Shipping_TakesTheGroupOf4183()
    {
        var selection = UnrealProfile.CreateCatalog().Select(Target("4.19.2", "win-x64-shipping"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry.Source, Is.EqualTo("Profiles/unreal4-4.18.3-win-x64-shipping.json"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
        }
    }

    // x86 players stop at 4.27 and the fallback stays in the major, so 5.0.3 on x86 fails instead of taking 4.27.2.
    [Test]
    public void Select_Shipped503X86_Fails()
    {
        Assert.That(
            UnrealProfile.CreateCatalog().Select(Target("5.0.3", "win-x86-shipping"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo("No profile fits unreal4 5.0.3 win-x86-shipping. Add a profile that lists 5.0.3."));
    }

    // A version past the newest x64 document of its major takes that document with a warning when the caller allows a guess.
    [Test]
    public void Select_Shipped590X64_FallsBackTo584WithAWarning()
    {
        var selection = UnrealProfile.CreateCatalog().Select(Target("5.9.0", "win-x64-shipping"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("5.8.4"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.LowerVersion));
            Assert.That(selection.Warning, Is.EqualTo("No profile lists unreal4 5.9.0 win-x64-shipping. Using 5.8.4, the nearest lower version."));
        }
    }

    // A caller that doesn't allow a guess gets the version it would take instead of a selection.
    [Test]
    public void Select_Shipped590X64WithDisallow_FailsWith584AsTheNearest()
    {
        Assert.That(
            UnrealProfile.CreateCatalog().Select(Target("5.9.0", "win-x64-shipping"), ProfileGuessHandling.Disallow).UnwrapErr().Message,
            Is.EqualTo("No profile lists unreal4 5.9.0 win-x64-shipping. Pass ProfileGuessHandling.Allow to use 5.8.4, the nearest listed version."));
    }

    // A file stamp of 5.8 without a patch takes the listed 5.8.4 as its line, so it doesn't come with a warning.
    [Test]
    public void Select_Shipped58X64WithoutAPatch_TakesTheListedPatchWithoutAWarning()
    {
        var selection = UnrealProfile.CreateCatalog().Select(Target("5.8", "win-x64-shipping"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("5.8.4"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
            Assert.That(selection.Warning, Is.Null);
        }
    }

    // x64 from 4.0 to 4.7 only has development players, so a shipping key below 4.8 fails with the version and the key.
    [Test]
    public void Select_Shipped451X64Shipping_Fails()
    {
        Assert.That(
            UnrealProfile.CreateCatalog().Select(Target("4.5.1", "win-x64-shipping"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo("No profile fits unreal4 4.5.1 win-x64-shipping. Add a profile that lists 4.5.1."));
    }

    // The UDK documents don't have a key, so each fits the key that detection builds for the UDK players of its group.
    [TestCase("2010-07-BETA", "unreal3-2010-06-BETA")]
    [TestCase("2010-09", "unreal3-2010-08-BETA")]
    [TestCase("2011-03-BETA2", "unreal3-2010-08-BETA")]
    [TestCase("2011-05-BETA", "unreal3-2011-05-BETA")]
    [TestCase("2012-01-BETA", "unreal3-2011-07-BETA")]
    [TestCase("2015-02", "unreal3-2011-07-BETA")]
    public void Select_ShippedUdkReleaseX86Shipping_TakesItsDocument(string release, string document)
    {
        var selection = UnrealProfile.CreateCatalog()
            .Select(new ProfileTarget(UnrealTarget.Unreal3, UnrealTarget.ParseVersion(release)!, "win-x86-shipping"), ProfileGuessHandling.Disallow)
            .Unwrap();

        Assert.That(selection.Entry.Source, Is.EqualTo($"Profiles/{document}.json"));
    }

    // A build without a measured release takes the nearest lower UDK document when guessing is allowed, across the years of the releases.
    [TestCase("3.8225", "unreal3-2010-08-BETA")]
    [TestCase("3.13000", "unreal3-2011-07-BETA")]
    public void Select_UnmeasuredUnreal3BuildWithGuessing_TakesTheNearestLowerDocument(string version, string document)
    {
        var selection = UnrealProfile.CreateCatalog()
            .Select(new ProfileTarget(UnrealTarget.Unreal3, UnrealTarget.ParseVersion(version)!, "win-x86-shipping"), ProfileGuessHandling.Allow)
            .Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry.Source, Is.EqualTo($"Profiles/{document}.json"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.LowerVersion));
        }
    }

    // A document a consumer adds after the shipped documents wins for the versions it lists.
    [Test]
    public void CreateCatalog_ConsumerDocumentFor4192_WinsOverTheShippedDocument()
    {
        var catalog = UnrealProfile.CreateCatalog();
        string json = Set(Set(FakeGame.Document("uproperty", "indirect", "flat"), "\"win-x64-shipping\"", "key"), """["4.19.2"]""", "versions");
        var entry = catalog.Add("file.json", json).Unwrap();

        Assert.That(catalog.Select(Target("4.19.2", "win-x64-shipping"), ProfileGuessHandling.Disallow).Unwrap().Entry, Is.SameAs(entry));
    }

    private static ProfileTarget Target(string version, string key)
    {
        return new ProfileTarget(UnrealTarget.Unreal4, UnrealTarget.ParseVersion(version)!, key);
    }
}
