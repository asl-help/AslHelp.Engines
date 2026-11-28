using AslHelp.Engines.Profiles;
using AslHelp.Memory;
using NUnit.Framework;
using static AslHelp.Engines.Unreal.Tests.FakeUdkGame;
using static AslHelp.Engines.Unreal.Tests.JsonEdits;

namespace AslHelp.Engines.Unreal.Tests;

public sealed class UnrealGameUdkCatalogTests
{
    private const string Key = "win-x86-shipping";
    private const string MissingRoute = """{ "2010-09": [{ "forward": "D1 D2 D3 ????????", "offset": 3 }, { "read": "global" }] }""";

    private FakeUdkGame _fakeGame = null!;

    [SetUp]
    public void SetUp()
    {
        _fakeGame = new FakeUdkGame();
    }

    // The game keeps the profile of the picked document and the selection it attached with.
    [Test]
    public void Attach_Selection_SetsProfileAndSelection()
    {
        var catalog = Catalog(Document);
        var selection = catalog.Select(Target("2010-09"), ProfileGuessHandling.Disallow).Unwrap();

        var game = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, selection).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(game.Profile, Is.SameAs(catalog.Entries[0].Profile));
            Assert.That(game.Selection, Is.SameAs(selection));
            Assert.That(selection.Match, Is.EqualTo(ProfileMatch.Listed));
            Assert.That(game.GWorld.Path.Read<Address>().Unwrap(), Is.EqualTo(_fakeGame.World));
        }
    }

    // A later release still attaches when the caller allows a guess and the game keeps the warning for the plugin to show.
    [Test]
    public void Attach_GuessedSelection_KeepsTheWarningOnTheGame()
    {
        var selection = Catalog(Document).Select(Target("2010-10"), ProfileGuessHandling.Allow).Unwrap();

        var game = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, selection).Unwrap();

        Assert.That(game.Selection!.Warning, Is.EqualTo($"No profile lists unreal3 2010-10 {Key}. Using 2010-09, the nearest lower version."));
    }

    // A target below every document fails with the engine, the version and the key.
    [Test]
    public void Select_TargetNoDocumentFits_FailsWithTheVersion()
    {
        Assert.That(
            Catalog(Document).Select(Target("3.5860"), ProfileGuessHandling.Allow).UnwrapErr().Message,
            Is.EqualTo($"No profile fits unreal3 3.5860 {Key}. Add a profile that lists 3.5860."));
    }

    // A picked document whose routes don't find a global fails with the picked version, the target and the cause.
    [Test]
    public void Attach_DocumentWhoseRoutesMiss_FailsWithThePickedVersionAndTheTarget()
    {
        var catalog = Catalog(Set(Document, MissingRoute, "globals", "GObjObjects"));

        string message = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, Select(catalog, "2010-09")).UnwrapErr().Message;

        Assert.That(
            message,
            Does.StartWith($"The 2010-09 profile doesn't fit the game (unreal3 2010-09 {Key}). Can't find GObjObjects in {ModuleName}. ")
                .And.EndWith("Check the version and the configuration."));
    }

    // Routes that find every global still fail when name 0 doesn't read None, because the names of another build are somewhere else.
    [Test]
    public void Attach_NameZeroIsNotNone_FailsWithThePickedVersionAndTheTarget()
    {
        var catalog = Catalog(Set(Document, "\"0x11\"", "offsets", "FNameEntry", "Name"));

        Assert.That(
            UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, Select(catalog, "2010-09")).UnwrapErr().Message,
            Is.EqualTo(
                $"The 2010-09 profile doesn't fit the game (unreal3 2010-09 {Key}). Name 0 reads 'one' and not None. "
                + "Check the version and the configuration."));
    }

    // A name table that's still a null pointer fails without the misfit, so a plugin can attach again after the game starts.
    [Test]
    public void Attach_NameTableStillNull_FailsWithTheRetryHint()
    {
        _fakeGame.Memory.WritePointer(_fakeGame.NameTableGlobal, Address.Zero);

        string message = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, Select(Catalog(Document), "2010-09")).UnwrapErr().Message;

        Assert.That(
            message,
            Does.StartWith($"Name 0 of unreal3 2010-09 {Key} doesn't read yet. ")
                .And.EndWith("Attach again a little later. Check the version and the configuration if it keeps failing."));
    }

    // The profile overload doesn't select or check names, so it attaches with names that don't read and leaves Selection null.
    [Test]
    public void Attach_ExplicitProfile_LeavesSelectionNull()
    {
        var profile = UnrealProfile.Parse(Set(Document, "\"0x11\"", "offsets", "FNameEntry", "Name")).Unwrap();

        var game = UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, profile).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(game.Profile, Is.SameAs(profile));
            Assert.That(game.Selection, Is.Null);
        }
    }

    // Attach without a target detects first, so a module without a file path fails with the module name.
    [Test]
    public void Attach_CatalogAndModuleWithoutAPath_FailsWithTheModuleName()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, UnrealProfile.CreateCatalog(), ProfileGuessHandling.Allow).UnwrapErr().Message,
                Is.EqualTo($"The path of module '{ModuleName}' isn't known, so its file can't be read."));
            Assert.That(
                UnrealGame.Attach(_fakeGame.Memory, _fakeGame.Module, ProfileGuessHandling.Allow).UnwrapErr().Message,
                Is.EqualTo($"The path of module '{ModuleName}' isn't known, so its file can't be read."));
        }
    }

    private static ProfileCatalog<UnrealProfile> Catalog(string document)
    {
        ProfileCatalog<UnrealProfile> catalog = new(UnrealProfile.Map, UnrealTarget.ParseVersion);
        _ = catalog.Add("file.json", document).Unwrap();

        return catalog;
    }

    private static ProfileSelection<UnrealProfile> Select(ProfileCatalog<UnrealProfile> catalog, string version)
    {
        return catalog.Select(Target(version), ProfileGuessHandling.Disallow).Unwrap();
    }

    private static ProfileTarget Target(string version)
    {
        return new ProfileTarget(UnrealTarget.Unreal3, UnrealTarget.ParseVersion(version)!, Key);
    }
}
