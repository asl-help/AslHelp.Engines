using AslHelp.Engines.Measuring.Cli.Targets;
using AslHelp.Engines.Measuring.Cli.Targets.Errors;
using AslHelp.Engines.Measuring.Cli.Unreal4.Errors;
using System;
using System.Collections.Generic;
using System.IO;

namespace AslHelp.Engines.Measuring.Cli.Unreal4;

/// <summary>
///     Reads the Unreal targets from <c>targets.json</c> or from a file in its format.
/// </summary>
internal static class UnrealTargets
{
    private const string TargetsResource = "Unreal4/targets.json";

    /// <summary>
    ///     Reads the targets file at <paramref name="path"/> or <c>targets.json</c> from the resources of this assembly.
    /// </summary>
    /// <param name="path">The path of the file or <see langword="null"/> for the resource.</param>
    /// <returns>
    ///     The targets by name or a failure when the file or the resource is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UnrealTarget>> Read(string? path)
    {
        return TargetsFile.Read(path, TargetsResource, UnrealTargetContext.Default.DictionaryStringUnrealTarget, Validate);
    }

    /// <summary>
    ///     Reads <c>targets.json</c> from the resources of this assembly.
    /// </summary>
    /// <returns>
    ///     The targets by name or a failure when the assembly does not have the resource or the resource is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UnrealTarget>> ReadEmbedded()
    {
        return TargetsFile.ReadEmbedded(TargetsResource, UnrealTargetContext.Default.DictionaryStringUnrealTarget, Validate);
    }

    /// <summary>
    ///     Reads the targets file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>
    ///     The targets by name or a failure with <paramref name="path"/> when the file does not exist, does not read
    ///     or is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UnrealTarget>> ReadFile(string path)
    {
        return TargetsFile.ReadFile(path, UnrealTargetContext.Default.DictionaryStringUnrealTarget, Validate);
    }

    /// <summary>
    ///     Reads targets in the format of <c>targets.json</c>.
    /// </summary>
    /// <param name="json">The UTF-8 JSON object of targets by name, each with <c>$type</c>.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <returns>
    ///     The targets by name or a failure with <paramref name="path"/> when the file is not an object of targets or does not have any targets,
    ///     a target or a member is null, lacks a key, has an unknown key, an unknown type, a blank value or a bad range, a name appears twice,
    ///     a constant doesn't have a scope or takes the report key of a member, a member is called sizeof
    ///     or a constructed range leaves the range of its global.
    ///     An old name of a member needs its end, at or after the start of the member.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, UnrealTarget>> Read(Stream json, string path)
    {
        return TargetsFile.Read(json, path, UnrealTargetContext.Default.DictionaryStringUnrealTarget, Validate);
    }

    /// <summary>
    ///     Checks the ranges, blank values, null members and report keys that the parse lets through, so a bad file fails before the 1st
    ///     player gets read.
    /// </summary>
    /// <param name="targets">The targets by name, none of them null.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <returns>
    ///     A failure when a target breaks a rule of the file.
    /// </returns>
    private static Result Validate(IReadOnlyDictionary<string, UnrealTarget> targets, string path)
    {
        foreach ((string name, var target) in targets)
        {
            if (HasBlankValue(target))
            {
                return TargetsError.BlankValue(path, name);
            }

            if (!IsRange(target.From, target.FromMinor, target.Until, target.UntilMinor))
            {
                return UnrealTargetsError.TargetsBadRange(path, name, target.From, target.Until);
            }

            // Refuse a constant without a scope, because the report lists each value under a type.
            if (target is UnrealConstantTarget && UnrealConstantTarget.ReportKey(name) is null)
            {
                return UnrealTargetsError.TargetsConstantUnscoped(path, name);
            }

            if (target is UnrealGlobalTarget global && ValidateConstructed(global, name, path).TryUnwrapErr(out var constructedError))
            {
                return constructedError;
            }

            if (target is UnrealLayoutTarget layout && ValidateMembers(layout, name, path).TryUnwrapErr(out var membersError))
            {
                return membersError;
            }
        }

        return ValidateReportKeys(targets, path);
    }

    private static Result ValidateConstructed(UnrealGlobalTarget global, string name, string path)
    {
        if (!IsConstructedRange(global))
        {
            return UnrealTargetsError.TargetsBadRange(path, name, global.ConstructedFrom, global.ConstructedUntil);
        }

        // Refuse a constructed range past the range of the global, because the measurement never reaches a version outside it.
        bool inside = global.ConstructedFromMinor is null
            || ((global.FromMinor is null || global.FromMinor <= global.ConstructedFromMinor)
                && (global.UntilMinor is null || global.ConstructedUntilMinor <= global.UntilMinor));

        return inside
            ? Result.Ok()
            : UnrealTargetsError.TargetsConstructedOutside(path, name, global);
    }

    private static Result ValidateMembers(UnrealLayoutTarget layout, string type, string path)
    {
        foreach ((string member, var range) in layout.Members)
        {
            string name = $"{type}.{member}";

            if (range is null)
            {
                return TargetsError.Null(path, name);
            }

            if (string.IsNullOrWhiteSpace(member) || (range.OldName is { } oldName && string.IsNullOrWhiteSpace(oldName)))
            {
                return TargetsError.BlankValue(path, name);
            }

            // Refuse the name of the size, because the report lists the size of each type under it.
            if (member == UnrealLayoutTarget.SizeofMember)
            {
                return UnrealTargetsError.TargetsMemberReserved(path, name);
            }

            // Refuse a member range outside the range of its type too, because the measurement never reaches a version outside it.
            if (!IsRange(range.From, range.FromMinor, range.Until, range.UntilMinor) || !Overlaps(layout, range))
            {
                return UnrealTargetsError.TargetsBadRange(path, name, range.From, range.Until);
            }

            if (!IsOldNameRange(range))
            {
                return UnrealTargetsError.TargetsBadRange(path, name, range.From, range.OldNameUntil);
            }
        }

        return Result.Ok();
    }

    // Refuses a constant whose report key is also a member or the size of a layout, because the report has 1 value per key.
    private static Result ValidateReportKeys(IReadOnlyDictionary<string, UnrealTarget> targets, string path)
    {
        HashSet<string> memberKeys = [with(StringComparer.Ordinal)];

        foreach ((string type, var target) in targets)
        {
            if (target is UnrealLayoutTarget layout)
            {
                _ = memberKeys.Add($"{type}.{UnrealLayoutTarget.SizeofMember}");

                foreach (string member in layout.Members.Keys)
                {
                    _ = memberKeys.Add($"{type}.{member}");
                }
            }
        }

        foreach ((string name, var target) in targets)
        {
            if (target is UnrealConstantTarget && UnrealConstantTarget.ReportKey(name) is { } key && memberKeys.Contains(key))
            {
                return UnrealTargetsError.TargetsReportKeyTaken(path, name, key);
            }
        }

        return Result.Ok();
    }

    private static bool HasBlankValue(UnrealTarget target)
    {
        return target switch
        {
            UnrealFunctionStaticTarget function => string.IsNullOrWhiteSpace(function.Function),
            UnrealGlobalTarget { ConstructedType: { } type } => string.IsNullOrWhiteSpace(type),
            _ => false,
        };
    }

    private static bool IsConstructedRange(UnrealGlobalTarget global)
    {
        // Take both ends or none of them, because a range open at 1 end is the same as a shorter from or until.
        // A type without ends goes through its constructor on every version of the target, such as a static that never has a public.
        return (global.ConstructedType is not null || global.ConstructedFrom is null)
            && (global.ConstructedFrom is null) == (global.ConstructedUntil is null)
            && IsRange(global.ConstructedFrom, global.ConstructedFromMinor, global.ConstructedUntil, global.ConstructedUntilMinor);
    }

    private static bool Overlaps(UnrealLayoutTarget layout, UnrealMemberTarget member)
    {
        return (member.FromMinor is null || layout.UntilMinor is null || member.FromMinor <= layout.UntilMinor)
            && (layout.FromMinor is null || member.UntilMinor is null || layout.FromMinor <= member.UntilMinor);
    }

    private static bool IsOldNameRange(UnrealMemberTarget member)
    {
        // Take the old name with its end or none of them, because an old name without an end would replace the name on every version.
        return (member.OldName is null) == (member.OldNameUntil is null)
            && IsRange(member.From, member.FromMinor, member.OldNameUntil, member.OldNameUntilMinor);
    }

    // Takes the text of each end with its parsed value, because a text that doesn't parse has a null value like an open end.
    private static bool IsRange(string? fromText, Version? from, string? untilText, Version? until)
    {
        return (fromText is null || from is not null)
            && (untilText is null || until is not null)
            && (from is null || until is null || from <= until);
    }
}
