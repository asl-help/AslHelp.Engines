using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a profile document that <see cref="UnrealProfile.Parse"/> can't use.
/// </summary>
/// <remarks>
///     The failures that the documents of every engine share come from <see cref="Profiles.ProfileReader"/>.
/// </remarks>
internal abstract record ProfileMapError : ResultError
{
    /// <summary>
    ///     Creates the error for a document without the offsets of a value kind, which only fails reads of that kind.
    /// </summary>
    /// <param name="className">The property class of the kind, like <c>BoolProperty</c>.</param>
    /// <param name="paths">The path of each missing offset, like <c>offsets.UBoolProperty.FieldMask</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileMapError KindOffsetsMissing(string className, IReadOnlyList<string> paths)
    {
        return new KindOffsetsMissingError(className, [.. paths]);
    }

    /// <summary>
    ///     Creates the error for an engine value that the library doesn't read.
    /// </summary>
    /// <param name="value">The value of <c>engine</c>.</param>
    /// <param name="known">The values the library reads, joined for the message.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileMapError UnknownEngine(string value, string known)
    {
        return new UnknownEngineError(value, known);
    }

    /// <summary>
    ///     Creates the error for a shape value that the library doesn't read.
    /// </summary>
    /// <param name="key">The key of the shape under <c>shapes</c>.</param>
    /// <param name="value">The value of the shape.</param>
    /// <param name="known">The values the library reads, joined for the message.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileMapError UnknownShape(string key, string value, string known)
    {
        return new UnknownShapeError(key, value, known);
    }
}

file sealed record KindOffsetsMissingError(
    string ClassName,
    IReadOnlyList<string> Paths) : ProfileMapError
{
    public override string Message => SR.FormatProfileKindOffsetsMissing(ClassName, string.Join(", ", Paths));
}

file sealed record UnknownEngineError(
    string Value,
    string Known) : ProfileMapError
{
    public override string Message => SR.FormatProfileEngineUnknown(Value, Known);
}

file sealed record UnknownShapeError(
    string Key,
    string Value,
    string Known) : ProfileMapError
{
    public override string Message => SR.FormatProfileShapeUnknown(Key, Value, Known);
}
