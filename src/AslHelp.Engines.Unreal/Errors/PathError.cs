using AslHelp.Engines.Unreal.Paths;
using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a failed read of a member path.
/// </summary>
internal abstract record PathError : ResultError
{
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
    ///     Creates the error for a segment of a path whose read failed.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="segment">The segment that failed, 0 for the root the path starts at.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PathError SegmentFailed(MemberPath path, int segment, ResultError cause)
    {
        return new SegmentFailedError(path, segment, cause);
    }
}

file sealed record NullObjectError(
    MemberPath Path,
    int Segment) : PathError
{
    public override string Message => SR.FormatPathNullObject(Path, Path.ToString(Segment));
}

file sealed record SegmentFailedError(
    MemberPath Path,
    int Segment,
    ResultError Cause) : PathError
{
    public override string Message => Segment + 1 == Path.SegmentCount
        ? SR.FormatPathFailed(Path, Cause.Message)
        : SR.FormatPathSegmentFailed(Path, Path.ToString(Segment + 1), Cause.Message);
}
