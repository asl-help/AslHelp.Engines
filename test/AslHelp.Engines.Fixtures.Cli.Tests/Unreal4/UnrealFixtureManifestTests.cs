using AslHelp.Engines.Fixtures.Cli.Unreal4;
using AslHelp.Engines.Fixtures.Unreal4;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealFixtureManifestTests
{
    private const UnrealConfig Configuration = UnrealConfig.Shipping;
    private const string Player = "win-x64-shipping";

    private static readonly string[] _allClasses =
    [
        "FixtureCharacter",
        "FixtureData",
        "FixtureEntity",
        "FixtureGameMode",
        "FixtureHero",
        "FixtureItem",
        "FixtureRunningState",
        "FixtureState",
        "FixtureSubsystem",
    ];

    private static readonly string[] _baseProperties =
    [
        "IntValue",
        "LongValue",
        "FloatValue",
        "DoubleValue",
        "BoolValue",
        "bFirst",
        "bSecond",
        "bThird",
        "Kind",
        "OldKind",
        "StringValue",
        "NameValue",
        "TextValue",
        "Coord",
        "Position",
        "IntArray",
        "StringArray",
        "CoordArray",
        "VecArray",
    ];

    private static readonly string[] _fixtureDataOnly = ["FixtureData"];

    private static readonly string[] _objectProperties =
    [
        "Item",
        "Items",
        "WeakItem",
        "Hero",
        "CurrentState",
        "Hp",
    ];

    private static readonly string[] _valueOnly = ["Value"];

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

    // The newest engine has every class and every property, in the order of the header.
    [Test]
    public void FromProject_Rendered58_ListsSubsystemAndEveryProperty()
    {
        var manifest = FromRealTree("5.8", "5.8.3");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.EngineVersion, Is.EqualTo("5.8.3"));
            Assert.That(manifest.Player, Is.EqualTo(Player));
            Assert.That(manifest.Configuration, Is.EqualTo(Configuration));
            Assert.That(manifest.Classes, Is.EqualTo(_allClasses));
            Assert.That(manifest.Properties, Is.EqualTo(_baseProperties.Concat(["Dict", "IntDict", "IntSet"]).Concat(_objectProperties)));
        }
    }

    // 4.19 has maps and the set and doesn't have the subsystem.
    [Test]
    public void FromProject_Rendered419_ListsMapsAndSetWithoutSubsystem()
    {
        var manifest = FromRealTree("4.19", "4.19.2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.Classes, Does.Not.Contain("FixtureSubsystem"));
            Assert.That(manifest.Properties, Is.EqualTo(_baseProperties.Concat(["Dict", "IntDict", "IntSet"]).Concat(_objectProperties)));
        }
    }

    // 4.0 doesn't have maps, the set or the subsystem and its namespaced enum still parses to Kind.
    [Test]
    public void FromProject_Rendered40_LacksMapsSetAndSubsystem()
    {
        var manifest = FromRealTree("4.0", "4.0.2");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.Classes, Does.Not.Contain("FixtureSubsystem"));
            Assert.That(manifest.Classes, Does.Contain("FixtureData"));
            Assert.That(manifest.Properties, Is.EqualTo(_baseProperties.Concat(_objectProperties)));
        }
    }

    // A bitfield gives its member name and a commented-out property adds nothing.
    [Test]
    public void FromProject_BitfieldAndComment_TakesTheMemberName()
    {
        string project = _folder.NewFolder();
        WriteHeader(project, """
            UCLASS()
            class AFixtureData : public AActor
            {
                UPROPERTY()
                uint8 bFirst : 1;

                // UPROPERTY() int32 Fake;

                UPROPERTY(EditAnywhere, meta=(ClampMin="0"))
                int32 Value = 2;
            };

            """);

        var manifest = UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration);
        string[] expected = ["bFirst", "Value"];

        Assert.That(manifest.Properties, Is.EqualTo(expected));
    }

    // A member inside a block comment isn't compiled in, so a measurement can't expect it.
    [TestCase("/* UPROPERTY() int32 Fake; */")]
    [TestCase("/*\r\n    UPROPERTY()\r\n    int32 Fake;\r\n    */")]
    public void FromProject_BlockCommentedProperty_AddsNothing(string comment)
    {
        var manifest = FromHeader(Actor($"{comment}\r\n    UPROPERTY()\r\n    int32 Value;"));

        Assert.That(manifest.Properties, Is.EqualTo(_valueOnly));
    }

    // A block comment between 2 words of a declaration doesn't glue them together.
    [Test]
    public void FromProject_BlockCommentInsideDeclaration_TakesTheMemberName()
    {
        var manifest = FromHeader(Actor("UPROPERTY()\r\n    int32/* comment */Value;"));

        Assert.That(manifest.Properties, Is.EqualTo(_valueOnly));
    }

    // A block comment marker inside a line comment doesn't open a block comment and the other way around.
    [Test]
    public void FromProject_CommentMarkersInsideComments_StayComments()
    {
        var manifest = FromHeader(Actor("// /* not open\r\n    UPROPERTY()\r\n    int32 Value;\r\n    /* // */ UPROPERTY()\r\n    int32 Other;"));
        string[] expected = ["Value", "Other"];

        Assert.That(manifest.Properties, Is.EqualTo(expected));
    }

    // Each row is a declaration the header tool takes, so the member name can't come out empty.
    [TestCase("int32 Value")]
    [TestCase("int32 Value[3]")]
    [TestCase("int32 Value[2] = { 1, 2 }")]
    [TestCase("int32 Value{2}")]
    [TestCase("TArray<int32> Value = { 1, 2 }")]
    [TestCase("int32 Value = (1 + 2)")]
    [TestCase("FString Value = TEXT(\"a b\")")]
    [TestCase("TMap<FString,\r\n        int32>\r\n        Value\r\n    ")]
    [TestCase("TArray<TEnumAsByte<EFixtureKind::Type>> Value")]
    [TestCase("uint8 Value:1")]
    [TestCase("UFixtureItem *Value")]
    public void FromProject_DeclarationForm_TakesTheMemberName(string declaration)
    {
        var manifest = FromHeader(Actor($"UPROPERTY()\r\n    {declaration};"));

        Assert.That(manifest.Properties, Is.EqualTo(_valueOnly));
    }

    // A declaration without a member at its end fails, so the file can't list an empty property.
    [TestCase("DECLARE_MEMBER(Value)")]
    [TestCase("int32 (*Value)()")]
    public void FromProject_DeclarationWithoutMember_ThrowsWithThePath(string declaration)
    {
        string project = _folder.NewFolder();
        WriteHeader(project, Actor($"UPROPERTY()\r\n    {declaration};"));

        Assert.That(
            () => UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains(Path.Combine(project, "Source", "Fixture", "File.h"))
                .And.Message.Contains(declaration));
    }

    // Properties of a class after the data actor don't count, because the class body ends at the brace that opens a line.
    [Test]
    public void FromProject_ClassAfterTheActor_LeavesItsPropertiesOut()
    {
        string project = _folder.NewFolder();
        WriteHeader(project, """
            UCLASS()
            class AFixtureData : public AActor
            {
                UPROPERTY()
                int32 Value;
            };

            UCLASS()
            class UOther : public UObject
            {
                UPROPERTY()
                int32 Other;
            };

            """);

        var manifest = UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration);
        string[] expectedClasses = ["FixtureData", "Other"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manifest.Classes, Is.EqualTo(expectedClasses));
            Assert.That(manifest.Properties, Is.EqualTo(_valueOnly));
        }
    }

    // A closing brace inside a line of the class doesn't end the class body, so later properties still count.
    [Test]
    public void FromProject_InlineMethodAndNestedStruct_KeepsReadingTheClass()
    {
        var manifest = FromHeader(Actor(
            "int32 Get() const { return 0; };\r\n    struct FInner\r\n    {\r\n    };\r\n\r\n    UPROPERTY()\r\n    int32 Value;"));

        Assert.That(manifest.Properties, Is.EqualTo(_valueOnly));
    }

    // An export macro, final and macro arguments all fit between UCLASS and the class name.
    [Test]
    public void FromProject_ApiMacroAndFinal_FindsTheActor()
    {
        var manifest = FromHeader(
            "UCLASS(Blueprintable, meta=(Tip=\"b\"))\r\nclass FIXTURE_API AFixtureData final : public AActor\r\n{\r\n};\r\n");

        Assert.That(manifest.Classes, Is.EqualTo(_fixtureDataOnly));
    }

    // Classes from any header sort ordinally, so the file doesn't change with the culture or the order of the folder.
    [Test]
    public void FromProject_ClassesFromSeveralHeaders_SortOrdinally()
    {
        string project = _folder.NewFolder();
        WriteHeader(project, Actor(string.Empty));
        TestFolder.WriteBytes(project, "Source/Fixture/Zeta.h", Encoding.UTF8.GetBytes("UCLASS()\r\nclass Uabc : public UObject\r\n{\r\n};\r\n"));
        TestFolder.WriteBytes(project, "Source/Fixture/Alpha.h", Encoding.UTF8.GetBytes("UCLASS()\r\nclass UZed : public UObject\r\n{\r\n};\r\n"));

        var manifest = UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration);
        string[] expected = ["FixtureData", "Zed", "abc"];

        Assert.That(manifest.Classes, Is.EqualTo(expected));
    }

    // A tree without the data actor can't be a player project.
    [Test]
    public void FromProject_NoFixtureDataClass_Throws()
    {
        string project = _folder.NewFolder();
        WriteHeader(project, "UCLASS()\r\nclass UOther : public UObject\r\n{\r\n};\r\n");

        Assert.That(
            () => UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(project));
    }

    // A missing module folder fails with its path.
    [Test]
    public void FromProject_MissingModuleFolder_Throws()
    {
        string project = _folder.NewFolder();

        Assert.That(
            () => UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration),
            Throws.TypeOf<DirectoryNotFoundException>().With.Message.Contains(project));
    }

    // The file has camelCase keys and no BOM, so any JSON reader takes it.
    [Test]
    public void Write_Manifest_WritesCamelCaseKeysWithoutBom()
    {
        string folder = _folder.NewFolder();
        UnrealFixtureManifest manifest = new("4.19.2", Player, Configuration, ["FixtureData"], ["IntValue"]);

        manifest.Write(folder);

        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, UnrealFixtureManifest.FileName));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), Is.False);
            Assert.That(root.GetProperty("engineVersion").GetString(), Is.EqualTo("4.19.2"));
            Assert.That(root.GetProperty("player").GetString(), Is.EqualTo(Player));
            Assert.That(root.GetProperty("configuration").GetString(), Is.EqualTo("Shipping"));
            Assert.That(root.GetProperty("classes")[0].GetString(), Is.EqualTo("FixtureData"));
            Assert.That(root.GetProperty("properties")[0].GetString(), Is.EqualTo("IntValue"));
        }
    }

    // A written manifest reads back with the same values.
    [Test]
    public void Read_WrittenManifest_ReturnsTheSameValues()
    {
        string folder = _folder.NewFolder();
        UnrealFixtureManifest manifest = new("4.19.2", Player, Configuration, ["FixtureData"], ["IntValue"]);
        manifest.Write(folder);

        var read = UnrealFixtureManifest.Read(folder);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.EngineVersion, Is.EqualTo("4.19.2"));
            Assert.That(read.Player, Is.EqualTo(Player));
            Assert.That(read.Configuration, Is.EqualTo(Configuration));
            Assert.That(read.Classes, Is.EqualTo(["FixtureData"]));
            Assert.That(read.Properties, Is.EqualTo(["IntValue"]));
        }
    }

    // A file with the JSON null throws with the path of the file.
    [Test]
    public void Read_NullJson_ThrowsInvalidDataException()
    {
        string folder = _folder.NewFolder();
        TestFolder.WriteBytes(folder, UnrealFixtureManifest.FileName, "null"u8.ToArray());

        Assert.That(
            () => UnrealFixtureManifest.Read(folder),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(Path.Combine(folder, UnrealFixtureManifest.FileName)));
    }

    private static string Actor(string members)
    {
        return $"UCLASS()\r\nclass AFixtureData : public AActor\r\n{{\r\n    {members}\r\n}};\r\n";
    }

    private static void WriteHeader(string project, string text)
    {
        TestFolder.WriteBytes(project, "Source/Fixture/File.h", Encoding.UTF8.GetBytes(text));
    }

    private UnrealFixtureManifest FromHeader(string text)
    {
        string project = _folder.NewFolder();
        WriteHeader(project, text);

        return UnrealFixtureManifest.FromProject(project, "1.0", Player, Configuration);
    }

    private UnrealFixtureManifest FromRealTree(string minor, string engineVersion)
    {
        string target = _folder.NewFolder();

        UnrealFixtureSource.Render(Path.Combine(AppContext.BaseDirectory, "projects", "unreal4"), target, Version.Parse(minor));

        return UnrealFixtureManifest.FromProject(target, engineVersion, Player, Configuration);
    }
}
