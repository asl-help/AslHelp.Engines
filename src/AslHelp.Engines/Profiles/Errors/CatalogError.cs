namespace AslHelp.Engines.Profiles.Errors;

/// <summary>
///     Represents a target that <see cref="ProfileCatalog{TProfile}.Select"/> doesn't find a profile for.
/// </summary>
internal abstract record CatalogError : ResultError
{
    /// <summary>
    ///     Creates the error for a target that no document of its engine and key lists or covers with a lower version.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CatalogError NoneFits(ProfileTarget target)
    {
        return new NoneFitsError(target);
    }

    /// <summary>
    ///     Creates the error for a target that no document lists when the caller doesn't allow a guess.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="nearest">The listed version that a guess would take.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CatalogError NotListed(ProfileTarget target, ProfileVersion nearest)
    {
        return new NotListedError(target, nearest);
    }
}

file sealed record NoneFitsError(
    ProfileTarget Target) : CatalogError
{
    public override string Message => SR.FormatCatalogNoneFits(Target, Target.Version);
}

file sealed record NotListedError(
    ProfileTarget Target,
    ProfileVersion Nearest) : CatalogError
{
    public override string Message => SR.FormatCatalogNotListed(Target, Nearest);
}
