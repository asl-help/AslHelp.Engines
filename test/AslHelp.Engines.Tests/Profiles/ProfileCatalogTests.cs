using AslHelp.Engines.Profiles;
using NUnit.Framework;
using static AslHelp.Engines.Tests.Profiles.FakeProfile;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileCatalogTests
{
    private const string Key = "x64";

    // A version in the middle of a list picks that document without a warning.
    [Test]
    public void Select_ListedVersion_ReturnsTheEntryWithoutAWarning()
    {
        var catalog = Catalog(Document(Key, "1.0.0"), Document(Key, "2.0.0", "2.1.0", "2.2.0"));

        var selection = catalog.Select(Target("2.1.0"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(catalog.Entries[1]));
            Assert.That(selection.Version.Text, Is.EqualTo("2.1.0"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
            Assert.That(selection.Warning, Is.Null);
        }
    }

    // A document that lists the version wins over a document whose versions are below and above it.
    [Test]
    public void Select_VersionListedByOneGroupAndBetweenVersionsOfAnother_TakesTheListingGroup()
    {
        var catalog = Catalog(Document(Key, "1.0.0", "2.1.0"), Document(Key, "2.0.0", "2.2.0"));

        var selection = catalog.Select(Target("2.1.0"), ProfileGuessHandling.Disallow).Unwrap();

        Assert.That(selection.Entry, Is.SameAs(catalog.Entries[0]));
    }

    // The release type is a part of the version, so a document that lists the alpha doesn't list the final of that patch.
    [Test]
    public void Select_AlphaListedAndFinalTarget_FailsWithDisallow()
    {
        var catalog = Catalog(Document(Key, "2022.3.5a1"));

        Assert.That(
            catalog.Select(Target("2022.3.5f1"), ProfileGuessHandling.Disallow).UnwrapErr().Message,
            Is.EqualTo("No profile lists fake 2022.3.5f1 x64. Pass ProfileGuessHandling.Allow to use 2022.3.5a1, the nearest listed version."));
    }

    // A document that lists only a line covers every release of that line, alphas too, without a warning.
    [Test]
    public void Select_EntryThatListsOnlyALine_CoversEveryReleaseOfIt()
    {
        var catalog = Catalog(Document(Key, "2021.3"), Document(Key, "2022.1"));

        var alpha = catalog.Select(Target("2022.1.0a1"), ProfileGuessHandling.Disallow).Unwrap();
        var final = catalog.Select(Target("2021.3.45f1"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alpha.Version.Text, Is.EqualTo("2022.1"));
            Assert.That(alpha.Match, Is.EqualTo(ProfileMatch.Listed));
            Assert.That(final.Version.Text, Is.EqualTo("2021.3"));
            Assert.That(final.Warning, Is.Null);
        }
    }

    // A target with fewer parts takes the highest listed version that starts with it, without a warning.
    [TestCase("2017.4", "2017.4.30f1")]
    [TestCase("2018.1.0", "2018.1.0f1")]
    public void Select_TargetWithFewerParts_TakesTheHighestListedVersionOfItsLine(string target, string listed)
    {
        var catalog = Catalog(Document(Key, "2017.3.0f3", "2017.4.0f1", "2017.4.30f1"), Document(Key, "2018.1.0f1", "2018.2.0f1"));

        var selection = catalog.Select(Target(target), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo(listed));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
            Assert.That(selection.Warning, Is.Null);
        }
    }

    // A caller that doesn't allow a guess gets the target and the version a guess would take instead of a selection.
    [Test]
    public void Select_UnlistedPatchWithDisallow_FailsWithTheNearestVersion()
    {
        var catalog = Catalog(Document(Key, "4.27.0"), Document(Key, "4.27.4"));

        Assert.That(
            catalog.Select(Target("4.27.1"), ProfileGuessHandling.Disallow).UnwrapErr().Message,
            Is.EqualTo("No profile lists fake 4.27.1 x64. Pass ProfileGuessHandling.Allow to use 4.27.0, the nearest listed version."));
    }

    // An unlisted patch takes the nearest patch of its minor and the warning shows both versions.
    [Test]
    public void Select_UnlistedPatchWithAllow_TakesTheNearestPatchWithAWarning()
    {
        var catalog = Catalog(Document(Key, "4.27.0"), Document(Key, "4.27.4"));

        var selection = catalog.Select(Target("4.27.1"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("4.27.0"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.SameMinor));
            Assert.That(selection.Warning, Is.EqualTo("No profile lists fake 4.27.1 x64. Using 4.27.0 of the same minor."));
        }
    }

    // A patch right between 2 listed patches takes the lower patch.
    [Test]
    public void Select_PatchBetween2ListedPatches_TakesTheLowerPatch()
    {
        var catalog = Catalog(Document(Key, "3.4.3", "3.4.1"));

        Assert.That(catalog.Select(Target("3.4.2"), ProfileGuessHandling.Allow).Unwrap().Version.Text, Is.EqualTo("3.4.1"));
    }

    // An unlisted minor takes the highest version below it and the warning shows both versions.
    [Test]
    public void Select_UnlistedMinor_TakesTheHighestLowerVersionWithAWarning()
    {
        var catalog = Catalog(Document(Key, "5.7.4"), Document(Key, "5.8.4"), Document(Key, "6.0.0"));

        var selection = catalog.Select(Target("5.9.0"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("5.8.4"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.LowerVersion));
            Assert.That(selection.Warning, Is.EqualTo("No profile lists fake 5.9.0 x64. Using 5.8.4, the nearest lower version."));
        }
    }

    // A version below every listed version fails, because Select never takes a higher version outside the minor.
    [Test]
    public void Select_VersionBelowEveryListedVersion_FailsWithEngineVersionAndKey()
    {
        var catalog = Catalog(Document(Key, "4.8.3"));

        Assert.That(
            catalog.Select(Target("4.7.6"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo("No profile fits fake 4.7.6 x64. Add a profile that lists 4.7.6."));
    }

    // A version of a lower major isn't a fallback, because Select only guesses within the major of the target.
    [Test]
    public void Select_OnlyALowerMajorListed_Fails()
    {
        var catalog = Catalog(Document(Key, "1.9.0"));

        Assert.That(
            catalog.Select(Target("2.0.0"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo("No profile fits fake 2.0.0 x64. Add a profile that lists 2.0.0."));
    }

    // Documents of another key or another engine don't count, even when they list the version.
    [TestCase("fake", "x86")]
    [TestCase("other", Key)]
    public void Select_OtherKeyOrOtherEngine_Fails(string engine, string key)
    {
        var catalog = Catalog(Document(Key, "1.0.0"));

        Assert.That(catalog.Select(new ProfileTarget(engine, FakeVersion.Parse("1.0.0")!, key), ProfileGuessHandling.Allow).IsErr, Is.True);
    }

    // A document without a key fits every key of its engine.
    [TestCase("x64")]
    [TestCase("x86")]
    public void Select_EntryWithoutAKey_FitsEveryKey(string key)
    {
        var catalog = Catalog(Document(null, "1.0.0"));

        var selection = catalog.Select(new ProfileTarget(Engine, FakeVersion.Parse("1.0.0")!, key), ProfileGuessHandling.Disallow).Unwrap();

        Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
    }

    // A document added after another document for the same version wins, so a consumer document replaces a shipped document.
    [Test]
    public void Select_VersionListedByALaterEntry_TakesTheLaterEntry()
    {
        var catalog = Catalog(Document(Key, "1.0.0", "1.1.0"), Document(Key, "1.1.0"));

        Assert.That(catalog.Select(Target("1.1.0"), ProfileGuessHandling.Disallow).Unwrap().Entry, Is.SameAs(catalog.Entries[1]));
    }

    // A document that lists the version wins over a later document that only lists another patch of the minor.
    [Test]
    public void Select_LaterEntryListsOnlyTheMinor_TakesTheListingEntry()
    {
        var catalog = Catalog(Document(Key, "1.1.2"), Document(Key, "1.1.0"));

        var selection = catalog.Select(Target("1.1.2"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(catalog.Entries[0]));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
        }
    }

    private static ProfileCatalog<FakeProfile> Catalog(params string[] documents)
    {
        ProfileCatalog<FakeProfile> catalog = new(Map, FakeVersion.Parse);

        foreach (string document in documents)
        {
            _ = catalog.Add("file.json", document).Unwrap();
        }

        return catalog;
    }

    private static ProfileTarget Target(string version)
    {
        return new ProfileTarget(Engine, FakeVersion.Parse(version)!, Key);
    }
}
