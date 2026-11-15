using AslHelp.Engines.Measuring.Cli.Targets.Errors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AslHelp.Engines.Measuring.Cli.Targets;

/// <summary>
///     Reads the targets of any engine from its <c>targets.json</c> or from a file in that format, an object of targets by name.
/// </summary>
/// <remarks>
///     Checks the rules that every engine has (an object with targets, no null target and no blank name) and leaves the rest to the engine.
/// </remarks>
internal static class TargetsFile
{
    /// <summary>
    ///     Reads the file at <paramref name="path"/> or the resource of this assembly if <paramref name="path"/> is <see langword="null"/>.
    /// </summary>
    /// <typeparam name="TTarget">The type of a target.</typeparam>
    /// <param name="path">The path of the file or <see langword="null"/> for <paramref name="resource"/>.</param>
    /// <param name="resource">The name of the resource built into the command.</param>
    /// <param name="typeInfo">The generated type info of the targets by name.</param>
    /// <param name="validate">Checks the rules of the engine on targets that passed the common checks, with the path for the messages.</param>
    /// <returns>
    ///     The targets by name or a failure with the path or the resource.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, TTarget>> Read<TTarget>(
        string? path,
        string resource,
        JsonTypeInfo<Dictionary<string, TTarget>> typeInfo,
        Func<IReadOnlyDictionary<string, TTarget>, string, Result> validate)
    {
        return path is null ? ReadEmbedded(resource, typeInfo, validate) : ReadFile(path, typeInfo, validate);
    }

    /// <summary>
    ///     Reads the resource <paramref name="resource"/> of this assembly.
    /// </summary>
    /// <typeparam name="TTarget">The type of a target.</typeparam>
    /// <param name="resource">The name of the resource.</param>
    /// <param name="typeInfo">The generated type info of the targets by name.</param>
    /// <param name="validate">Checks the rules of the engine on targets that passed the common checks, with the path for the messages.</param>
    /// <returns>
    ///     The targets by name or a failure when the assembly does not have the resource or the resource is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, TTarget>> ReadEmbedded<TTarget>(
        string resource,
        JsonTypeInfo<Dictionary<string, TTarget>> typeInfo,
        Func<IReadOnlyDictionary<string, TTarget>, string, Result> validate)
    {
        using var json = typeof(TargetsFile).Assembly.GetManifestResourceStream(resource);

        return json is null ? TargetsError.NotEmbedded(resource) : Read(json, resource, typeInfo, validate);
    }

    /// <summary>
    ///     Reads the targets file at <paramref name="path"/>.
    /// </summary>
    /// <typeparam name="TTarget">The type of a target.</typeparam>
    /// <param name="path">The path of the file.</param>
    /// <param name="typeInfo">The generated type info of the targets by name.</param>
    /// <param name="validate">Checks the rules of the engine on targets that passed the common checks, with the path for the messages.</param>
    /// <returns>
    ///     The targets by name or a failure with <paramref name="path"/> when the file does not exist, does not read
    ///     or is not a valid set of targets.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, TTarget>> ReadFile<TTarget>(
        string path,
        JsonTypeInfo<Dictionary<string, TTarget>> typeInfo,
        Func<IReadOnlyDictionary<string, TTarget>, string, Result> validate)
    {
        try
        {
            using var json = File.OpenRead(path);

            return Read(json, path, typeInfo, validate);
        }
        catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return TargetsError.NotFound(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TargetsError.Unreadable(path, exception.Message);
        }
    }

    /// <summary>
    ///     Reads targets in the format of <c>targets.json</c>.
    /// </summary>
    /// <typeparam name="TTarget">The type of a target.</typeparam>
    /// <param name="json">The UTF-8 JSON object of targets by name.</param>
    /// <param name="path">The path of the file for the messages.</param>
    /// <param name="typeInfo">The generated type info of the targets by name.</param>
    /// <param name="validate">Checks the rules of the engine on targets that passed the common checks, with the path for the messages.</param>
    /// <returns>
    ///     The targets by name or a failure with <paramref name="path"/> when the file is not an object of targets, does not have any targets,
    ///     a target is null or has a blank name or <paramref name="validate"/> fails.
    /// </returns>
    public static Result<IReadOnlyDictionary<string, TTarget>> Read<TTarget>(
        Stream json,
        string path,
        JsonTypeInfo<Dictionary<string, TTarget>> typeInfo,
        Func<IReadOnlyDictionary<string, TTarget>, string, Result> validate)
    {
        Dictionary<string, TTarget>? targets;

        try
        {
            targets = JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            // Catch NotSupportedException too, because a target of an abstract type without a type key throws it.
            return TargetsError.Invalid(path, exception.Message);
        }

        if (targets is null or { Count: 0 })
        {
            return TargetsError.Empty(path);
        }

        foreach ((string name, var target) in targets)
        {
            // Check for null, because the nullable annotations of the parse do not reach the values of a dictionary.
            if (target is null)
            {
                return TargetsError.Null(path, name);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return TargetsError.BlankName(path);
            }
        }

        return validate(targets, path).And(Result.Ok<IReadOnlyDictionary<string, TTarget>>(targets));
    }
}
