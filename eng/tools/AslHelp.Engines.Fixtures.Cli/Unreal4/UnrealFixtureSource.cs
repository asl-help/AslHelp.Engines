using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AslHelp.Engines.Fixtures.Cli.Unreal4;

/// <summary>
///     Renders the gated player project for 1 engine version.
/// </summary>
/// <remarks>
///     UnrealHeaderTool ignores <c>#if</c> on engine versions, so the project gates its differences with whole lines:
///     <c>//@if &gt;=4.14</c>, <c>//@else</c> and <c>//@endif</c> in <c>.h</c>, <c>.cpp</c> and <c>.cs</c> files and the same with
///     <c>;@</c> in <c>.ini</c> files. The operators are <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;</c>, <c>&lt;=</c> and <c>==</c> on major.minor.
/// </remarks>
internal static partial class UnrealFixtureSource
{
    private static ReadOnlySpan<byte> BlankEngineAssociation => "\"EngineAssociation\": \"\""u8;

    private static ReadOnlySpan<byte> LineSpaceBytes => " \t\r\n"u8;

    /// <summary>
    ///     Replaces <paramref name="targetDirectory"/> with the project in <paramref name="sourceDirectory"/> as
    ///     <paramref name="engineVersion"/> needs it.
    /// </summary>
    /// <remarks>
    ///     The render writes the files of <see cref="RenderFiles"/> and also gives every render an empty <c>Content</c> folder.
    ///     The files render in memory before the first write, so a bad directive leaves the old target as it was.
    ///     The project goes to a sibling <c>.partial</c> folder first and then moves to <paramref name="targetDirectory"/>,
    ///     so a failed write leaves the old target as it was too.
    /// </remarks>
    /// <param name="sourceDirectory">The gated project.</param>
    /// <param name="targetDirectory">The folder to replace.</param>
    /// <param name="engineVersion">The engine version. Only the major and minor parts count.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="sourceDirectory"/> does not exist.</exception>
    /// <exception cref="IOException">A read or a write fails.</exception>
    /// <exception cref="InvalidDataException">A directive line is bad. The message has the path and the line.</exception>
    public static void Render(string sourceDirectory, string targetDirectory, Version engineVersion)
    {
        ArgumentNullException.ThrowIfNull(targetDirectory);

        var files = RenderFiles(sourceDirectory, engineVersion);

        // Trims a trailing separator, so the temp folder sits next to the target and not inside it.
        string target = Path.TrimEndingDirectorySeparator(targetDirectory);
        string tempDirectory = target + TempPaths.PartialSuffix;

        // Deletes a temp folder from a crashed run, so its files don't mix with this one's.
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }

        try
        {
            // Creates the Content folder with the temp folder, because 4.18 only stages a project with 1 and git can't store an
            // empty folder.
            _ = Directory.CreateDirectory(Path.Combine(tempDirectory, "Content"));

            foreach (var file in files)
            {
                string path = Path.Combine(tempDirectory, file.RelativePath);

                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Content.Span);
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(tempDirectory, target);
        }
        catch
        {
            TempPaths.DeleteFolderQuietly(tempDirectory);
            throw;
        }
    }

    /// <summary>
    ///     Renders every file of the project in <paramref name="sourceDirectory"/> as <paramref name="engineVersion"/> needs it.
    /// </summary>
    /// <remarks>
    ///     Kept lines keep their bytes and line endings.
    ///     The render skips a gated file that has only whitespace left and keeps every other kind of file as is.
    ///     The files are what <see cref="Render"/> writes, so a key over them covers exactly the project of 1 version.
    /// </remarks>
    /// <param name="sourceDirectory">The gated project.</param>
    /// <param name="engineVersion">The engine version. Only the major and minor parts count.</param>
    /// <returns>
    ///     The rendered files, each with its path relative to <paramref name="sourceDirectory"/> with '/' separators.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="sourceDirectory"/> does not exist.</exception>
    /// <exception cref="IOException">A read fails.</exception>
    /// <exception cref="InvalidDataException">A directive line is bad. The message has the path and the line.</exception>
    public static IReadOnlyList<CacheKeyFile> RenderFiles(string sourceDirectory, Version engineVersion)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(engineVersion);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(UnrealSR.FormatSourceFolderNotFound(sourceDirectory));
        }

        Version version = new(engineVersion.Major, engineVersion.Minor);
        List<CacheKeyFile> files = [];

        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            // Compares the extension in 1 case, because Windows and the build tool treat File.H like file.h.
            var content = Path.GetExtension(source).ToUpperInvariant() switch
            {
                ".CPP" or ".CS" or ".H" => RenderGated(source, "//@"u8, version),
                ".INI" => RenderGated(source, ";@"u8, version),
                ".UPROJECT" => RenderProjectFile(source, version),
                _ => File.ReadAllBytes(source),
            };

            if (content is { } bytes)
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, source).Replace(Path.DirectorySeparatorChar, '/');
                files.Add(new(relativePath, bytes));
            }
        }

        return files;
    }

    /// <summary>
    ///     Renders the lines of <paramref name="source"/> that <paramref name="engineVersion"/> keeps.
    /// </summary>
    /// <param name="source">The gated file.</param>
    /// <param name="marker">The bytes that open a directive line.</param>
    /// <param name="engineVersion">The engine version, as major.minor.</param>
    /// <returns>
    ///     The kept lines or <see langword="null"/> when only whitespace is left.
    /// </returns>
    private static ReadOnlyMemory<byte>? RenderGated(string source, ReadOnlySpan<byte> marker, Version engineVersion)
    {
        byte[] bytes = File.ReadAllBytes(source);

        // Starts at 1 byte or more, because ArrayBufferWriter refuses a capacity of 0 and an empty file gets here too.
        ArrayBufferWriter<byte> kept = new(Math.Max(bytes.Length, 1));
        Stack<DirectiveFrame> frames = new();
        ReadOnlySpan<byte> rest = bytes;
        int lineNumber = 0;

        while (!rest.IsEmpty)
        {
            int lineFeed = rest.IndexOf((byte)'\n');
            var line = lineFeed < 0 ? rest : rest[..(lineFeed + 1)];
            var trimmed = line.Trim(LineSpaceBytes);

            rest = rest[line.Length..];
            lineNumber++;

            if (!trimmed.StartsWith(marker))
            {
                if (frames.Count == 0 || frames.Peek().Active)
                {
                    kept.Write(line);
                }

                continue;
            }

            string directive = Encoding.UTF8.GetString(trimmed[marker.Length..]);
            int wordEnd = directive.AsSpan().IndexOfAnyExceptInRange('a', 'z');
            string word = wordEnd < 0 ? directive : directive[..wordEnd];
            string argument = directive[word.Length..].TrimStart();

            switch (word)
            {
                case "if":
                {
                    bool parentActive = frames.Count == 0 || frames.Peek().Active;
                    frames.Push(new DirectiveFrame(parentActive, IsTaken(argument, engineVersion, source, lineNumber), InElse: false, lineNumber));
                    break;
                }
                case "else" when argument.Length == 0:
                {
                    if (!frames.TryPop(out var frame))
                    {
                        throw new InvalidDataException(UnrealSR.FormatDirectiveElseWithoutIf(source, lineNumber));
                    }

                    if (frame.InElse)
                    {
                        throw new InvalidDataException(UnrealSR.FormatDirectiveElseTwice(source, lineNumber, frame.Line));
                    }

                    frames.Push(frame with { InElse = true });
                    break;
                }
                case "endif" when argument.Length == 0:
                {
                    if (!frames.TryPop(out _))
                    {
                        throw new InvalidDataException(UnrealSR.FormatDirectiveEndifWithoutIf(source, lineNumber));
                    }

                    break;
                }
                default:
                {
                    throw new InvalidDataException(UnrealSR.FormatDirectiveUnknown(source, lineNumber, directive));
                }
            }
        }

        if (frames.TryPeek(out var open))
        {
            throw new InvalidDataException(UnrealSR.FormatDirectiveUnclosed(source, open.Line));
        }

        if (!kept.WrittenSpan.ContainsAnyExcept(LineSpaceBytes))
        {
            return null;
        }

        return kept.WrittenMemory;
    }

    /// <summary>
    ///     Renders <paramref name="source"/> with major.minor of <paramref name="engineVersion"/> in its blank engine association.
    /// </summary>
    /// <param name="source">The <c>.uproject</c> file.</param>
    /// <param name="engineVersion">The engine version, as major.minor.</param>
    /// <returns>
    ///     The rendered bytes.
    /// </returns>
    private static ReadOnlyMemory<byte> RenderProjectFile(string source, Version engineVersion)
    {
        byte[] bytes = File.ReadAllBytes(source);

        int index = bytes.AsSpan().IndexOf(BlankEngineAssociation);
        if (index < 0)
        {
            return bytes;
        }

        // Splits before the closing quote, so the version goes between the 2 quotes.
        int split = index + BlankEngineAssociation.Length - 1;

        ArrayBufferWriter<byte> rendered = new(bytes.Length + 8);
        rendered.Write(bytes.AsSpan(0, split));
        rendered.Write(Encoding.UTF8.GetBytes(engineVersion.ToString()));
        rendered.Write(bytes.AsSpan(split));

        return rendered.WrittenMemory;
    }

    /// <summary>
    ///     Compares <paramref name="engineVersion"/> against the condition of an <c>@if</c> line.
    /// </summary>
    /// <param name="condition">The text after <c>@if</c>, such as <c>&gt;=4.14</c>.</param>
    /// <param name="engineVersion">The engine version, as major.minor.</param>
    /// <param name="source">The path of the file, for the message.</param>
    /// <param name="lineNumber">The number of the line, for the message.</param>
    /// <returns>
    ///     <see langword="true"/> if the condition is true for the version.
    /// </returns>
    /// <exception cref="InvalidDataException">The condition isn't an op and a major.minor.</exception>
    private static bool IsTaken(string condition, Version engineVersion, string source, int lineNumber)
    {
        var match = ConditionRegex().Match(condition);

        if (!match.Success || !Version.TryParse(match.Groups[2].ValueSpan, out var version))
        {
            throw new InvalidDataException(UnrealSR.FormatDirectiveBadCondition(source, lineNumber, condition));
        }

        int comparison = engineVersion.CompareTo(version);

        return match.Groups[1].ValueSpan switch
        {
            ">=" => comparison >= 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            "<" => comparison < 0,
            _ => comparison == 0,
        };
    }

    [GeneratedRegex(@"^(>=|>|<=|<|==)([0-9]+\.[0-9]+)$")]
    private static partial Regex ConditionRegex();
}

/// <summary>
///     Represents an open <c>@if</c> block.
/// </summary>
/// <param name="ParentActive">Whether the lines around the block are kept.</param>
/// <param name="Taken">Whether the condition of the <c>@if</c> is true.</param>
/// <param name="InElse">Whether the block is past its <c>@else</c>.</param>
/// <param name="Line">The number of the <c>@if</c> line.</param>
file readonly record struct DirectiveFrame(
    bool ParentActive,
    bool Taken,
    bool InElse,
    int Line)
{
    /// <summary>
    ///     Gets a value indicating whether the current lines of the block are kept.
    /// </summary>
    public bool Active => ParentActive && Taken != InElse;
}
