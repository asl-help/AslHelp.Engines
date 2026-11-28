namespace AslHelp.Engines.Errors;

/// <summary>
///     Represents a failed step or read of an <see cref="IMemberPath"/>.
/// </summary>
/// <remarks>
///     An engine derives its path errors from this type, so a caller can see whether a failure is a typo or a read that can work later.
/// </remarks>
public abstract record MemberPathError : ResultError
{
    /// <summary>
    ///     Gets a value indicating whether the path doesn't fit the classes of the game, like a member that the class doesn't have
    ///     or a value read as another type.
    /// </summary>
    /// <remarks>
    ///     A shape mismatch fails every read the same way until an object on the way changes to another class.
    ///     Any other failure can work on a later read, like a null object.
    ///     A step that fails as a shape mismatch while the path doesn't read can pass once the game loads more classes, so try it again later.
    /// </remarks>
    public abstract bool IsShapeMismatch { get; }
}
