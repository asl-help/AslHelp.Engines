using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AslHelp.Engines.Measuring.Cli.Unreal3;

/// <summary>
///     Reads the UDK targets from <c>targets.json</c> or from a file in its format.
/// </summary>
internal static class UdkTargets
{
    private const int LeastFirstNames = 2;
    private const string TargetsResource = "Unreal3/targets.json";

    /// <summary>
    ///     Reads <c>targets.json</c> from the resources of this assembly.
    /// </summary>
    /// <returns>
    ///     The targets by global or a failure when the assembly does not have the resource or the resource is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UdkTarget>> ReadEmbedded()
    {
        using var json = typeof(UdkTargets).Assembly.GetManifestResourceStream(TargetsResource);

        return json is null ? UdkError.TargetsNotEmbedded(TargetsResource) : Read(json, TargetsResource);
    }

    /// <summary>
    ///     Reads the targets file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The targets by global or a failure with <paramref name="path"/> when the file does not exist, does not read
    ///     or is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UdkTarget>> ReadFile(string path)
    {
        try
        {
            using var json = File.OpenRead(path);

            return Read(json, path);
        }
        catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return UdkError.TargetsNotFound(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UdkError.FileUnreadable(path, exception.Message);
        }
    }

    /// <summary>
    ///     Reads targets in the format of <c>targets.json</c>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON object of targets by global, each with <c>$type</c>.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <returns>
    ///     The targets by global or a failure with <paramref name="path"/> when the file is not an object of targets or does not have any targets,
    ///     a target is null, lacks a key, has an unknown key, an unknown type or a blank value, a global appears twice,
    ///     the name array has too few first names or the file does not have exactly 1 name array and 1 object array.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UdkTarget>> Read(Stream json, string path)
    {
        Dictionary<string, UdkTarget>? targets;

        try
        {
            targets = JsonSerializer.Deserialize(json, UdkTargetContext.Default.DictionaryStringUdkTarget);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Catch NotSupportedException too, because a target without a type reads as the abstract record.
            return UdkError.TargetsInvalid(path, exception.Message);
        }

        if (targets is null or { Count: 0 })
        {
            return UdkError.TargetsEmpty(path);
        }

        return Validate(targets, path).And(Result.Ok<IReadOnlyDictionary<string, UdkTarget>>(targets));
    }

    /// <summary>
    ///     Refuses what the parse cannot refuse, so a bad file fails before the 1st player starts.
    /// </summary>
    /// <param name="targets">The targets by global.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <returns>
    ///     A failure when a target breaks a rule of the file.
    /// </returns>
    private static Result Validate(Dictionary<string, UdkTarget> targets, string path)
    {
        foreach ((string global, var target) in targets)
        {
            // Check for null, because the nullable annotations of the parse do not reach the values of a dictionary.
            if (target is null)
            {
                return UdkError.TargetsNull(path, global);
            }

            if (string.IsNullOrWhiteSpace(global) || HasBlankValue(target))
            {
                return UdkError.TargetsBlankValue(path, global);
            }

            if (target is UdkNameArrayTarget { FirstNames.Count: < LeastFirstNames })
            {
                return UdkError.TargetsFewFirstNames(path, global, LeastFirstNames);
            }
        }

        if (targets.Values.OfType<UdkNameArrayTarget>().Count() != 1)
        {
            return UdkError.TargetsNeedsOneNameArray(path);
        }

        // Require the object array too, because the search for every member and slot starts from it.
        if (targets.Values.OfType<UdkObjectArrayTarget>().Count() != 1)
        {
            return UdkError.TargetsNeedsOneObjectArray(path);
        }

        return Result.Ok();
    }

    private static bool HasBlankValue(UdkTarget target)
    {
        return target switch
        {
            // Check for null too, because the nullable annotations of the parse do not reach the items of a list.
            UdkNameArrayTarget nameArray => nameArray.FirstNames.Any(string.IsNullOrWhiteSpace),
            UdkObjectSlotTarget slot => string.IsNullOrWhiteSpace(slot.Class) || (slot.Object is { } name && string.IsNullOrWhiteSpace(name)),
            _ => false,
        };
    }
}
