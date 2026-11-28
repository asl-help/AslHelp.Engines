using AslHelp.Memory;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a root of a path that doesn't reach its object.
/// </summary>
internal abstract record RootError : ResultError
{
    /// <summary>
    ///     Creates the error for an object whose address now has an object of another class.
    /// </summary>
    /// <param name="className">The name of the class of the object.</param>
    /// <param name="obj">The address of the object.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RootError ObjectGone(string className, Address obj)
    {
        return new ObjectGoneError(className, obj);
    }

    /// <summary>
    ///     Creates the error for a game instance that isn't there yet or doesn't have a subsystem of the class.
    /// </summary>
    /// <param name="className">The name of the class of the subsystem.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RootError SubsystemNotFound(string className)
    {
        return new SubsystemNotFoundError(className);
    }

    /// <summary>
    ///     Creates the error for a profile without the subsystem collection of the game instance.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RootError SubsystemsUnavailable()
    {
        return new SubsystemsUnavailableError();
    }
}

file sealed record ObjectGoneError(
    string ClassName,
    Address Object) : RootError
{
    public override string Message => SR.FormatRootObjectGone(ClassName, Object);
}

file sealed record SubsystemNotFoundError(
    string ClassName) : RootError
{
    public override string Message => SR.FormatRootSubsystemNotFound(ClassName);
}

file sealed record SubsystemsUnavailableError : RootError
{
    public override string Message => SR.RootSubsystemsUnavailable;
}
