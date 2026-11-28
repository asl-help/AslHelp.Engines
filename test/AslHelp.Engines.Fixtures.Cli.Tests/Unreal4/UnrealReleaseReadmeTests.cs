using AslHelp.Engines.Fixtures.Cli.Unreal4;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AslHelp.Engines.Fixtures.Cli.Tests.Unreal4;

public sealed class UnrealReleaseReadmeTests
{
    private const string ReadmeResource = "readme.md";
    private const string TableHeader = "| Versions | `players` |";

    private static readonly string[] _allPlayers = ["win-x64-shipping", "win-x64-development", "win-x86-shipping", "win-x86-development"];

    // Each row renders to the columns of the 1 table line that covers its minor, so a changed row without a changed table fails
    // here. The toolset column stays out, because it comes from the build logs and not from the row.
    [Test]
    public void All_EveryRow_MatchesItsTableLine()
    {
        var lines = ReadTableLines();

        using (Assert.EnterMultipleScope())
        {
            foreach (var row in EmbeddedReleases<UnrealRelease>.All)
            {
                string[][] covering = [.. lines.Where(line => Covers(line[0], row.Minor))];

                Assert.That(covering, Has.Length.EqualTo(1), row.Version);

                if (covering.Length == 1)
                {
                    Assert.That(covering[0][1..6], Is.EqualTo(Columns(row)), row.Version);
                }
            }

            foreach (string[] line in lines)
            {
                Assert.That(EmbeddedReleases<UnrealRelease>.All.Any(row => Covers(line[0], row.Minor)), Is.True, line[0]);
            }
        }
    }

    private static string[] Columns(UnrealRelease row)
    {
        string[] tags = [.. row.Players.Select(static player => player.Tag)];

        return
        [
            tags.SequenceEqual(_allPlayers) ? "all 4" : string.Join(", ", tags),
            Flags(row.EditorFlags),
            Flags(row.PackageFlags),
            row.PathPrepend is null ? string.Empty : PathFolder(row.PathPrepend),
            row.Runtimes == UnrealRuntimeSource.Image ? "image" : "engine",
        ];
    }

    private static bool Covers(string versions, string minor)
    {
        string[] ends = versions.Split(" to ");
        var version = Version.Parse(minor);

        return version >= Version.Parse(ends[0]) && version <= Version.Parse(ends[^1]);
    }

    private static string Flags(IReadOnlyList<string> flags)
    {
        return flags.Count == 0 ? string.Empty : $"`{string.Join(' ', flags)}`";
    }

    // Renders C:\VS2017\VC\Tools\MSVC\14.16.27023\bin\HostX64\x64 as VS2017 14.16 `HostX64\x64`.
    private static string PathFolder(string path)
    {
        string[] parts = path.Split('\\');
        string msvc = string.Join('.', parts[5].Split('.')[..2]);

        return $"{parts[1]} {msvc} `{parts[^2]}\\{parts[^1]}`";
    }

    private static string ReadReadme()
    {
        using var stream = typeof(UnrealReleaseReadmeTests).Assembly.GetManifestResourceStream(ReadmeResource)!;
        using StreamReader reader = new(stream);

        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    private static List<string[]> ReadTableLines()
    {
        string[] lines = ReadReadme().Split('\n');
        int header = Array.FindIndex(lines, line => line.StartsWith(TableHeader, StringComparison.Ordinal));

        Assert.That(header, Is.Not.Negative, TableHeader);

        List<string[]> table = [];

        // Skip the header and the separator line.
        for (int i = header + 2; i < lines.Length && lines[i].StartsWith('|', StringComparison.Ordinal); i++)
        {
            table.Add([.. lines[i].Trim('|').Split('|').Select(cell => cell.Trim())]);
        }

        return table;
    }
}
