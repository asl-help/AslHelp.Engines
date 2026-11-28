using AslHelp.Engines.Profiles;
using NUnit.Framework;
using System.IO;
using static AslHelp.Engines.Tests.Profiles.FakeProfile;

namespace AslHelp.Engines.Tests.Profiles;

public sealed class ProfileCatalogSelectEdgeTests
{
    private const string Key = "x64";

    private ProfileCatalog<FakeProfile> _catalog = null!;
    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = new ProfileCatalog<FakeProfile>(Map, FakeVersion.Parse);
        _folder = Path.Combine(Path.GetTempPath(), $"selection-{TestContext.CurrentContext.Test.ID}");
        _ = Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_folder, recursive: true);
    }

    // A catalog without documents fails with the target and doesn't throw.
    [Test]
    public void Select_EmptyCatalog_FailsWithTheTarget()
    {
        Assert.That(
            _catalog.Select(Target("1.0.0"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo("No profile fits fake 1.0.0 x64. Add a profile that lists 1.0.0."));
    }

    // A later document wins the lower version fallback too.
    [Test]
    public void Select_LowerVersionListedTwice_TakesTheLaterEntry()
    {
        Add(Document(Key, "1.0.0"), Document(Key, "1.0.0"));

        var selection = _catalog.Select(Target("1.1.0"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(_catalog.Entries[1]));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.LowerVersion));
        }
    }

    // A later document wins the same minor fallback too.
    [Test]
    public void Select_SameMinorPatchListedTwice_TakesTheLaterEntry()
    {
        Add(Document(Key, "1.0.0"), Document(Key, "1.0.0"));

        var selection = _catalog.Select(Target("1.0.3"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(_catalog.Entries[1]));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.SameMinor));
        }
    }

    // The fallback never goes above the minor of the target, so a catalog with only a higher minor or major fails.
    [TestCase("1.2.0")]
    [TestCase("2.0.0")]
    public void Select_OnlyHigherMinorOrMajorListed_Fails(string listed)
    {
        Add(Document(Key, listed));

        Assert.That(_catalog.Select(Target("1.1.0"), ProfileGuessHandling.Allow).IsErr, Is.True);
    }

    // The lower version fallback compares numbers, so 1.9 is below 1.10 and 1.10 is the highest version below 1.11.
    [Test]
    public void Select_LowerMinorsOf2Digits_ComparesNumbersAndNotText()
    {
        Add(Document(Key, "1.10.0", "1.9.0"));

        Assert.That(_catalog.Select(Target("1.11.0"), ProfileGuessHandling.Allow).Unwrap().Version.Text, Is.EqualTo("1.10.0"));
    }

    // The same minor fallback wins over a nearer lower version of another minor.
    [Test]
    public void Select_SameMinorAndLowerMinorListed_PrefersTheSameMinor()
    {
        Add(Document(Key, "1.0.9", "1.1.9"));

        var selection = _catalog.Select(Target("1.1.0"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("1.1.9"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.SameMinor));
        }
    }

    // A failed document doesn't count in the pick.
    [Test]
    public void Select_AfterAFailedAdd_IgnoresTheFailedDocument()
    {
        string failed = """{ "engine": "fake", "key": "x64", "versions": ["1.2.0"], "offsets": { "Thing": { "Speed": "0xZZ" } }, "globals": {} }""";
        Add(Document(Key, "1.0.0"));
        _ = _catalog.Add("file.json", failed);

        Assert.That(_catalog.Select(Target("1.2.0"), ProfileGuessHandling.Allow).Unwrap().Match, Is.EqualTo(ProfileMatch.LowerVersion));
    }

    // A document that lists the same parts wins over a line, even when the line comes from a later document.
    [Test]
    public void Select_ExactAndLineBothListed_TakesTheExactEntry()
    {
        Add(Document(Key, "1.2.3"), Document(Key, "1.2"));

        var selection = _catalog.Select(Target("1.2.3"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(_catalog.Entries[0]));
            Assert.That(selection.Version.Text, Is.EqualTo("1.2.3"));
        }
    }

    // The longest listed line is the nearest one, so 1.2.3 wins over 1.2 for 1.2.3.1.
    [Test]
    public void Select_2ListedLinesCoverTheTarget_TakesTheLongerLine()
    {
        Add(Document(Key, "1.2.3"), Document(Key, "1.2"));

        Assert.That(_catalog.Select(Target("1.2.3.1"), ProfileGuessHandling.Disallow).Unwrap().Version.Text, Is.EqualTo("1.2.3"));
    }

    // Versions of the target line in 2 documents can have 2 layouts, so the highest of them is a guess with a warning.
    [Test]
    public void Select_TargetLineSplitOver2EntriesWithAllow_TakesTheHighestWithAWarning()
    {
        Add(Document(Key, "5.8.4"), Document(Key, "5.8.0"));

        var selection = _catalog.Select(Target("5.8"), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(_catalog.Entries[0]));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.SameMinor));
            Assert.That(selection.Warning, Is.EqualTo("No profile lists fake 5.8 x64. Using 5.8.4 of the same minor."));
        }
    }

    // A target line split over 2 documents doesn't pass Disallow, also when the versions differ only by the release type.
    [Test]
    public void Select_TargetLineSplitOver2EntriesWithDisallow_FailsWithTheHighest()
    {
        Add(Document(Key, "2022.3.5a1"), Document(Key, "2022.3.5f1"));

        Assert.That(
            _catalog.Select(Target("2022.3.5"), ProfileGuessHandling.Disallow).UnwrapErr().Message,
            Is.EqualTo("No profile lists fake 2022.3.5 x64. Pass ProfileGuessHandling.Allow to use 2022.3.5f1, the nearest listed version."));
    }

    // A later document wins the pick of the highest version of a line too.
    [Test]
    public void Select_TargetLineListedBy2Entries_TakesTheLaterEntry()
    {
        Add(Document(Key, "1.2.4"), Document(Key, "1.2.4"));

        var selection = _catalog.Select(Target("1.2"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Entry, Is.SameAs(_catalog.Entries[1]));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
        }
    }

    // A guess stays in the line of the target, so a1 takes the listed a2 above it over 6000.5.0f1 of a lower line.
    [TestCase("6000.7.0a4", "6000.7.0a2")]
    [TestCase("6000.7.0a6", "6000.7.0a5")]
    [TestCase("6000.7.0a1", "6000.7.0a2")]
    public void Select_AlphaBetweenListedAlphasWithAllow_TakesTheNearestOfTheLine(string target, string listed)
    {
        Add(Document(Key, "6000.5.0f1", "6000.7.0a2", "6000.7.0a5"));

        var selection = _catalog.Select(Target(target), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo(listed));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.SameMinor));
        }
    }

    // A final below the listed final of its line takes that final with a warning and the listed final takes it without a warning.
    [TestCase("5.3.0f2", ProfileMatch.SameMinor)]
    [TestCase("5.3.0f4", ProfileMatch.Listed)]
    public void Select_FinalAtOrBelowTheListedFinalWithAllow_TakesTheListedFinalOfTheLine(string target, ProfileMatch match)
    {
        Add(Document(Key, "5.0.0f4", "5.3.0f4"));

        var selection = _catalog.Select(Target(target), ProfileGuessHandling.Allow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("5.3.0f4"));
            Assert.That(selection.Match, Is.EqualTo(match));
        }
    }

    // A guess never takes a version with another 1st part, so a target between lines of other majors fails even with Allow.
    [Test]
    public void Select_TargetBetweenLinesOfOtherMajorsWithAllow_Fails()
    {
        Add(Document(Key, "2017.2", "2021.3", "2018.4"));

        Assert.That(
            _catalog.Select(Target("2020.1.18f1"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo("No profile fits fake 2020.1.18f1 x64. Add a profile that lists 2020.1.18f1."));
    }

    // A listed version that starts with the whole target is nearer than a listed line, so 2022.3.5 takes 2022.3.5f1 over a later 2022.3.
    [Test]
    public void Select_TargetWithoutATypeAndAShorterLineListed_TakesTheVersionOfTheTargetLine()
    {
        Add(Document(Key, "2022.3.5f1"), Document(Key, "2022.3"));

        var selection = _catalog.Select(Target("2022.3.5"), ProfileGuessHandling.Disallow).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selection.Version.Text, Is.EqualTo("2022.3.5f1"));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
        }
    }

    // Disallow fails like Allow when there's no version to guess, so the message doesn't have a nearest version.
    [Test]
    public void Select_NothingOfTheMajorWithDisallow_FailsWithTheTarget()
    {
        Add(Document(Key, "2.0.0"));

        Assert.That(
            _catalog.Select(Target("1.0.0"), ProfileGuessHandling.Disallow).UnwrapErr().Message,
            Is.EqualTo("No profile fits fake 1.0.0 x64. Add a profile that lists 1.0.0."));
    }

    // A value outside the enum doesn't guess, so a cast number can't turn the guess on.
    [Test]
    public void Select_GuessHandlingOutsideTheEnum_FailsLikeDisallow()
    {
        Add(Document(Key, "1.0.0"));

        Assert.That(
            _catalog.Select(Target("1.0.3"), (ProfileGuessHandling)2).UnwrapErr().Message,
            Does.StartWith("No profile lists fake 1.0.3 x64."));
    }

    // A failed embedded batch leaves the documents added before it.
    [Test]
    public void AddEmbedded_BrokenResourceAfterEarlierAdds_KeepsTheEarlierEntries()
    {
        Add(Document(Key, "1.0.0"));

        _ = _catalog.AddEmbedded(typeof(FakeProfile).Assembly, "Broken/");

        Assert.That(_catalog.Entries, Has.Count.EqualTo(1));
    }

    // A path with letters outside ASCII reads and stays the source as given.
    [Test]
    public void AddFile_PathWithNonAsciiLetters_ReadsTheDocument()
    {
        string path = Path.Combine(_folder, "pläyer プロ", "file.json");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Document(Key, "1.0.0"));

        Assert.That(_catalog.AddFile(path).Unwrap().Source, Is.EqualTo(path));
    }

    // A folder, an empty path or a path with a NUL fails with the path instead of throwing.
    [TestCase("")]
    [TestCase("folder")]
    [TestCase("file\0.json")]
    public void AddFile_PathThatIsNotAFile_FailsWithThePath(string name)
    {
        // Join the path by hand, because Path.Combine on .NET Framework throws for the NUL.
        string path = string.IsNullOrEmpty(name) ? "" : $"{_folder}{Path.DirectorySeparatorChar}{name}";

        if (name == "folder")
        {
            _ = Directory.CreateDirectory(path);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_catalog.AddFile(path).UnwrapErr().Message, Does.StartWith($"Can't read the profile {path}. "));
            Assert.That(_catalog.Entries, Is.Empty);
        }
    }

    private void Add(params string[] documents)
    {
        foreach (string document in documents)
        {
            _ = _catalog.Add("file.json", document).Unwrap();
        }
    }

    private static ProfileTarget Target(string version)
    {
        return new ProfileTarget(Engine, FakeVersion.Parse(version)!, Key);
    }
}
