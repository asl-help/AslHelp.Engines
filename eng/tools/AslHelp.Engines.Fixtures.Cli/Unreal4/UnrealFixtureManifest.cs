using AslHelp.Engines.Fixtures.Unreal4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Represents <c>fixture.json</c>, the list of what a player compiled in, so a measurement skips what a version lacks and
///     fails on what it should have.
/// </summary>
/// <param name="EngineVersion">The pinned engine version, such as <c>4.19.2</c>.</param>
/// <param name="Player">The player tag, such as <c>win-x64-shipping</c>.</param>
/// <param name="Configuration">The build configuration, written as <c>Development</c> or <c>Shipping</c>.</param>
/// <param name="Classes">The reflected classes without their <c>U</c> or <c>A</c> prefix, sorted.</param>
/// <param name="Properties">The reflected properties of the data actor, in the order of the header.</param>
internal sealed partial record UnrealFixtureManifest(
    string EngineVersion,
    string Player,
    UnrealConfig Configuration,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> Properties)
{
    /// <summary>
    ///     Represents the name of the file next to the player.
    /// </summary>
    public const string FileName = "fixture.json";

    private const string DataClass = "FixtureData";

    // Matches the arguments of a reflection macro, with 1 level of nested parentheses as in meta=(...).
    private const string MacroArguments = @"\((?:[^()]|\([^()]*\))*\)";

    /// <summary>
    ///     Reads the manifest from the headers of a rendered project.
    /// </summary>
    /// <param name="projectDirectory">The project that <see cref="UnrealFixtureSource.Render"/> wrote.</param>
    /// <param name="engineVersion">The pinned engine version.</param>
    /// <param name="player">The player tag.</param>
    /// <param name="configuration">The build configuration.</param>
    /// <returns>
    ///     The manifest.
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">The project doesn't have a <c>Source/Fixture</c> folder.</exception>
    /// <exception cref="InvalidDataException">
    ///     The headers don't declare the data actor or a reflected property of it doesn't end in a member name.
    /// </exception>
    public static UnrealFixtureManifest FromProject(string projectDirectory, string engineVersion, string player, UnrealConfig configuration)
    {
        string moduleDirectory = Path.Combine(projectDirectory, "Source", "Fixture");

        if (!Directory.Exists(moduleDirectory))
        {
            throw new DirectoryNotFoundException(UnrealSR.FormatSourceFolderNotFound(moduleDirectory));
        }

        List<string> classes = [];
        List<string> properties = [];

        foreach (string header in Directory.EnumerateFiles(moduleDirectory, "*.h"))
        {
            // Replaces each comment with a space, so a comment between 2 words of a declaration doesn't glue them together.
            string text = CommentRegex().Replace(File.ReadAllText(header), " ");

            foreach (Match match in ClassRegex().Matches(text))
            {
                string name = match.Groups[1].Value;
                classes.Add(name);

                if (name == DataClass)
                {
                    AddProperties(header, text[(match.Index + match.Length)..], properties);
                }
            }
        }

        if (!classes.Contains(DataClass))
        {
            throw new InvalidDataException(UnrealSR.FormatManifestNoFixtureData(moduleDirectory, DataClass));
        }

        classes.Sort(StringComparer.Ordinal);

        return new UnrealFixtureManifest(engineVersion, player, configuration, classes, properties);
    }

    /// <summary>
    ///     Reads <see cref="FileName"/> from <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The folder of the player.</param>
    /// <returns>
    ///     The manifest.
    /// </returns>
    /// <exception cref="FileNotFoundException">The folder doesn't have the file.</exception>
    /// <exception cref="InvalidDataException">The file is the JSON <c>null</c>.</exception>
    /// <exception cref="JsonException">The file isn't the JSON of a manifest.</exception>
    public static UnrealFixtureManifest Read(string playerDirectory)
    {
        string path = Path.Combine(playerDirectory, FileName);
        using var stream = File.OpenRead(path);

        return JsonSerializer.Deserialize(stream, UnrealFixtureManifestContext.Default.UnrealFixtureManifest)
            ?? throw new InvalidDataException(UnrealSR.FormatManifestEmpty(path));
    }

    /// <summary>
    ///     Writes <see cref="FileName"/> into <paramref name="playerDirectory"/>.
    /// </summary>
    /// <param name="playerDirectory">The folder of the player.</param>
    /// <exception cref="IOException">The write fails.</exception>
    public void Write(string playerDirectory)
    {
        using var stream = File.Create(Path.Combine(playerDirectory, FileName));

        JsonSerializer.Serialize(stream, this, UnrealFixtureManifestContext.Default.UnrealFixtureManifest);
    }

    /// <summary>
    ///     Adds the member that each reflected property of a class declares.
    /// </summary>
    /// <param name="header">The path of the header, for the message.</param>
    /// <param name="text">The header text after the opening brace of the class.</param>
    /// <param name="properties">The list to add to.</param>
    /// <exception cref="InvalidDataException">A declaration doesn't end in a member name.</exception>
    private static void AddProperties(string header, string text, List<string> properties)
    {
        // Stops at the closing brace that opens a line, because the closing brace of a member sits inside a line.
        string body = ClassEndRegex().Split(text, 2)[0];

        foreach (Match property in PropertyRegex().Matches(body))
        {
            string declaration = property.Groups[1].Value;
            var member = MemberNameRegex().Match(declaration);

            if (!member.Success)
            {
                throw new InvalidDataException(UnrealSR.FormatManifestNoMemberName(declaration.Trim(), header));
            }

            properties.Add(member.Groups[1].Value);
        }
    }

    [GeneratedRegex(@"^\};", RegexOptions.Multiline)]
    private static partial Regex ClassEndRegex();

    [GeneratedRegex(@"UCLASS" + MacroArguments + @"\s*class\s+(?:\w+_API\s+)?[UA](\w+)\b[^{]*\{")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    // Takes the last word before an array size, an initializer, a bit width or the end of the declaration.
    [GeneratedRegex(@"(\w+)\s*(?:\[[^\]]*\]\s*)?(?:=.*|\{.*\}|:\s*\d+)?\s*$", RegexOptions.Singleline)]
    private static partial Regex MemberNameRegex();

    [GeneratedRegex(@"UPROPERTY" + MacroArguments + @"\s*([^;]+);")]
    private static partial Regex PropertyRegex();
}
