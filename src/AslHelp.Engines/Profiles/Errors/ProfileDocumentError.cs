using System.Collections.Generic;

namespace AslHelp.Engines.Profiles.Errors;

/// <summary>
///     Represents a profile document that <see cref="ProfileReader"/> can't read.
/// </summary>
internal abstract record ProfileDocumentError : ResultError
{
    /// <summary>
    ///     Creates the error for a global that isn't a locator.
    /// </summary>
    /// <param name="global">The key of the global under <c>globals</c>.</param>
    /// <param name="reason">The message of the locator converter.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileDocumentError GlobalUnreadable(string global, string reason)
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
    public static ProfileDocumentError Invalid(string reason)
    {
        return new InvalidError(reason);
    }

    /// <summary>
    ///     Creates the error for a document without parts that its engine needs.
    /// </summary>
    /// <param name="paths">The path of each missing part, like <c>offsets.Thing.Speed</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileDocumentError Missing(IReadOnlyList<string> paths)
    {
        return new MissingError([.. paths]);
    }

    /// <summary>
    ///     Creates the error for an offset below 0.
    /// </summary>
    /// <param name="path">The path of the offset, like <c>offsets.Thing.Speed</c>.</param>
    /// <param name="offset">The value of the offset.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileDocumentError OffsetNegative(string path, int offset)
    {
        return new OffsetNegativeError(path, offset);
    }

    /// <summary>
    ///     Creates the error for a file or an embedded resource whose document doesn't read.
    /// </summary>
    /// <param name="source">The file path or the resource name.</param>
    /// <param name="reason">The message of the failed read or of the error of the document.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileDocumentError SourceUnreadable(string source, string reason)
    {
        return new SourceUnreadableError(source, reason);
    }

    /// <summary>
    ///     Creates the error for keys at the root or under <c>offsets</c> that neither the reader nor the mapper of the engine read.
    /// </summary>
    /// <param name="keys">The keys in the order of the document, with the path for an offset, like <c>offsets.Thing.Sped</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileDocumentError UnknownKeys(IReadOnlyList<string> keys)
    {
        return new UnknownKeysError([.. keys]);
    }

    /// <summary>
    ///     Creates the error for a version under <c>versions</c> that the version parser of the engine doesn't read.
    /// </summary>
    /// <param name="version">The text of the version.</param>
    /// <param name="engine">The value of <c>engine</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ProfileDocumentError VersionInvalid(string version, string engine)
    {
        return new VersionInvalidError(version, engine);
    }
}

file sealed record GlobalUnreadableError(
    string Global,
    string Reason) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileGlobalUnreadable(Global, Reason);
}

file sealed record InvalidError(
    string Reason) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileInvalid(Reason);
}

file sealed record MissingError(
    IReadOnlyList<string> Paths) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileMissing(string.Join(", ", Paths));
}

file sealed record OffsetNegativeError(
    string Path,
    int Offset) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileOffsetNegative(Path, Offset);
}

file sealed record SourceUnreadableError(
    string Source,
    string Reason) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileSourceUnreadable(Source, Reason);
}

file sealed record UnknownKeysError(
    IReadOnlyList<string> Keys) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileUnknownKeys(string.Join(", ", Keys));
}

file sealed record VersionInvalidError(
    string Version,
    string Engine) : ProfileDocumentError
{
    public override string Message => SR.FormatProfileVersionInvalid(Version, Engine);
}
