using AslHelp.Engines.Profiles;

namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a step of an <see cref="UnrealGame"/> attach that failed.
/// </summary>
internal abstract record AttachError : ResultError
{
    /// <summary>
    ///     Creates the error for a global that no route of its locator finds in the module.
    /// </summary>
    /// <param name="global">The key of the global under <c>globals</c> in the profile.</param>
    /// <param name="module">The name of the module.</param>
    /// <param name="cause">The error of the locator, which lists each route with its error.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AttachError GlobalNotLocated(string global, string module, ResultError cause)
    {
        return new GlobalNotLocatedError(global, module, cause);
    }

    /// <summary>
    ///     Creates the error for a name table whose name 0 doesn't read, like the null pointer of a game that's still starting.
    /// </summary>
    /// <param name="target">The detected or passed target.</param>
    /// <param name="cause">The error of the read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AttachError NameTableNotReady(ProfileTarget target, ResultError cause)
    {
        return new NameTableNotReadyError(target, cause);
    }

    /// <summary>
    ///     Creates the error for a name table whose name 0 isn't <c>None</c>. Every build has <c>None</c> at index 0.
    /// </summary>
    /// <param name="name">The text read for name 0.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AttachError NameZeroMismatch(string name)
    {
        return new NameZeroMismatchError(name);
    }

    /// <summary>
    ///     Creates the error for a picked document that doesn't fit the game.
    /// </summary>
    /// <param name="target">The detected or passed target.</param>
    /// <param name="version">The listed version of the document that the pick went by.</param>
    /// <param name="cause">The error of the attach or of the name check.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AttachError ProfileMisfit(ProfileTarget target, ProfileVersion version, ResultError cause)
    {
        return new ProfileMisfitError(target, version, cause);
    }
}

file sealed record GlobalNotLocatedError(
    string Global,
    string Module,
    ResultError Cause) : AttachError
{
    public override string Message => SR.FormatAttachGlobalNotLocated(Global, Module, Cause.Message);
}

file sealed record NameTableNotReadyError(
    ProfileTarget Target,
    ResultError Cause) : AttachError
{
    public override string Message => SR.FormatAttachNameTableNotReady(Target, Cause.Message);
}

file sealed record NameZeroMismatchError(
    string Name) : AttachError
{
    public override string Message => SR.FormatAttachNameZeroMismatch(Name);
}

file sealed record ProfileMisfitError(
    ProfileTarget Target,
    ProfileVersion Version,
    ResultError Cause) : AttachError
{
    public override string Message => SR.FormatAttachProfileMisfit(Version, Target, Cause.Message);
}
