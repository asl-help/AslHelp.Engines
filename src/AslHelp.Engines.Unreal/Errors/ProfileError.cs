using System.Collections.Generic;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a profile document that <see cref="UnrealProfile.Parse"/> can't use.
/// </summary>
internal abstract record ProfileError : ResultError
{
    /// <summary>
    ///     Creates the error for a global that isn't a locator.
    /// </summary>
    /// <param name="global">The key of the global under <c>globals</c>.</param>
    /// <param name="reason">The message of the locator converter.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileError GlobalUnreadable(string global, string reason)
    {
        return new GlobalUnreadableError(global, reason);
    }

    /// <summary>
    ///     Creates the error for a document that isn't valid JSON or has a value that doesn't read.
    /// </summary>
    /// <param name="reason">The message of the failed read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileError Invalid(string reason)
    {
        return new InvalidError(reason);
    }

    /// <summary>
    ///     Creates the error for a document without the offsets of a value kind, which only fails reads of that kind.
    /// </summary>
    /// <param name="className">The property class of the kind, like <c>BoolProperty</c>.</param>
    /// <param name="paths">The path of each missing offset, like <c>offsets.UBoolProperty.FieldMask</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileError KindOffsetsMissing(string className, IReadOnlyList<string> paths)
    {
        return new KindOffsetsMissingError(className, [.. paths]);
    }

    /// <summary>
    ///     Creates the error for a document without parts that attach needs.
    /// </summary>
    /// <param name="paths">The path of each missing part, like <c>offsets.UField.Next</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileError Missing(IReadOnlyList<string> paths)
    {
        return new MissingError([.. paths]);
    }

    /// <summary>
    ///     Creates the error for an offset below 0.
    /// </summary>
    /// <param name="path">The path of the offset, like <c>offsets.UField.Next</c>.</param>
    /// <param name="offset">The value of the offset.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileError OffsetNegative(string path, int offset)
    {
        return new OffsetNegativeError(path, offset);
    }

    /// <summary>
    ///     Creates the error for an engine value that the library doesn't read.
    /// </summary>
    /// <param name="value">The value of <c>engine</c>.</param>
    /// <param name="known">The values the library reads, joined for the message.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileError UnknownEngine(string value, string known)
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
    public static ProfileError UnknownShape(string key, string value, string known)
    {
        return new UnknownShapeError(key, value, known);
    }
}

file sealed record GlobalUnreadableError(
    string Global,
    string Reason) : ProfileError
{
    public override string Message => SR.FormatProfileGlobalUnreadable(Global, Reason);
}

file sealed record InvalidError(
    string Reason) : ProfileError
{
    public override string Message => SR.FormatProfileInvalid(Reason);
}

file sealed record KindOffsetsMissingError(
    string ClassName,
    IReadOnlyList<string> Paths) : ProfileError
{
    public override string Message => SR.FormatProfileKindOffsetsMissing(ClassName, string.Join(", ", Paths));
}

file sealed record MissingError(
    IReadOnlyList<string> Paths) : ProfileError
{
    public override string Message => SR.FormatProfileMissing(string.Join(", ", Paths));
}

file sealed record OffsetNegativeError(
    string Path,
    int Offset) : ProfileError
{
    public override string Message => SR.FormatProfileOffsetNegative(Path, Offset);
}

file sealed record UnknownEngineError(
    string Value,
    string Known) : ProfileError
{
    public override string Message => SR.FormatProfileEngineUnknown(Value, Known);
}

file sealed record UnknownShapeError(
    string Key,
    string Value,
    string Known) : ProfileError
{
    public override string Message => SR.FormatProfileShapeUnknown(Key, Value, Known);
}
