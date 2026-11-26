namespace AslHelp.Engines.Unreal.Errors;

/// <summary>
///     Represents a step of <see cref="UnrealGame.Attach"/> that failed.
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
}

file sealed record GlobalNotLocatedError(
    string Global,
    string Module,
    ResultError Cause) : AttachError
{
    public override string Message => SR.FormatAttachGlobalNotLocated(Global, Module, Cause.Message);
}
