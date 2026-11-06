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
    ///     The targets by global.
    /// </returns>
    /// <exception cref="FileNotFoundException">The assembly has no <c>targets.json</c> resource.</exception>
    /// <exception cref="InvalidDataException">The resource is not a valid set of targets.</exception>
    public static IReadOnlyDictionary<string, UdkTarget> ReadEmbedded()
    {
        using var json = typeof(UdkTargets).Assembly.GetManifestResourceStream(TargetsResource)
            ?? throw new FileNotFoundException(UdkSR.FormatTargetsNotEmbedded(TargetsResource), TargetsResource);

        return Read(json, TargetsResource);
    }

    /// <summary>
    ///     Reads the targets file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The targets by global.
    /// </returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid set of targets. The message has <paramref name="path"/>.</exception>
    public static IReadOnlyDictionary<string, UdkTarget> ReadFile(string path)
    {
        using var json = File.OpenRead(path);

        return Read(json, path);
    }

    /// <summary>
    ///     Reads targets in the format of <c>targets.json</c>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON object of targets by global, each with <c>$type</c>.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <returns>
    ///     The targets by global.
    /// </returns>
    /// <exception cref="InvalidDataException">
    ///     The file is not an object of targets or has no targets. A target is null, lacks a key, has an unknown key, an unknown type or a
    ///     blank value. A global appears twice, the name array has too few first names or the file does not have exactly 1 name array and
    ///     1 object array. The message has <paramref name="path"/>.
    /// </exception>
    public static IReadOnlyDictionary<string, UdkTarget> Read(Stream json, string path)
    {
        Dictionary<string, UdkTarget>? targets;

        try
        {
            targets = JsonSerializer.Deserialize(json, UdkTargetContext.Default.DictionaryStringUdkTarget);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Catch NotSupportedException too, because a target without a type reads as the abstract record.
            throw new InvalidDataException(UdkSR.FormatTargetsInvalid(path, exception.Message), exception);
        }

        if (targets is null or { Count: 0 })
        {
            throw new InvalidDataException(UdkSR.FormatTargetsEmpty(path));
        }

        Validate(targets, path);

        return targets;
    }

    /// <summary>
    ///     Refuses what the parse cannot refuse, so a bad file fails before the 1st player starts.
    /// </summary>
    /// <param name="targets">The targets by global.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <exception cref="InvalidDataException">A target breaks a rule of the file.</exception>
    private static void Validate(Dictionary<string, UdkTarget> targets, string path)
    {
        foreach ((string global, var target) in targets)
        {
            // Check for null, because the nullable annotations of the parse do not reach the values of a dictionary.
            if (target is null)
            {
                throw new InvalidDataException(UdkSR.FormatTargetsNull(path, global));
            }

            if (string.IsNullOrWhiteSpace(global) || HasBlankValue(target))
            {
                throw new InvalidDataException(UdkSR.FormatTargetsBlankValue(path, global));
            }

            if (target is UdkNameArrayTarget { FirstNames.Count: < LeastFirstNames })
            {
                throw new InvalidDataException(UdkSR.FormatTargetsFewFirstNames(path, global, LeastFirstNames));
            }
        }

        if (targets.Values.OfType<UdkNameArrayTarget>().Count() != 1)
        {
            throw new InvalidDataException(UdkSR.FormatTargetsNeedsOneNameArray(path));
        }

        // Require the object array too, because the search for every member and slot starts from it.
        if (targets.Values.OfType<UdkObjectArrayTarget>().Count() != 1)
        {
            throw new InvalidDataException(UdkSR.FormatTargetsNeedsOneObjectArray(path));
        }
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
