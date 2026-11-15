using AslHelp.Engines.Fixtures.Cli.Unreal4;
using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed partial class UnrealFixtureTreeTests
{
    private const string BuildFile = "Source/Fixture/Fixture.Build.cs";
    private const string CharacterHeader = "Source/Fixture/FixtureCharacter.h";
    private const UnrealConfig Configuration = UnrealConfig.Shipping;
    private const string DataHeader = "Source/Fixture/FixtureData.h";
    private const string DataSource = "Source/Fixture/FixtureData.cpp";
    private const string DefaultEngine = "Config/DefaultEngine.ini";
    private const string DefaultGame = "Config/DefaultGame.ini";
    private const string EditorTargetFile = "Source/FixtureEditor.Target.cs";
    private const string EntityHeader = "Source/Fixture/FixtureEntity.h";
    private const string GameModeHeader = "Source/Fixture/FixtureGameMode.h";
    private const string GameModeSource = "Source/Fixture/FixtureGameMode.cpp";
    private const string HeroHeader = "Source/Fixture/FixtureHero.h";
    private const string ItemHeader = "Source/Fixture/FixtureItem.h";
    private const string ModuleSource = "Source/Fixture/Fixture.cpp";
    private const string PchHeader = "Source/Fixture/FixturePCH.h";
    private const string Player = "win-x64-shipping";
    private const string RunningStateHeader = "Source/Fixture/FixtureRunningState.h";
    private const string StateHeader = "Source/Fixture/FixtureState.h";
    private const string SubsystemHeader = "Source/Fixture/FixtureSubsystem.h";
    private const string SubsystemSource = "Source/Fixture/FixtureSubsystem.cpp";
    private const string TargetFile = "Source/Fixture.Target.cs";
    private const string TypesSource = "Source/Fixture/FixtureTypes.cpp";
    private const string ViewportClientHeader = "Source/Fixture/FixtureViewportClient.h";
    private const string ViewportClientSource = "Source/Fixture/FixtureViewportClient.cpp";

    private static readonly string[] _alwaysRendered =
    [
        "Config/DefaultEngine.ini",
        "Config/DefaultGame.ini",
        "Config/DefaultInput.ini",
        "Fixture.uproject",
        BuildFile,
        ModuleSource,
        DataSource,
        DataHeader,
        ItemHeader,
        EntityHeader,
        CharacterHeader,
        HeroHeader,
        StateHeader,
        RunningStateHeader,
        GameModeSource,
        GameModeHeader,
        TypesSource,
        TargetFile,
        EditorTargetFile,
    ];

    private TestFolder _folder = null!;

    private static IEnumerable<Version> Minors()
    {
        for (int minor = 0; minor <= 27; minor++)
        {
            yield return new Version(4, minor);
        }

        for (int minor = 0; minor <= 8; minor++)
        {
            yield return new Version(5, minor);
        }
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _folder = new TestFolder();

        foreach (var version in Minors())
        {
            UnrealFixtureSource.Render(OutputProject(), Rendered(version), version);
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _folder.Delete();
    }

    // No directive reaches the engine in any version.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_LeavesNoDirectiveLine(Version version)
    {
        string[] gated = [.. RenderedFiles(version)
            .SelectMany(File.ReadLines)
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith("//@", StringComparison.Ordinal) || line.StartsWith(";@", StringComparison.Ordinal))];

        Assert.That(gated, Is.Empty);
    }

    // Every version gets the whole project and only the gated files come and go, so a stray gate can't drop another file.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_WritesExactlyTheExpectedFiles(Version version)
    {
        List<string> expected = [.. _alwaysRendered];

        // Keeps the module header for the shared PCH before 4.15.
        if (version < new Version(4, 15))
        {
            expected.Add(PchHeader);
        }

        // Keeps the subsystem class from 4.22, because engines before that don't have subsystems.
        if (version >= new Version(4, 22))
        {
            expected.Add(SubsystemHeader);
            expected.Add(SubsystemSource);
        }

        // Keeps the viewport client on 4.17 only, because its game viewport crashes under the null RHI.
        if (version == new Version(4, 17))
        {
            expected.Add(ViewportClientHeader);
            expected.Add(ViewportClientSource);
        }

        Assert.That(RelativeFiles(Rendered(version)), Is.EquivalentTo(expected));
    }

    // The build copies only the project files and the unreal3 classes, so a stray editor file can't change a player key.
    [Test]
    public void Build_ProjectsFolder_CopiesUnreal4FilesAndOnlyUcForUnreal3()
    {
        string projects = Path.Combine(AppContext.BaseDirectory, "projects");
        string[] unreal3 = [.. Directory.EnumerateFiles(Path.Combine(projects, "unreal3"), "*", SearchOption.AllDirectories)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unreal3, Is.Not.Empty.And.All.EndsWith(".uc"));
            string[] gated = [PchHeader, SubsystemHeader, SubsystemSource, ViewportClientHeader, ViewportClientSource];

            Assert.That(RelativeFiles(OutputProject()), Is.EquivalentTo(_alwaysRendered.Concat(gated)));
        }
    }

    // Engines before 5.0 don't have TObjectPtr, so a raw pointer with the same layout takes its place.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_UsesTObjectPtrFrom50(Version version)
    {
        Assert.That(AllText(version).Contains("TObjectPtr", StringComparison.Ordinal), Is.EqualTo(version >= new Version(5, 0)));
    }

    // A class body takes GENERATED_UCLASS_BODY before 4.6 and a struct body takes GENERATED_USTRUCT_BODY before 4.8.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_UsesTheBodyMacroOfItsVersion(Version version)
    {
        string all = AllText(version);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(all.Contains("GENERATED_UCLASS_BODY", StringComparison.Ordinal), Is.EqualTo(version < new Version(4, 6)));
            Assert.That(all.Contains("GENERATED_USTRUCT_BODY", StringComparison.Ordinal), Is.EqualTo(version < new Version(4, 8)));
            Assert.That(Text(version, DataHeader).Contains("GENERATED_BODY", StringComparison.Ordinal), Is.EqualTo(version >= new Version(4, 6)));
        }
    }

    // The rendered project file has to parse as JSON and its association has only major.minor.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_ProjectFileIsJsonForThatEngine(Version version)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(RenderedFile(version, "Fixture.uproject")));
        var root = document.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("EngineAssociation").GetString(), Is.EqualTo($"{version}"));
            Assert.That(root.GetProperty("Modules")[0].GetProperty("Name").GetString(), Is.EqualTo("Fixture"));
        }
    }

    // BeginPlay writes the marker last, because the values are only complete once the marker is set.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_ReadyMarkerIsLastInBeginPlay(Version version)
    {
        List<string> lines = [.. File.ReadLines(RenderedFile(version, DataSource))];
        int marker = lines.FindIndex(line => line.Contains("FixtureReady.txt", StringComparison.Ordinal));
        int lastValue = lines.FindLastIndex(line => line.Contains("Hp = 1000;", StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(marker, Is.GreaterThan(lastValue));
            Assert.That(lines[marker + 1], Is.EqualTo("}"));
            Assert.That(lastValue, Is.GreaterThan(lines.IndexOf("void AFixtureData::BeginPlay()")));
        }
    }

    // Shipping doesn't have logs, so a check waits for this 1 file next to the executable.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_WritesOneMarkerNextToTheExecutable(Version version)
    {
        string[] markers = [.. RenderedFiles(version)
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("FixtureReady.txt", StringComparison.Ordinal))];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markers, Has.Length.EqualTo(1));
            Assert.That(markers[0], Does.Contain("FFileHelper::SaveStringToFile("));
            Assert.That(markers[0], Does.Contain("FPlatformProcess::BaseDir()"));
        }
    }

    // BeginPlay sets the same values on every engine, so a test can read them on any version.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_SetsEveryValue(Version version)
    {
        string source = Text(version, DataSource);

        string[] values =
        [
            "PrimaryActorTick.bCanEverTick = true;",
            "IntValue = 1337;",
            "LongValue = 9000000000LL;",
            "FloatValue = 3.5f;",
            "DoubleValue = 2.718281828;",
            "BoolValue = true;",
            "bFirst = 1;",
            "bSecond = 0;",
            "bThird = 1;",
            "Kind = EFixtureKind::Iron;",
            "OldKind = OldIron;",
            "StringValue = TEXT(\"hello-fixture\");",
            "NameValue = FName(TEXT(\"fixture-name\"));",
            "TextValue = FText::FromString(TEXT(\"hello-text\"));",
            "Coord.X = 7;",
            "Coord.Y = 9;",
            "Position = FVector(1.0f, 2.0f, 3.0f);",
            "IntArray.Add(100);",
            "IntArray.Add(200);",
            "IntArray.Add(300);",
            "StringArray.Add(TEXT(\"a\"));",
            "StringArray.Add(TEXT(\"b\"));",
            "StringArray.Add(TEXT(\"c\"));",
            "CoordArray[0].X = 1;",
            "CoordArray[0].Y = 2;",
            "CoordArray[1].X = 3;",
            "CoordArray[1].Y = 4;",
            "VecArray.Add(FVector(1.25f, -2.5f, 3.75f));",
            "VecArray.Add(FVector(4.5f, 5.25f, -6.75f));",
            "Item->ItemName = TEXT(\"sword\");",
            "Item->Count = 1;",
            "Shield->ItemName = TEXT(\"shield\");",
            "Shield->Count = 3;",
            "Items.Add(Item);",
            "Items.Add(Shield);",
            "WeakItem = Shield;",
            "Hero->Id = 7;",
            "Hero->Level = 12;",
            "Hero->Title = TEXT(\"the-brave\");",
            "Running->Speed = 8.5f;",
            "CurrentState = Running;",
            "Hp = 1000;",
            "Hp -= 5;",
            "Elapsed -= 1.0f;",
        ];

        Assert.That(values.Where(value => !source.Contains(value, StringComparison.Ordinal)), Is.Empty);
    }

    // Each row is 1 gated text and the engine range it exists on.
    [TestCase(TargetFile, "TargetType.Game", "4.0", "5.8")]
    [TestCase(TargetFile, ": base(Target)", "4.16", "5.8")]
    [TestCase(TargetFile, "using System.Collections.Generic;", "4.0", "4.15")]
    [TestCase(TargetFile, "SetupBinaries", "4.0", "4.15")]
    [TestCase(TargetFile, "OutExtraModuleNames.Add", "4.0", "4.15")]
    [TestCase(TargetFile, " ExtraModuleNames.Add", "4.16", "5.8")]
    [TestCase(TargetFile, "DefaultBuildSettings", "4.24", "5.8")]
    [TestCase(TargetFile, "IncludeOrderVersion", "5.1", "5.8")]
    [TestCase(TargetFile, "WindowsCompiler.VisualStudio2017", "4.15", "4.19")]
    [TestCase(TargetFile, "CompilerVersion = \"14.29.30133\"", "4.20", "4.21")]
    [TestCase(TargetFile, "GlobalDefinitions.Add", "4.16", "4.18")]
    [TestCase(TargetFile, "SetupGlobalEnvironment", "4.15", "4.15")]
    [TestCase(TargetFile, "_mm_loadu_si64", "4.15", "4.18")]
    [TestCase(EditorTargetFile, "TargetType.Editor", "4.0", "5.8")]
    [TestCase(EditorTargetFile, ": base(Target)", "4.16", "5.8")]
    [TestCase(EditorTargetFile, "using System.Collections.Generic;", "4.0", "4.15")]
    [TestCase(EditorTargetFile, "SetupBinaries", "4.0", "4.15")]
    [TestCase(EditorTargetFile, "OutExtraModuleNames.Add", "4.0", "4.15")]
    [TestCase(EditorTargetFile, " ExtraModuleNames.Add", "4.16", "5.8")]
    [TestCase(EditorTargetFile, "DefaultBuildSettings", "4.24", "5.8")]
    [TestCase(EditorTargetFile, "IncludeOrderVersion", "5.1", "5.8")]
    [TestCase(EditorTargetFile, "WindowsCompiler.VisualStudio2017", "4.15", "4.19")]
    [TestCase(EditorTargetFile, "CompilerVersion = \"14.29.30133\"", "4.20", "4.21")]
    [TestCase(BuildFile, "public Fixture(TargetInfo Target)", "4.0", "4.15")]
    [TestCase(BuildFile, "ReadOnlyTargetRules", "4.16", "5.8")]
    [TestCase(BuildFile, "\"Core\", \"CoreUObject\", \"Engine\"", "4.0", "5.8")]
    [TestCase(BuildFile, "PCHUsageMode.NoSharedPCHs", "4.5", "4.7")]
    [TestCase(BuildFile, "PCHUsageMode.UseExplicitOrSharedPCHs", "4.15", "5.8")]
    [TestCase(BuildFile, "MinFilesUsingPrecompiledHeaderOverride", "4.10", "4.11")]
    [TestCase(BuildFile, "PublicSystemIncludePaths.InsertRange(0,", "4.10", "4.10")]
    [TestCase(BuildFile, @"Windows Kits\8.1\Include\shared", "4.10", "4.10")]
    [TestCase(BuildFile, @"Windows Kits\8.1\Include\um", "4.10", "4.10")]
    [TestCase(ModuleSource, "FixturePCH.h", "4.0", "4.14")]
    [TestCase(ModuleSource, "Modules/ModuleManager.h", "4.15", "5.8")]
    [TestCase(ModuleSource, "Fixture.generated.inl", "4.0", "4.1")]
    [TestCase(ModuleSource, "FixtureGameMode.h", "4.0", "4.1")]
    [TestCase(ModuleSource, "FixtureRunningState.h", "4.0", "4.1")]
    [TestCase(ModuleSource, "IMPLEMENT_PRIMARY_GAME_MODULE", "4.0", "5.8")]
    [TestCase(PchHeader, "#include \"Engine.h\"", "4.0", "4.14")]
    [TestCase(PchHeader, "#define _mm_loadu_si64(p)", "4.0", "4.14")]
    [TestCase(DataHeader, "FixturePCH.h", "4.0", "4.14")]
    [TestCase(DataHeader, "CoreMinimal.h", "4.15", "5.8")]
    [TestCase(DataHeader, "GameFramework/Actor.h", "4.15", "5.8")]
    [TestCase(DataHeader, "enum class EFixtureKind : uint8", "4.5", "5.8")]
    [TestCase(DataHeader, "namespace EFixtureKind", "4.0", "4.4")]
    [TestCase(DataHeader, "TEnumAsByte<EFixtureKind::Type> Kind;", "4.0", "4.4")]
    [TestCase(DataHeader, " EFixtureKind Kind;", "4.5", "5.8")]
    [TestCase(DataHeader, "enum EFixtureOldKind : int", "5.1", "5.8")]
    [TestCase(DataHeader, "enum EFixtureOldKind\r\n", "4.0", "5.0")]
    [TestCase(DataHeader, "TEnumAsByte<EFixtureOldKind> OldKind;", "4.0", "5.8")]
    [TestCase(DataHeader, "AFixtureData();", "4.7", "5.8")]
    [TestCase(DataHeader, "AFixtureData(const FObjectInitializer& ObjectInitializer);", "4.6", "4.6")]
    [TestCase(DataHeader, "TMap<FString, int32> Dict;", "4.8", "5.8")]
    [TestCase(DataHeader, "TMap<int32, int32> IntDict;", "4.8", "5.8")]
    [TestCase(DataHeader, "TSet<int32> IntSet;", "4.13", "5.8")]
    [TestCase(DataHeader, "TWeakObjectPtr<UFixtureItem> WeakItem;", "4.0", "5.8")]
    [TestCase(DataHeader, "UFixtureItem* Item;", "4.0", "4.27")]
    [TestCase(DataHeader, "TArray<UFixtureItem*> Items;", "4.0", "4.27")]
    [TestCase(DataHeader, "UFixtureHero* Hero;", "4.0", "4.27")]
    [TestCase(DataHeader, "UFixtureState* CurrentState;", "4.0", "4.27")]
    [TestCase(DataSource, "FixturePCH.h", "4.0", "4.14")]
    [TestCase(DataSource, "HAL/PlatformProcess.h", "4.15", "5.8")]
    [TestCase(DataSource, "Misc/FileHelper.h", "4.15", "5.8")]
    [TestCase(DataSource, "AFixtureData::AFixtureData()", "4.7", "5.8")]
    [TestCase(DataSource, "AFixtureData::AFixtureData(const FObjectInitializer& ObjectInitializer)", "4.6", "4.6")]
    [TestCase(DataSource, "FPostConstructInitializeProperties", "4.0", "4.5")]
    [TestCase(DataSource, "Dict.Add(TEXT(\"two\"), 2);", "4.8", "5.8")]
    [TestCase(DataSource, "IntDict.Add(5, 50);", "4.8", "5.8")]
    [TestCase(DataSource, "IntSet.Add(22);", "4.13", "5.8")]
    [TestCase(GameModeHeader, "GameFramework/GameMode.h", "4.0", "4.13")]
    [TestCase(GameModeHeader, "GameFramework/GameModeBase.h", "4.14", "5.8")]
    [TestCase(GameModeHeader, "public AGameMode\r\n", "4.0", "4.13")]
    [TestCase(GameModeHeader, "public AGameModeBase", "4.14", "5.8")]
    [TestCase(GameModeHeader, "GENERATED_UCLASS_BODY", "4.0", "4.5")]
    [TestCase(GameModeHeader, "FObjectInitializer", "4.6", "4.6")]
    [TestCase(GameModeHeader, "AFixtureData* FixtureData;", "4.0", "4.27")]
    [TestCase(GameModeHeader, "TObjectPtr<AFixtureData> FixtureData;", "5.0", "5.8")]
    [TestCase(GameModeSource, "FixturePCH.h", "4.0", "4.14")]
    [TestCase(GameModeSource, "FObjectInitializer", "4.6", "4.6")]
    [TestCase(GameModeSource, "FPostConstructInitializeProperties", "4.0", "4.5")]
    [TestCase(GameModeSource, "SpawnActor<AFixtureData>", "4.0", "5.8")]
    [TestCase(SubsystemHeader, "public UGameInstanceSubsystem", "4.22", "5.8")]
    [TestCase(SubsystemSource, "SubsystemValue = 4242;", "4.22", "5.8")]
    [TestCase(DefaultEngine, "GlobalDefaultGameMode=/Script/Fixture.FixtureGameMode", "4.0", "5.8")]
    [TestCase(DefaultEngine, "GameDefaultMap=/Engine/Maps/Entry.Entry", "4.0", "5.8")]
    [TestCase(DefaultEngine, "[URL]", "4.0", "4.5")]
    [TestCase(DefaultEngine, "GameName=Fixture", "4.0", "4.5")]
    [TestCase(DefaultEngine, "GameViewportClientClassName=/Script/Fixture.FixtureViewportClient", "4.17", "4.17")]
    [TestCase(ViewportClientSource, "UFixtureViewportClient::Draw", "4.17", "4.17")]
    [TestCase(DefaultGame, "ProjectName=Fixture", "4.0", "5.8")]
    [TestCase(TypesSource, "FixturePCH.h", "4.0", "4.14")]
    [TestCase(TypesSource, "CoreMinimal.h", "4.15", "5.8")]
    [TestCase(TypesSource, "UObject/EnumProperty.h", "4.15", "5.8")]
    [TestCase(TypesSource, "UObject/Field.h", "4.25", "5.8")]
    [TestCase(TypesSource, "Engine/GameInstance.h", "4.4", "5.8")]
    [TestCase(TypesSource, "GameFramework/GameModeBase.h", "4.14", "5.8")]
    [TestCase(TypesSource, "GameFramework/GameStateBase.h", "4.14", "5.8")]
    [TestCase(TypesSource, "Subsystems/SubsystemCollection.h", "4.22", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UObjectBase)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UObject)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UField)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UStruct)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UClass)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UEnum)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UPackage)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FName)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FNameEntry)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FString)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptArray)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FWeakObjectPtr)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FUObjectArray)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UEngine)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UGameEngine)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(ULocalPlayer)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UGameViewportClient)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UWorld)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FWorldContext)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(AWorldSettings)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(AGameMode)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(AGameState)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(ACharacter)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(APlayerState)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UMovementComponent)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UCharacterMovementComponent)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UDataTable)", "4.0", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UGameInstance)", "4.4", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UProperty)", "4.0", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UBoolProperty)", "4.0", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UObjectProperty)", "4.0", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UStructProperty)", "4.0", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UArrayProperty)", "4.0", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UByteProperty)", "4.0", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UMapProperty)", "4.8", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(USetProperty)", "4.13", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(UEnumProperty)", "4.15", "4.24")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FField)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FFieldClass)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FBoolProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FObjectProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FStructProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FArrayProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FMapProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FSetProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FByteProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FEnumProperty)", "4.25", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptBitArray)", "4.8", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptSparseArray)", "4.8", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptSet)", "4.8", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptMap)", "4.8", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptMapLayout)", "4.8", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FScriptSetLayout)", "4.8", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FUObjectItem)", "4.11", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FFixedUObjectArray)", "4.11", "4.19")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FChunkedFixedUObjectArray)", "4.20", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(AGameModeBase)", "4.14", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(AGameStateBase)", "4.14", "5.8")]
    [TestCase(TypesSource, "FIXTURE_KEEP_TYPE(FSubsystemCollectionBase)", "4.22", "5.8")]
    public void Render_EveryMinor_TextOnlyInItsRange(string relativePath, string text, string first, string last)
    {
        var firstVersion = Version.Parse(first);
        var lastVersion = Version.Parse(last);

        string[] expected = [.. Minors()
            .Where(version => version >= firstVersion && version <= lastVersion)
            .Select(version => version.ToString())];
        string[] actual = [.. Minors()
            .Where(version => Text(version, relativePath).Contains(text, StringComparison.Ordinal))
            .Select(version => version.ToString())];

        Assert.That(actual, Is.EqualTo(expected));
    }

    // Git checks the tree out with CRLF and every rendered line keeps it, in every file of every version.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_EveryFileKeepsCrlf(Version version)
    {
        string[] withBareLineFeed = [.. RenderedFiles(version).Where(path => !HasOnlyCrlf(File.ReadAllText(path)))];

        Assert.That(withBareLineFeed, Is.Empty);
    }

    // UHT ignores #if on engine versions, so the tree gates at text level and a plain #if can't slip in.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_LeavesNoPreprocessorGate(Version version)
    {
        string[] lines = [.. RenderedFiles(version)
            .SelectMany(File.ReadLines)
            .Where(line => line.TrimStart().StartsWith("#if", StringComparison.Ordinal) || line.Contains("ENGINE_M", StringComparison.Ordinal))];

        Assert.That(lines, Is.Empty);
    }

    // A gate that cuts a block in half shows up as an uneven count.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_BracesAndParenthesesBalance(Version version)
    {
        string[] unbalanced = [.. RenderedFiles(version).Where(path => !IsBalanced(File.ReadAllText(path)))];

        Assert.That(unbalanced, Is.Empty);
    }

    // A config line is a blank line, a comment, a section or a key with a value, so a gate can't leave anything else behind.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_ConfigLinesStayWellFormed(Version version)
    {
        string[] badLines = [.. Directory.EnumerateFiles(Path.Combine(Rendered(version), "Config"), "*.ini")
            .SelectMany(File.ReadLines)
            .Where(line => !ConfigLineRegex().IsMatch(line))];

        Assert.That(badLines, Is.Empty);
    }

    // The module header defines the intrinsic in front of the engine headers, because wchar.h calls it.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_PchDefinesTheIntrinsicFirstBefore415(Version version)
    {
        string pch = Text(version, PchHeader);
        int define = pch.IndexOf("#define _mm_loadu_si64", StringComparison.Ordinal);
        int engine = pch.IndexOf("#include \"Engine.h\"", StringComparison.Ordinal);

        Assert.That(define >= 0 && define < engine, Is.EqualTo(version < new Version(4, 15)));
    }

    // Only the game target defines the CRT intrinsic, because the editor target never builds for 32-bit.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_EditorTargetLeavesTheIntrinsicOut(Version version)
    {
        Assert.That(Text(version, EditorTargetFile), Does.Not.Contain("_mm_loadu_si64"));
    }

    // An include of a fixture file that this version doesn't render would stop the compile.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_LocalIncludesExist(Version version)
    {
        string module = Path.Combine(Rendered(version), "Source", "Fixture");
        string[] missing = [.. Directory.EnumerateFiles(module)
            .Where(path => Path.GetExtension(path) is ".cpp" or ".h")
            .SelectMany(File.ReadLines)
            .Select(line => IncludeRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Where(include => include.StartsWith("Fixture", StringComparison.Ordinal))
            .Where(include => !include.Contains(".generated.", StringComparison.Ordinal))
            .Where(include => !File.Exists(Path.Combine(module, include)))];

        Assert.That(missing, Is.Empty);
    }

    // UHT requires the generated header as the last include of a header with reflected types.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_GeneratedHeaderIsTheLastInclude(Version version)
    {
        string[] headers = [.. ReflectedHeaders(version)];
        string[] wrong = [.. headers.Where(header =>
        {
            string name = Path.GetFileNameWithoutExtension(header);
            var includes = IncludeRegex().Matches(File.ReadAllText(header));

            return includes[^1].Groups[1].Value != $"{name}.generated.h";
        })];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(headers, Has.Length.EqualTo(ReflectedClassCount(version)));
            Assert.That(wrong, Is.Empty);
        }
    }

    // UHT of 4.0 refuses a header with 2 reflected classes, so every version gets 1 class per header.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_EveryHeaderDeclaresOneClassAtMost(Version version)
    {
        string[] crowded = [.. ReflectedHeaders(version).Where(header => UclassRegex().Count(File.ReadAllText(header)) > 1)];

        Assert.That(crowded, Is.Empty);
    }

    // Installed engines from 4.5 to 4.7 find no shared PCH for the editor target, so only they take the module's own.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_UsesTheSharedPchBefore415OutsideOf45To47(Version version)
    {
        bool expected = version < new Version(4, 15) && (version < new Version(4, 5) || version > new Version(4, 7));

        Assert.That(Text(version, BuildFile).Contains("PCHUsageMode.UseSharedPCHs", StringComparison.Ordinal), Is.EqualTo(expected));
    }

    // Engines before 4.15 take the shared module header as the first include of every source file.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_EverySourceFileOpensWithThePchBefore415(Version version)
    {
        bool expected = version < new Version(4, 15);
        string[] sources = [ModuleSource, DataSource, GameModeSource, TypesSource];

        foreach (string source in sources)
        {
            string first = IncludeRegex().Match(Text(version, source)).Groups[1].Value;

            Assert.That(first == "FixturePCH.h", Is.EqualTo(expected), source);
        }
    }

    // GENERATED_UCLASS_BODY declares the constructor before 4.6, so every reflected class has to define it.
    [TestCaseSource(nameof(Minors))]
    public void Render_EveryMinor_EveryClassDefinesItsConstructorBefore46(Version version)
    {
        string headers = string.Concat(ReflectedHeaders(version).Select(File.ReadAllText));
        string sources = Text(version, DataSource) + Text(version, GameModeSource);
        bool expected = version < new Version(4, 6);
        string[] classes = [.. ReflectedClassRegex().Matches(headers).Select(match => match.Groups[1].Value)];
        string[] defined = [.. classes.Where(name =>
            sources.Contains($"{name}::{name}(const FPostConstructInitializeProperties& PCIP)", StringComparison.Ordinal))];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(classes, Has.Length.EqualTo(ReflectedClassCount(version)));
            Assert.That(defined, Is.EqualTo(expected ? classes : []));
        }
    }

    // The manifest lists 1 property per UPROPERTY of the data actor on every engine, so nothing silently goes missing from it.
    [TestCaseSource(nameof(Minors))]
    public void FromProject_EveryMinor_ListsOnePropertyPerUproperty(Version version)
    {
        var manifest = FromRendered(version);
        string header = Text(version, DataHeader);
        string actor = header[header.IndexOf("class AFixtureData", StringComparison.Ordinal)..];
        int count = UpropertyRegex().Count(actor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.Properties, Has.Count.EqualTo(count));
            Assert.That(manifest.Properties, Is.Unique);
            Assert.That(manifest.Properties, Has.None.Empty);
            Assert.That(manifest.Properties[0], Is.EqualTo("IntValue"));
            Assert.That(manifest.Properties[^1], Is.EqualTo("Hp"));
        }
    }

    // Measure skips what the manifest leaves out, so each feature shows up from the first engine that has it.
    [TestCaseSource(nameof(Minors))]
    public void FromProject_EveryMinor_ListsFeaturesFromTheirFirstEngine(Version version)
    {
        var manifest = FromRendered(version);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.Properties.Contains("Dict"), Is.EqualTo(version >= new Version(4, 8)));
            Assert.That(manifest.Properties.Contains("IntDict"), Is.EqualTo(version >= new Version(4, 8)));
            Assert.That(manifest.Properties.Contains("IntSet"), Is.EqualTo(version >= new Version(4, 13)));
            Assert.That(manifest.Classes.Contains("FixtureSubsystem"), Is.EqualTo(version >= new Version(4, 22)));
            Assert.That(manifest.Classes.Contains("FixtureViewportClient"), Is.EqualTo(version == new Version(4, 17)));
            Assert.That(manifest.Classes, Has.Count.EqualTo(ReflectedClassCount(version)));
        }
    }

    [GeneratedRegex(@"^(\s*|;.*|\[.+\]|[^=\s]+=.*)$")]
    private static partial Regex ConfigLineRegex();

    [GeneratedRegex(@"^\s*#include\s+""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex IncludeRegex();

    [GeneratedRegex(@"\bclass\s+([UA]Fixture\w+)\s*:")]
    private static partial Regex ReflectedClassRegex();

    [GeneratedRegex(@"^\s*UCLASS\(", RegexOptions.Multiline)]
    private static partial Regex UclassRegex();

    [GeneratedRegex(@"\bUPROPERTY\(")]
    private static partial Regex UpropertyRegex();

    private static bool HasOnlyCrlf(string text)
    {
        return text.Count(character => character == '\n') == text.Split("\r\n").Length - 1;
    }

    private static bool IsBalanced(string text)
    {
        return text.Count(character => character == '{') == text.Count(character => character == '}')
            && text.Count(character => character == '(') == text.Count(character => character == ')');
    }

    private static string OutputProject()
    {
        return Path.Combine(AppContext.BaseDirectory, "projects", "unreal4");
    }

    private static IEnumerable<string> RelativeFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
    }

    private string AllText(Version version)
    {
        return string.Concat(RenderedFiles(version).Select(File.ReadAllText));
    }

    // Counts 8 classes on every engine, plus the subsystem from 4.22 and the viewport client on 4.17.
    private static int ReflectedClassCount(Version version)
    {
        return 8 + (version >= new Version(4, 22) ? 1 : 0) + (version == new Version(4, 17) ? 1 : 0);
    }

    private IEnumerable<string> ReflectedHeaders(Version version)
    {
        return Directory.EnumerateFiles(Path.Combine(Rendered(version), "Source", "Fixture"), "*.h")
            .Where(header => UclassRegex().IsMatch(File.ReadAllText(header)));
    }

    private UnrealFixtureManifest FromRendered(Version version)
    {
        return UnrealFixtureManifest.FromProject(Rendered(version), $"{version}", Player, Configuration);
    }

    private string Rendered(Version version)
    {
        return Path.Combine(_folder.Root, $"{version}");
    }

    private string RenderedFile(Version version, string relativePath)
    {
        return Path.Combine(Rendered(version), relativePath);
    }

    private IEnumerable<string> RenderedFiles(Version version)
    {
        return Directory.EnumerateFiles(Rendered(version), "*", SearchOption.AllDirectories);
    }

    private string Text(Version version, string relativePath)
    {
        string path = RenderedFile(version, relativePath);

        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
