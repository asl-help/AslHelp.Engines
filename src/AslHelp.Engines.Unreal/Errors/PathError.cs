using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed read of a member path.
/// </summary>
internal abstract record PathError : ResultError
{
    /// <summary>
    ///     Gets a value indicating whether the path doesn't fit the classes and kinds of the game, like a member that a class doesn't have.
    ///     A step returns such a failure at once. A failed memory read, like a null object, isn't one.
    /// </summary>
    public abstract bool IsMismatch { get; }

    /// <summary>
    ///     Creates the error for a path that reaches an object pointer of 0 before its end.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="segment">The segment that reads a member of the null object.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PathError NullObject(MemberPath path, int segment)
    {
        return new NullObjectError(path, segment);
    }

    /// <summary>
    ///     Creates the error for a typed read of the root of a path alone, which only reads as an <see cref="Address"/>.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="requestedType">The name of the type of the read, like <c>Int32</c>.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PathError RootWrongType(MemberPath path, string requestedType)
    {
        return new RootWrongTypeError(path, requestedType);
    }

    /// <summary>
    ///     Creates the error for a segment of a path whose read failed.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="segment">The segment that failed, 0 for the root the path starts at.</param>
    /// <param name="cause">The error of the read.</param>
    /// <param name="isMismatch">Whether the cause comes from the classes and kinds of the game and not from a memory read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PathError SegmentFailed(MemberPath path, int segment, ResultError cause, bool isMismatch)
    {
        return new SegmentFailedError(path, segment, cause, isMismatch);
    }
}

file sealed record NullObjectError(
    MemberPath Path,
    int Segment) : PathError
{
    public override bool IsMismatch => false;

    public override string Message => SR.FormatPathNullObject(Path, Path.ToString(Segment));
}

file sealed record RootWrongTypeError(
    MemberPath Path,
    string RequestedType) : PathError
{
    public override bool IsMismatch => true;

    public override string Message => SR.FormatPathRootWrongType(Path, RequestedType);
}

file sealed record SegmentFailedError(
    MemberPath Path,
    int Segment,
    ResultError Cause,
    bool IsMismatch) : PathError
{
    public override bool IsMismatch { get; } = IsMismatch;

    public override string Message => Segment + 1 == Path.SegmentCount
        ? SR.FormatPathFailed(Path, Cause.Message)
        : SR.FormatPathSegmentFailed(Path, Path.ToString(Segment + 1), Cause.Message);
}
